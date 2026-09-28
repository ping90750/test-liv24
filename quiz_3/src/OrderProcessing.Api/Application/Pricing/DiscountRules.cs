using Microsoft.Extensions.Options;
using OrderProcessing.Common;

namespace OrderProcessing.Application.Pricing;

public sealed class PricingOptions
{
    public decimal BulkDiscountThreshold { get; set; } = 50_000m;

    public decimal BulkDiscountAmount { get; set; } = 2_000m;
}

/// <summary>Percentage coupons (DIS10, DIS20, ...) stored in the database instead of hard-coded.</summary>
public sealed class CouponDiscountRule(ICouponProvider coupons, TimeProvider clock) : IDiscountRule
{
    public int Priority => 10;

    public async ValueTask<Result<decimal>> ApplyAsync(decimal total, PricingContext context, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(context.CouponCode))
            return total;

        var coupon = await coupons.FindAsync(context.CouponCode, ct);
        if (coupon is null || !coupon.IsActive || coupon.ExpiresAt <= clock.GetUtcNow().UtcDateTime)
            return OrderErrors.InvalidCoupon;

        return total * (1m - coupon.PercentOff / 100m);
    }
}

/// <summary>Flat discount for large orders, applied after coupons (same order as the original code).</summary>
public sealed class BulkOrderDiscountRule(IOptions<PricingOptions> options) : IDiscountRule
{
    public int Priority => 20;

    public ValueTask<Result<decimal>> ApplyAsync(decimal total, PricingContext context, CancellationToken ct)
    {
        var settings = options.Value;
        Result<decimal> result = total > settings.BulkDiscountThreshold
            ? total - settings.BulkDiscountAmount
            : total;
        return ValueTask.FromResult(result);
    }
}
