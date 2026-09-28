using OrderProcessing.Domain;

namespace OrderProcessing.Application;

public sealed record PlaceOrderCommand(
    int UserId,
    IReadOnlyList<OrderLine>? Items,
    string? CouponCode,
    string IdempotencyKey);

public sealed record OrderResult(Guid OrderId, OrderStatus Status, decimal TotalAmount, string? FailureReason)
{
    public static OrderResult From(Order order) =>
        new(order.Id, order.Status, order.TotalAmount, order.FailureReason);
}

/// <summary>Outbox message published when an order is paid.</summary>
public sealed record OrderPaid(Guid OrderId);
