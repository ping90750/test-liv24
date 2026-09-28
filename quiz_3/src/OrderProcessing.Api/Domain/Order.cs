namespace OrderProcessing.Domain;

public class Order
{
    private readonly List<OrderItem> _items = [];

    private Order()
    {
    }

    public Guid Id { get; private set; }

    public int UserId { get; private set; }

    /// <summary>Client-supplied key; unique per user so retries never create a second order.</summary>
    public string IdempotencyKey { get; private set; } = null!;

    public decimal Subtotal { get; private set; }

    public decimal Discount { get; private set; }

    public decimal TotalAmount { get; private set; }

    public string? CouponCode { get; private set; }

    public OrderStatus Status { get; private set; }

    public string? PaymentTransactionId { get; private set; }

    public string? FailureReason { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime UpdatedAt { get; private set; }

    /// <summary>Optimistic concurrency token: the request and the reconciliation job can't both finalize an order.</summary>
    public int Version { get; private set; }

    public IReadOnlyList<OrderItem> Items => _items;

    public static Order Create(
        int userId,
        string idempotencyKey,
        IReadOnlyList<PricedLine> lines,
        PriceBreakdown price,
        string? couponCode,
        DateTime now)
    {
        if (lines.Count == 0)
            throw new ArgumentException("An order needs at least one item.", nameof(lines));

        var order = new Order
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            IdempotencyKey = idempotencyKey,
            Subtotal = price.Subtotal,
            Discount = price.Discount,
            TotalAmount = price.Total,
            CouponCode = couponCode,
            Status = OrderStatus.PendingPayment,
            CreatedAt = now,
            UpdatedAt = now,
        };
        order._items.AddRange(lines.Select(line => new OrderItem(order.Id, line)));
        return order;
    }

    public void MarkPaid(string transactionId, DateTime now)
    {
        EnsureStatus(OrderStatus.PendingPayment, OrderStatus.PaymentUnknown);
        Status = OrderStatus.Paid;
        PaymentTransactionId = transactionId;
        FailureReason = null;
        Touch(now);
    }

    public void MarkPaymentFailed(string reason, DateTime now)
    {
        EnsureStatus(OrderStatus.PendingPayment, OrderStatus.PaymentUnknown);
        Status = OrderStatus.PaymentFailed;
        FailureReason = reason;
        Touch(now);
    }

    public void MarkPaymentUnknown(DateTime now)
    {
        EnsureStatus(OrderStatus.PendingPayment);
        Status = OrderStatus.PaymentUnknown;
        Touch(now);
    }

    private void EnsureStatus(params OrderStatus[] allowed)
    {
        if (!allowed.Contains(Status))
            throw new InvalidOperationException($"Order {Id} cannot change from {Status}.");
    }

    private void Touch(DateTime now)
    {
        UpdatedAt = now;
        Version++;
    }
}
