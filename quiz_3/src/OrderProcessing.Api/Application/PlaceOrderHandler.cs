using Microsoft.EntityFrameworkCore;
using OrderProcessing.Application.Pricing;
using OrderProcessing.Common;
using OrderProcessing.Domain;
using OrderProcessing.Infrastructure.Persistence;

namespace OrderProcessing.Application;

/// <summary>
/// Orchestrates placing an order. Pricing, stock, payment and notification rules live in
/// their own services; this class only decides the order in which they run.
/// </summary>
public sealed class PlaceOrderHandler(
    AppDbContext db,
    IInventoryService inventory,
    IPricingService pricing,
    IPaymentGateway paymentGateway,
    OrderPaymentFinalizer finalizer,
    TimeProvider clock,
    ILogger<PlaceOrderHandler> logger)
{
    public async Task<Result<OrderResult>> HandleAsync(PlaceOrderCommand command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.IdempotencyKey))
            return OrderErrors.MissingIdempotencyKey;

        // 1) A retried request gets the original order back instead of a second charge.
        var existing = await FindByIdempotencyKeyAsync(command.UserId, command.IdempotencyKey, ct);
        if (existing is not null)
            return OrderResult.From(existing);

        // 2) Validate.
        var user = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == command.UserId && u.IsActive, ct);
        if (user is null)
            return OrderErrors.InvalidUser;

        if (command.Items is not { Count: > 0 })
            return OrderErrors.EmptyOrder;

        if (command.Items.Any(i => i.Quantity <= 0))
            return OrderErrors.InvalidQuantity;

        // Merge repeated products so stock is checked against the total quantity,
        // and sort by id so concurrent orders lock rows in the same order (no deadlocks).
        var lines = command.Items
            .GroupBy(i => i.ProductId)
            .Select(g => new OrderLine(g.Key, g.Sum(i => i.Quantity)))
            .OrderBy(l => l.ProductId)
            .ToList();

        var productIds = lines.Select(l => l.ProductId).ToList();
        var products = await db.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);
        if (products.Count != lines.Count)
            return OrderErrors.ProductNotFound;

        // 3) Price from the database, never from the client.
        var pricedLines = lines
            .Select(l => new PricedLine(l.ProductId, products[l.ProductId].Name, products[l.ProductId].Price, l.Quantity))
            .ToList();
        var price = await pricing.CalculateAsync(pricedLines, command.CouponCode, ct);
        if (price.IsFailure)
            return price.Error;

        // 4) Reserve stock and save the order in one transaction.
        var order = Order.Create(
            user.Id, command.IdempotencyKey, pricedLines, price.Value, command.CouponCode, clock.GetUtcNow().UtcDateTime);

        var created = await TryCreateOrderAsync(order, lines, ct);
        if (created.IsFailure)
            return created.Error;

        if (!created.Value)
        {
            // A concurrent request with the same key won the race.
            var winner = await FindByIdempotencyKeyAsync(command.UserId, command.IdempotencyKey, ct);
            return OrderResult.From(winner!);
        }

        // 5) Charge outside the transaction. From here on we finish even if the client
        //    disconnects, so the order never stops half-way.
        var payment = await ChargeAsync(order, user.PaymentMethodToken);
        try
        {
            await finalizer.ApplyAsync(order, payment, CancellationToken.None);
            return OrderResult.From(order);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Reconciliation finalized the order first; report what it decided.
            db.ChangeTracker.Clear();
            var current = await db.Orders.AsNoTracking().FirstAsync(o => o.Id == order.Id, CancellationToken.None);
            return OrderResult.From(current);
        }
    }

    /// <returns>true if created; false if another request already saved an order with this key.</returns>
    private async Task<Result<bool>> TryCreateOrderAsync(Order order, IReadOnlyList<OrderLine> lines, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        if (!await inventory.TryReserveAsync(lines, ct))
        {
            await transaction.RollbackAsync(ct);
            return OrderErrors.InsufficientStock;
        }

        db.Orders.Add(order);
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return true;
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(ct);
            db.ChangeTracker.Clear();

            // The unique (UserId, IdempotencyKey) index rejected a duplicate. Any other failure is rethrown.
            if (await FindByIdempotencyKeyAsync(order.UserId, order.IdempotencyKey, ct) is null)
                throw;

            return false;
        }
    }

    private async Task<PaymentResult> ChargeAsync(Order order, string paymentMethodToken)
    {
        try
        {
            // The order id is the gateway idempotency key, so a retried charge can't take money twice.
            var request = new ChargeRequest(order.Id.ToString(), paymentMethodToken, order.TotalAmount);
            return await paymentGateway.ChargeAsync(request, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Timeouts and transport errors don't tell us whether money moved: reconciliation decides.
            logger.LogWarning(ex, "Charge for order {OrderId} ended with an unknown outcome", order.Id);
            return PaymentResult.Unknown(ex.Message);
        }
    }

    private Task<Order?> FindByIdempotencyKeyAsync(int userId, string idempotencyKey, CancellationToken ct) =>
        db.Orders.AsNoTracking()
            .FirstOrDefaultAsync(o => o.UserId == userId && o.IdempotencyKey == idempotencyKey, ct);
}
