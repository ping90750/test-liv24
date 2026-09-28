using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OrderProcessing.Domain;
using OrderProcessing.Infrastructure.Persistence;

namespace OrderProcessing.Application.Jobs;

public sealed class OutboxOptions
{
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(2);

    public int BatchSize { get; set; } = 100;

    public int MaxAttempts { get; set; } = 10;
}

/// <summary>
/// Delivers outbox messages at least once. An email failure is retried here and never
/// reaches the customer as a failed order (the old code turned it into a 500 and a retry-charge).
/// </summary>
public sealed class OutboxProcessor(
    AppDbContext db,
    IEmailSender emailSender,
    TimeProvider clock,
    IOptions<OutboxOptions> options,
    ILogger<OutboxProcessor> logger) : IPeriodicJob
{
    public async Task RunAsync(CancellationToken ct)
    {
        var settings = options.Value;

        // With several app instances, claim rows with UPDLOCK/READPAST (SQL Server)
        // or FOR UPDATE SKIP LOCKED (PostgreSQL) so each message has one owner.
        var messages = await db.OutboxMessages
            .Where(m => m.ProcessedAt == null && m.Attempts < settings.MaxAttempts)
            .OrderBy(m => m.Id)
            .Take(settings.BatchSize)
            .ToListAsync(ct);

        foreach (var message in messages)
        {
            try
            {
                await DispatchAsync(message, ct);
                message.MarkProcessed(clock.GetUtcNow().UtcDateTime);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Outbox message {MessageId} ({Type}) failed", message.Id, message.Type);
                message.MarkFailed(ex.Message);
            }

            await db.SaveChangesAsync(ct);
        }
    }

    private async Task DispatchAsync(OutboxMessage message, CancellationToken ct)
    {
        switch (message.Type)
        {
            case nameof(OrderPaid):
                var paid = message.Deserialize<OrderPaid>();
                var email = await db.Orders
                    .Where(o => o.Id == paid.OrderId)
                    .Join(db.Users, o => o.UserId, u => u.Id, (_, u) => u.Email)
                    .FirstAsync(ct);
                await emailSender.SendAsync(email, "Order completed", $"Your order {paid.OrderId} has been paid.", ct);
                break;

            default:
                throw new InvalidOperationException($"Unknown outbox message type '{message.Type}'.");
        }
    }
}
