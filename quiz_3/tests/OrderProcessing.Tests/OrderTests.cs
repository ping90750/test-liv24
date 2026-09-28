using OrderProcessing.Domain;

namespace OrderProcessing.Tests;

public class OrderTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void New_order_waits_for_payment_and_keeps_prices_per_item()
    {
        var order = NewOrder();

        Assert.Equal(OrderStatus.PendingPayment, order.Status);
        var item = Assert.Single(order.Items);
        Assert.Equal((order.Id, 1, 100m, 2), (item.OrderId, item.ProductId, item.UnitPrice, item.Quantity));
    }

    [Fact]
    public void Unknown_payment_can_later_be_settled_as_paid()
    {
        var order = NewOrder();
        order.MarkPaymentUnknown(Now);

        order.MarkPaid("txn", Now);

        Assert.Equal(OrderStatus.Paid, order.Status);
    }

    [Fact]
    public void Paid_order_cannot_be_marked_failed()
    {
        var order = NewOrder();
        order.MarkPaid("txn", Now);

        Assert.Throws<InvalidOperationException>(() => order.MarkPaymentFailed("late decline", Now));
    }

    [Fact]
    public void Failed_order_cannot_be_marked_paid()
    {
        var order = NewOrder();
        order.MarkPaymentFailed("declined", Now);

        Assert.Throws<InvalidOperationException>(() => order.MarkPaid("txn", Now));
    }

    [Fact]
    public void Every_transition_bumps_the_concurrency_version()
    {
        var order = NewOrder();

        order.MarkPaymentUnknown(Now);
        order.MarkPaid("txn", Now);

        Assert.Equal(2, order.Version);
    }

    private static Order NewOrder() => Order.Create(
        userId: 1,
        idempotencyKey: "key",
        lines: [new PricedLine(1, "Item", 100m, 2)],
        price: new PriceBreakdown(200m, 0m, 200m),
        couponCode: null,
        now: Now);
}
