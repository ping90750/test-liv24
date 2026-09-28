using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using OrderProcessing.Application;
using OrderProcessing.Application.Pricing;
using OrderProcessing.Domain;
using OrderProcessing.Tests.Support;

namespace OrderProcessing.Tests;

/// <summary>Pure unit tests: no database, no DI.</summary>
public class PricingServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly PricingService _pricing;

    public PricingServiceTests()
    {
        var clock = new FakeTimeProvider(Now);
        var coupons = new InMemoryCouponProvider(
            new Coupon { Code = "DIS10", PercentOff = 10m },
            new Coupon { Code = "DIS20", PercentOff = 20m },
            new Coupon { Code = "EXPIRED", PercentOff = 50m, ExpiresAt = Now.UtcDateTime.AddDays(-1) },
            new Coupon { Code = "DISABLED", PercentOff = 50m, IsActive = false });

        _pricing = new PricingService(
        [
            new BulkOrderDiscountRule(Options.Create(new PricingOptions())),
            new CouponDiscountRule(coupons, clock),
        ]);
    }

    [Theory]
    [InlineData(null, 200)]
    [InlineData("", 200)]
    [InlineData("DIS10", 180)]
    [InlineData("DIS20", 160)]
    public async Task Applies_percentage_coupons(string? coupon, decimal expected)
    {
        var price = await _pricing.CalculateAsync([Line(100m, 2)], coupon, CancellationToken.None);

        Assert.Equal(expected, price.Value.Total);
        Assert.Equal(200m, price.Value.Subtotal);
        Assert.Equal(200m - expected, price.Value.Discount);
    }

    [Theory]
    [InlineData(50_000, 50_000)]     // not over the threshold
    [InlineData(50_000.01, 48_000.01)]
    [InlineData(60_000, 58_000)]
    public async Task Takes_2000_off_orders_over_50000(decimal subtotal, decimal expected)
    {
        var price = await _pricing.CalculateAsync([Line(subtotal, 1)], null, CancellationToken.None);

        Assert.Equal(expected, price.Value.Total);
    }

    [Fact]
    public async Task Bulk_discount_is_checked_after_the_coupon_regardless_of_registration_order()
    {
        // 55,000 - 10% = 49,500, which is no longer over 50,000.
        var price = await _pricing.CalculateAsync([Line(55_000m, 1)], "DIS10", CancellationToken.None);

        Assert.Equal(49_500m, price.Value.Total);
    }

    [Fact]
    public async Task Rounds_to_two_decimals()
    {
        // 3 x 33.33 = 99.99 - 10% = 89.991
        var price = await _pricing.CalculateAsync([Line(33.33m, 3)], "DIS10", CancellationToken.None);

        Assert.Equal(89.99m, price.Value.Total);
    }

    [Theory]
    [InlineData("UNKNOWN")]
    [InlineData("EXPIRED")]
    [InlineData("DISABLED")]
    [InlineData("dis10")]
    public async Task Rejects_invalid_coupons(string coupon)
    {
        var price = await _pricing.CalculateAsync([Line(100m, 1)], coupon, CancellationToken.None);

        Assert.Equal(OrderErrors.InvalidCoupon, price.Error);
    }

    private static PricedLine Line(decimal unitPrice, int quantity) => new(1, "Item", unitPrice, quantity);
}
