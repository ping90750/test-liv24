using OrderProcessing.Application;
using OrderProcessing.Domain;

namespace OrderProcessing.Tests.Support;

internal sealed class StubPaymentGateway : IPaymentGateway
{
    private int _chargeCount;

    public int ChargeCount => Volatile.Read(ref _chargeCount);

    public Func<ChargeRequest, PaymentResult> OnCharge { get; set; } =
        request => PaymentResult.Success($"txn_{request.IdempotencyKey}");

    public Func<string, PaymentResult> OnGetStatus { get; set; } = _ => PaymentResult.NotFound();

    public Task<PaymentResult> ChargeAsync(ChargeRequest request, CancellationToken ct)
    {
        Interlocked.Increment(ref _chargeCount);
        return Task.FromResult(OnCharge(request));
    }

    public Task<PaymentResult> GetStatusAsync(string idempotencyKey, CancellationToken ct) =>
        Task.FromResult(OnGetStatus(idempotencyKey));
}

internal sealed class RecordingEmailSender : IEmailSender
{
    public List<(string To, string Subject)> Sent { get; } = [];

    public Func<string, Exception?> FailWith { get; set; } = _ => null;

    public Task SendAsync(string to, string subject, string body, CancellationToken ct)
    {
        if (FailWith(to) is { } error)
            throw error;

        Sent.Add((to, subject));
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryCouponProvider(params Coupon[] coupons) : ICouponProvider
{
    public Task<Coupon?> FindAsync(string code, CancellationToken ct) =>
        Task.FromResult(coupons.FirstOrDefault(c => c.Code == code));
}
