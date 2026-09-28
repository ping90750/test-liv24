using OrderProcessing.Domain;
using OrderProcessing.Infrastructure.Persistence;

namespace OrderProcessing.Application;

/// <summary>
/// Applies a payment outcome to an order. Used by the request path and by
/// <c>PaymentReconciler</c> so both follow exactly the same rules.
/// </summary>
public sealed class OrderPaymentFinalizer(AppDbContext db, IInventoryService inventory, TimeProvider clock)
{
    /// <exception cref="Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException">
    /// Another process changed the order first.
    /// </exception>
    public async Task ApplyAsync(Order order, PaymentResult result, CancellationToken ct)
    {
        if (result.Status == PaymentStatus.Unknown && order.Status == OrderStatus.PaymentUnknown)
            return; // Still unknown; try again on the next reconciliation run.

        var now = clock.GetUtcNow().UtcDateTime;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        switch (result.Status)
        {
            case PaymentStatus.Succeeded:
                order.MarkPaid(result.TransactionId ?? throw new InvalidOperationException("A successful charge has no transaction id."), now);
                db.OutboxMessages.Add(OutboxMessage.Create(new OrderPaid(order.Id), now));
                break;

            case PaymentStatus.Declined:
            case PaymentStatus.NotFound:
                order.MarkPaymentFailed(result.Reason ?? "Payment declined", now);
                var lines = order.Items.Select(i => new OrderLine(i.ProductId, i.Quantity)).ToList();
                await inventory.ReleaseAsync(lines, ct);
                break;

            case PaymentStatus.Unknown:
                order.MarkPaymentUnknown(now);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(result), result.Status, "Unsupported payment status.");
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}
