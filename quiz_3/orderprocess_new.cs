// Refactor of orderprocess.cs — keeps the original OrderService / ProcessOrderAsync flow,
// fixes the problems in place. Sketch only: entities, DbContext and DI are the same as before.
//
// [1] Result object instead of plain strings
// [2] Validate quantity, merge duplicate products
// [3] Load all products in one query (no N+1, no second lookup)
// [4] Coupons from the database, magic numbers as named constants
// [5] Reserve stock with one atomic UPDATE inside a transaction (no oversell)
// [6] Save order + items in the same transaction as the stock reservation
// [7] Charge outside the transaction, with token + idempotency key
// [8] Payment failed -> release stock; payment exception -> PaymentUnknown (reconciled later)
// [9] Email failure no longer fails a paid order

public enum OrderStatus { Pending, Completed, PaymentFailed, PaymentUnknown }

public record OrderResult(bool Success, string? Error = null, int? OrderId = null)
{
    public static OrderResult Ok(int orderId) => new(true, OrderId: orderId);
    public static OrderResult Fail(string error) => new(false, error);
}

public class OrderService
{
    private const decimal BulkDiscountThreshold = 50000m;
    private const decimal BulkDiscountAmount = 2000m;

    private readonly AppDbContext _db;
    private readonly IPaymentGateway _paymentGateway;   // interfaces -> mockable in unit tests
    private readonly IEmailService _emailService;
    private readonly ILogger<OrderService> _logger;

    public OrderService(
        AppDbContext db,
        IPaymentGateway paymentGateway,
        IEmailService emailService,
        ILogger<OrderService> logger)
    {
        _db = db;
        _paymentGateway = paymentGateway;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task<OrderResult> ProcessOrderAsync(CreateOrderRequest request, CancellationToken ct = default)
    {
        // Retried request (same IdempotencyKey) -> return the existing order, don't charge twice.
        var existing = await _db.Orders.AsNoTracking()
            .FirstOrDefaultAsync(o => o.UserId == request.UserId && o.IdempotencyKey == request.IdempotencyKey, ct);
        if (existing != null)
            return OrderResult.Ok(existing.Id);

        var user = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == request.UserId && u.IsActive, ct);
        if (user == null)
            return OrderResult.Fail("Invalid user");

        if (request.Items == null || !request.Items.Any())
            return OrderResult.Fail("Empty order");

        // [2]
        if (request.Items.Any(i => i.Quantity <= 0))
            return OrderResult.Fail("Invalid quantity");

        var items = request.Items
            .GroupBy(i => i.ProductId)
            .Select(g => new { ProductId = g.Key, Quantity = g.Sum(i => i.Quantity) })
            .OrderBy(i => i.ProductId)                     // same lock order for every request -> no deadlock
            .ToList();

        // [3]
        var productIds = items.Select(i => i.ProductId).ToList();
        var products = await _db.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);

        if (products.Count != items.Count)
            return OrderResult.Fail("Product not found");

        decimal subtotal = items.Sum(i => products[i.ProductId].Price * i.Quantity);

        // [4]
        var total = await CalculateTotalAsync(subtotal, request.CouponCode, ct);
        if (total == null)
            return OrderResult.Fail("Invalid coupon");

        var order = new Order
        {
            UserId = user.Id,
            IdempotencyKey = request.IdempotencyKey,
            CreatedAt = DateTime.UtcNow,
            TotalAmount = total.Value,
            Status = OrderStatus.Pending,
            Items = items.Select(i => new OrderItem
            {
                ProductId = i.ProductId,
                Quantity = i.Quantity,
                UnitPrice = products[i.ProductId].Price,   // price snapshot at order time
            }).ToList(),
        };

        // [5] + [6]
        await using (var tx = await _db.Database.BeginTransactionAsync(ct))
        {
            foreach (var item in items)
            {
                // UPDATE Products SET Stock = Stock - @qty WHERE Id = @id AND Stock >= @qty
                var updated = await _db.Products
                    .Where(p => p.Id == item.ProductId && p.Stock >= item.Quantity)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.Stock, p => p.Stock - item.Quantity), ct);

                if (updated == 0)
                {
                    await tx.RollbackAsync(ct);            // undo lines already reserved
                    return OrderResult.Fail("Insufficient stock");
                }
            }

            _db.Orders.Add(order);
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        // [7] From here on use CancellationToken.None: stock is reserved, so finish the order
        //     even if the client disconnects.
        PaymentResult paymentResult;
        try
        {
            paymentResult = await _paymentGateway.ChargeAsync(
                user.PaymentToken,                        // token from the payment provider, not a raw card number
                order.TotalAmount,
                idempotencyKey: $"order-{order.Id}",
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            // [8] Timeout / network error: we don't know if money moved.
            //     A background job checks the gateway later and completes or fails the order.
            _logger.LogWarning(ex, "Payment for order {OrderId} has an unknown result", order.Id);
            order.Status = OrderStatus.PaymentUnknown;
            await _db.SaveChangesAsync(CancellationToken.None);
            return OrderResult.Fail("Payment pending");
        }

        if (!paymentResult.Success)
        {
            // [8] Return the stock and mark the order failed together.
            await using var tx = await _db.Database.BeginTransactionAsync(CancellationToken.None);
            await ReleaseStockAsync(order.Items, CancellationToken.None);
            order.Status = OrderStatus.PaymentFailed;
            await _db.SaveChangesAsync(CancellationToken.None);
            await tx.CommitAsync(CancellationToken.None);
            return OrderResult.Fail("Payment failed");
        }

        order.Status = OrderStatus.Completed;
        await _db.SaveChangesAsync(CancellationToken.None);

        // [9] The order is paid; an email problem must not turn it into an error.
        //     (At scale: write an outbox row in the same SaveChanges and send it from a worker.)
        try
        {
            await _emailService.SendAsync(user.Email, "Order completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send confirmation email for order {OrderId}", order.Id);
        }

        return OrderResult.Ok(order.Id);
    }

    /// <returns>Total after discounts, or null when the coupon is unknown/expired.</returns>
    private async Task<decimal?> CalculateTotalAsync(decimal subtotal, string? couponCode, CancellationToken ct)
    {
        decimal total = subtotal;

        // Was: if (CouponCode == "DIS10") ... else if (CouponCode == "DIS20") ...
        if (!string.IsNullOrEmpty(couponCode))
        {
            var coupon = await _db.Coupons.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Code == couponCode && c.IsActive, ct);
            if (coupon == null)
                return null;

            total *= 1 - coupon.PercentOff / 100m;
        }

        if (total > BulkDiscountThreshold)
            total -= BulkDiscountAmount;

        return Math.Round(total, 2, MidpointRounding.AwayFromZero);
    }

    private async Task ReleaseStockAsync(IEnumerable<OrderItem> items, CancellationToken ct)
    {
        foreach (var item in items.OrderBy(i => i.ProductId))
        {
            await _db.Products
                .Where(p => p.Id == item.ProductId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Stock, p => p.Stock + item.Quantity), ct);
        }
    }
}

// New / changed members on existing types (not written out in full):
//   CreateOrderRequest.IdempotencyKey  string   (sent by the client, e.g. Idempotency-Key header)
//   Order.IdempotencyKey               string   unique index on (UserId, IdempotencyKey)
//   Order.Items                        List<OrderItem>  (ProductId, Quantity, UnitPrice)
//   Order.Status                       OrderStatus instead of string
//   User.PaymentToken                  replaces User.CardNumber (PCI-DSS)
//   Coupon                             Code, PercentOff, IsActive  (table instead of hard-coded codes)
//   Products                           CHECK (Stock >= 0)
//   IPaymentGateway.ChargeAsync(token, amount, idempotencyKey, ct)
