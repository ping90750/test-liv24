using OrderProcessing.Domain;

namespace OrderProcessing.Application;

public interface IInventoryService
{
    /// <summary>
    /// Atomically reserves every line or reports failure. Must run inside a transaction
    /// so a failure part-way through rolls back the lines already reserved.
    /// </summary>
    Task<bool> TryReserveAsync(IReadOnlyList<OrderLine> lines, CancellationToken ct);

    Task ReleaseAsync(IReadOnlyList<OrderLine> lines, CancellationToken ct);
}

public interface ICouponProvider
{
    Task<Coupon?> FindAsync(string code, CancellationToken ct);
}

public interface IEmailSender
{
    Task SendAsync(string to, string subject, string body, CancellationToken ct);
}

public enum PaymentStatus
{
    Succeeded,
    Declined,

    /// <summary>Timeout or transport error: the charge may or may not have happened.</summary>
    Unknown,

    /// <summary>The gateway has no charge for this idempotency key.</summary>
    NotFound,
}

public sealed record ChargeRequest(string IdempotencyKey, string PaymentMethodToken, decimal Amount);

public sealed record PaymentResult(PaymentStatus Status, string? TransactionId = null, string? Reason = null)
{
    public static PaymentResult Success(string transactionId) => new(PaymentStatus.Succeeded, transactionId);

    public static PaymentResult Decline(string reason) => new(PaymentStatus.Declined, Reason: reason);

    public static PaymentResult Unknown(string reason) => new(PaymentStatus.Unknown, Reason: reason);

    public static PaymentResult NotFound() => new(PaymentStatus.NotFound, Reason: "No charge found at the payment gateway");
}

public interface IPaymentGateway
{
    /// <summary>
    /// Charges once per <see cref="ChargeRequest.IdempotencyKey"/>; repeating a key returns the original result.
    /// </summary>
    Task<PaymentResult> ChargeAsync(ChargeRequest request, CancellationToken ct);

    Task<PaymentResult> GetStatusAsync(string idempotencyKey, CancellationToken ct);
}
