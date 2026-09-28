using System.Collections.Concurrent;
using OrderProcessing.Application;

namespace OrderProcessing.Infrastructure;

/// <summary>
/// In-memory gateway for local runs. It honours idempotency keys the way real
/// gateways (Stripe, Omise, 2C2P, ...) do: the same key returns the first result.
/// </summary>
public sealed class FakePaymentGateway : IPaymentGateway
{
    public const string DeclinedToken = "tok_declined";

    private readonly ConcurrentDictionary<string, PaymentResult> _charges = new();

    public Task<PaymentResult> ChargeAsync(ChargeRequest request, CancellationToken ct) =>
        Task.FromResult(_charges.GetOrAdd(request.IdempotencyKey, _ =>
            request.PaymentMethodToken == DeclinedToken
                ? PaymentResult.Decline("Card declined")
                : PaymentResult.Success($"txn_{Guid.NewGuid():N}")));

    public Task<PaymentResult> GetStatusAsync(string idempotencyKey, CancellationToken ct) =>
        Task.FromResult(_charges.TryGetValue(idempotencyKey, out var result) ? result : PaymentResult.NotFound());
}
