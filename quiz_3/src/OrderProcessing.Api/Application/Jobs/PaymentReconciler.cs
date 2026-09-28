using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OrderProcessing.Domain;
using OrderProcessing.Infrastructure.Persistence;

namespace OrderProcessing.Application.Jobs;

public sealed class ReconciliationOptions
{
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Must be longer than the gateway timeout so in-flight requests are left alone.</summary>
    public TimeSpan StaleAfter { get; set; } = TimeSpan.FromMinutes(5);

    public int BatchSize { get; set; } = 100;
}

/// <summary>
/// Settles orders whose payment outcome is unknown (gateway timeout) or that stopped
/// before charging (process crash), by asking the gateway what really happened.
/// </summary>
public sealed class PaymentReconciler(
    AppDbContext db,
    IPaymentGateway paymentGateway,
    OrderPaymentFinalizer finalizer,
    TimeProvider clock,
    IOptions<ReconciliationOptions> options,
    ILogger<PaymentReconciler> logger) : IPeriodicJob
{
    public async Task RunAsync(CancellationToken ct)
    {
        var settings = options.Value;
        var cutoff = clock.GetUtcNow().UtcDateTime - settings.StaleAfter;

        var staleOrderIds = await db.Orders
            .Where(o => (o.Status == OrderStatus.PendingPayment || o.Status == OrderStatus.PaymentUnknown)
                        && o.UpdatedAt < cutoff)
            .OrderBy(o => o.UpdatedAt)
            .Select(o => o.Id)
            .Take(settings.BatchSize)
            .ToListAsync(ct);

        foreach (var orderId in staleOrderIds)
        {
            // Fresh tracking per order so one failure can't leak pending changes into the next.
            db.ChangeTracker.Clear();
            try
            {
                var order = await db.Orders.Include(o => o.Items).FirstAsync(o => o.Id == orderId, ct);
                var result = await paymentGateway.GetStatusAsync(order.Id.ToString(), ct);
                await finalizer.ApplyAsync(order, result, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Reconciliation failed for order {OrderId}", orderId);
            }
        }
    }
}
