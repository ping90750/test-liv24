using OrderProcessing.Application;
using OrderProcessing.Application.Jobs;
using OrderProcessing.Domain;
using OrderProcessing.Tests.Support;
using static OrderProcessing.Tests.Support.TestHost;

namespace OrderProcessing.Tests;

public class BackgroundJobTests
{
    [Fact]
    public async Task Outbox_sends_the_paid_email_once()
    {
        await using var host = await TestHost.CreateAsync();
        await host.PlaceOrderAsync("key-1", new OrderLine(MouseId, 1));

        await host.RunJobAsync<OutboxProcessor>();
        await host.RunJobAsync<OutboxProcessor>();

        var email = Assert.Single(host.Email.Sent);
        Assert.Equal("active@example.com", email.To);
        Assert.NotNull(Assert.Single(await host.GetOutboxAsync()).ProcessedAt);
    }

    [Fact]
    public async Task Email_failure_is_retried_later_and_never_touches_the_order()
    {
        await using var host = await TestHost.CreateAsync();
        var placed = await host.PlaceOrderAsync("key-1", new OrderLine(MouseId, 1));
        host.Email.FailWith = _ => new InvalidOperationException("SMTP down");

        await host.RunJobAsync<OutboxProcessor>();

        var failed = Assert.Single(await host.GetOutboxAsync());
        Assert.Null(failed.ProcessedAt);
        Assert.Equal(1, failed.Attempts);
        Assert.Equal("SMTP down", failed.LastError);
        Assert.Equal(OrderStatus.Paid, (await host.GetOrderAsync(placed.Value.OrderId)).Status);

        host.Email.FailWith = _ => null;
        await host.RunJobAsync<OutboxProcessor>();

        Assert.Single(host.Email.Sent);
    }

    [Fact]
    public async Task Reconciler_marks_an_unknown_payment_paid_when_the_gateway_took_the_money()
    {
        await using var host = await TestHost.CreateAsync();
        host.Payment.OnCharge = _ => throw new TimeoutException();
        var placed = await host.PlaceOrderAsync("key-1", new OrderLine(MouseId, 2));
        host.Payment.OnGetStatus = _ => PaymentResult.Success("txn_late");

        host.Clock.Advance(TimeSpan.FromMinutes(10));
        await host.RunJobAsync<PaymentReconciler>();

        var order = await host.GetOrderAsync(placed.Value.OrderId);
        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Equal("txn_late", order.PaymentTransactionId);
        Assert.Equal(3, await host.GetStockAsync(MouseId));
        Assert.Single(await host.GetOutboxAsync());
    }

    [Fact]
    public async Task Reconciler_fails_the_order_and_releases_stock_when_no_charge_exists()
    {
        await using var host = await TestHost.CreateAsync();
        host.Payment.OnCharge = _ => throw new TimeoutException();
        var placed = await host.PlaceOrderAsync("key-1", new OrderLine(MouseId, 2));
        host.Payment.OnGetStatus = _ => PaymentResult.NotFound();

        host.Clock.Advance(TimeSpan.FromMinutes(10));
        await host.RunJobAsync<PaymentReconciler>();

        Assert.Equal(OrderStatus.PaymentFailed, (await host.GetOrderAsync(placed.Value.OrderId)).Status);
        Assert.Equal(5, await host.GetStockAsync(MouseId));
    }

    [Fact]
    public async Task Reconciler_leaves_recent_orders_alone()
    {
        await using var host = await TestHost.CreateAsync();
        host.Payment.OnCharge = _ => throw new TimeoutException();
        var placed = await host.PlaceOrderAsync("key-1", new OrderLine(MouseId, 2));
        host.Payment.OnGetStatus = _ => PaymentResult.NotFound();

        host.Clock.Advance(TimeSpan.FromMinutes(1));
        await host.RunJobAsync<PaymentReconciler>();

        Assert.Equal(OrderStatus.PaymentUnknown, (await host.GetOrderAsync(placed.Value.OrderId)).Status);
    }
}
