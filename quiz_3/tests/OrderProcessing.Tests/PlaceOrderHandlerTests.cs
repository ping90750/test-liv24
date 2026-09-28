using OrderProcessing.Application;
using OrderProcessing.Domain;
using OrderProcessing.Tests.Support;
using static OrderProcessing.Tests.Support.TestHost;

namespace OrderProcessing.Tests;

public class PlaceOrderHandlerTests
{
    [Fact]
    public async Task Successful_order_charges_once_deducts_stock_and_queues_email()
    {
        await using var host = await TestHost.CreateAsync();

        var result = await host.PlaceOrderAsync("key-1", new OrderLine(MouseId, 2));

        Assert.True(result.IsSuccess);
        Assert.Equal(OrderStatus.Paid, result.Value.Status);
        Assert.Equal(1_000m, result.Value.TotalAmount);
        Assert.Equal(1, host.Payment.ChargeCount);
        Assert.Equal(3, await host.GetStockAsync(MouseId));

        var order = await host.GetOrderAsync(result.Value.OrderId);
        var item = Assert.Single(order.Items);
        Assert.Equal((MouseId, 2, 500m), (item.ProductId, item.Quantity, item.UnitPrice));

        var message = Assert.Single(await host.GetOutboxAsync());
        Assert.Equal(nameof(OrderPaid), message.Type);
        Assert.Empty(host.Email.Sent); // sent later by OutboxProcessor, not inside the request
    }

    [Fact]
    public async Task Retrying_with_the_same_idempotency_key_returns_the_same_order_without_charging_again()
    {
        await using var host = await TestHost.CreateAsync();

        var first = await host.PlaceOrderAsync("key-1", new OrderLine(MouseId, 2));
        var retry = await host.PlaceOrderAsync("key-1", new OrderLine(MouseId, 2));

        Assert.Equal(first.Value.OrderId, retry.Value.OrderId);
        Assert.Equal(1, host.Payment.ChargeCount);
        Assert.Equal(1, await host.CountOrdersAsync());
        Assert.Equal(3, await host.GetStockAsync(MouseId));
    }

    [Fact]
    public async Task Repeated_product_lines_are_combined_before_checking_stock()
    {
        await using var host = await TestHost.CreateAsync();

        // 3 + 3 = 6 > 5 in stock. The old code checked each line alone and let this through.
        var result = await host.PlaceOrderAsync("key-1", new OrderLine(MouseId, 3), new OrderLine(MouseId, 3));

        Assert.Equal(OrderErrors.InsufficientStock, result.Error);
        Assert.Equal(5, await host.GetStockAsync(MouseId));
        Assert.Equal(0, host.Payment.ChargeCount);
    }

    [Fact]
    public async Task Insufficient_stock_on_one_line_rolls_back_the_lines_already_reserved()
    {
        await using var host = await TestHost.CreateAsync();

        var result = await host.PlaceOrderAsync("key-1", new OrderLine(LaptopId, 1), new OrderLine(MouseId, 6));

        Assert.Equal(OrderErrors.InsufficientStock, result.Error);
        Assert.Equal(10, await host.GetStockAsync(LaptopId));
        Assert.Equal(0, await host.CountOrdersAsync());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task Non_positive_quantity_is_rejected(int quantity)
    {
        await using var host = await TestHost.CreateAsync();

        var result = await host.PlaceOrderAsync("key-1", new OrderLine(MouseId, quantity));

        Assert.Equal(OrderErrors.InvalidQuantity, result.Error);
        Assert.Equal(5, await host.GetStockAsync(MouseId));
    }

    [Fact]
    public async Task Inactive_user_is_rejected()
    {
        await using var host = await TestHost.CreateAsync();

        var result = await host.PlaceOrderAsync(
            new PlaceOrderCommand(InactiveUserId, [new OrderLine(MouseId, 1)], null, "key-1"));

        Assert.Equal(OrderErrors.InvalidUser, result.Error);
    }

    [Fact]
    public async Task Empty_order_is_rejected()
    {
        await using var host = await TestHost.CreateAsync();

        var result = await host.PlaceOrderAsync("key-1");

        Assert.Equal(OrderErrors.EmptyOrder, result.Error);
    }

    [Fact]
    public async Task Missing_idempotency_key_is_rejected()
    {
        await using var host = await TestHost.CreateAsync();

        var result = await host.PlaceOrderAsync(" ", new OrderLine(MouseId, 1));

        Assert.Equal(OrderErrors.MissingIdempotencyKey, result.Error);
    }

    [Fact]
    public async Task Unknown_product_is_rejected()
    {
        await using var host = await TestHost.CreateAsync();

        var result = await host.PlaceOrderAsync("key-1", new OrderLine(999, 1));

        Assert.Equal(OrderErrors.ProductNotFound, result.Error);
    }

    [Fact]
    public async Task Invalid_coupon_is_rejected_instead_of_silently_ignored()
    {
        await using var host = await TestHost.CreateAsync();

        var result = await host.PlaceOrderAsync(
            new PlaceOrderCommand(ActiveUserId, [new OrderLine(MouseId, 1)], "NOPE", "key-1"));

        Assert.Equal(OrderErrors.InvalidCoupon, result.Error);
        Assert.Equal(0, await host.CountOrdersAsync());
    }

    [Fact]
    public async Task Coupon_and_bulk_discount_are_applied_to_the_charged_amount()
    {
        await using var host = await TestHost.CreateAsync();
        decimal charged = 0;
        host.Payment.OnCharge = request =>
        {
            charged = request.Amount;
            return PaymentResult.Success("txn");
        };

        // 3 x 30,000 = 90,000 -> DIS20 = 72,000 -> over 50,000 so -2,000 = 70,000
        var result = await host.PlaceOrderAsync(
            new PlaceOrderCommand(ActiveUserId, [new OrderLine(LaptopId, 3)], "DIS20", "key-1"));

        Assert.Equal(70_000m, result.Value.TotalAmount);
        Assert.Equal(70_000m, charged);
    }

    [Fact]
    public async Task Declined_payment_marks_the_order_failed_and_releases_stock()
    {
        await using var host = await TestHost.CreateAsync();
        host.Payment.OnCharge = _ => PaymentResult.Decline("Insufficient funds");

        var result = await host.PlaceOrderAsync("key-1", new OrderLine(MouseId, 2));

        Assert.Equal(OrderStatus.PaymentFailed, result.Value.Status);
        Assert.Equal("Insufficient funds", result.Value.FailureReason);
        Assert.Equal(5, await host.GetStockAsync(MouseId));
        Assert.Empty(await host.GetOutboxAsync());
    }

    [Fact]
    public async Task Gateway_error_leaves_the_order_for_reconciliation_with_stock_still_reserved()
    {
        await using var host = await TestHost.CreateAsync();
        host.Payment.OnCharge = _ => throw new TimeoutException("Gateway timed out");

        var result = await host.PlaceOrderAsync("key-1", new OrderLine(MouseId, 2));

        Assert.Equal(OrderStatus.PaymentUnknown, result.Value.Status);
        Assert.Equal(3, await host.GetStockAsync(MouseId));
    }

    [Fact]
    public async Task Gateway_receives_the_order_id_as_its_idempotency_key()
    {
        await using var host = await TestHost.CreateAsync();
        ChargeRequest? sent = null;
        host.Payment.OnCharge = request =>
        {
            sent = request;
            return PaymentResult.Success("txn");
        };

        var result = await host.PlaceOrderAsync("key-1", new OrderLine(MouseId, 1));

        Assert.Equal(result.Value.OrderId.ToString(), sent!.IdempotencyKey);
        Assert.Equal("tok_ok", sent.PaymentMethodToken);
    }
}
