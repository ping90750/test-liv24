using OrderProcessing.Application;
using OrderProcessing.Domain;
using OrderProcessing.Tests.Support;
using static OrderProcessing.Tests.Support.TestHost;

namespace OrderProcessing.Tests;

/// <summary>Parallel requests against a database file, each with its own connection.</summary>
public class ConcurrencyTests
{
    [Fact]
    public async Task Parallel_orders_never_oversell_stock()
    {
        await using var host = await TestHost.CreateAsync(onDisk: true);

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(i =>
            Task.Run(() => host.PlaceOrderAsync($"key-{i}", new OrderLine(MouseId, 1)))));

        Assert.Equal(5, results.Count(r => r.IsSuccess && r.Value.Status == OrderStatus.Paid));
        Assert.Equal(15, results.Count(r => r.IsFailure && r.Error == OrderErrors.InsufficientStock));
        Assert.Equal(0, await host.GetStockAsync(MouseId));
        Assert.Equal(5, host.Payment.ChargeCount);
    }

    [Fact]
    public async Task Parallel_retries_with_the_same_key_create_one_order_and_one_charge()
    {
        await using var host = await TestHost.CreateAsync(onDisk: true);

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ =>
            Task.Run(() => host.PlaceOrderAsync("same-key", new OrderLine(MouseId, 2)))));

        Assert.All(results, r => Assert.True(r.IsSuccess));
        Assert.Single(results.Select(r => r.Value.OrderId).Distinct());
        Assert.Equal(1, await host.CountOrdersAsync());
        Assert.Equal(1, host.Payment.ChargeCount);
        Assert.Equal(3, await host.GetStockAsync(MouseId));
    }
}
