using OrderProcessing.Common;
using OrderProcessing.Domain;

namespace OrderProcessing.Application.Pricing;

public interface IPricingService
{
    Task<Result<PriceBreakdown>> CalculateAsync(IReadOnlyList<PricedLine> lines, string? couponCode, CancellationToken ct);
}

public sealed record PricingContext(IReadOnlyList<PricedLine> Lines, string? CouponCode);

/// <summary>A discount step. Adding a new promotion means adding a rule, not editing the order flow.</summary>
public interface IDiscountRule
{
    /// <summary>Rules run in ascending priority; each receives the total left by the previous one.</summary>
    int Priority { get; }

    ValueTask<Result<decimal>> ApplyAsync(decimal total, PricingContext context, CancellationToken ct);
}

public sealed class PricingService(IEnumerable<IDiscountRule> rules) : IPricingService
{
    private readonly IDiscountRule[] _rules = rules.OrderBy(r => r.Priority).ToArray();

    public async Task<Result<PriceBreakdown>> CalculateAsync(
        IReadOnlyList<PricedLine> lines, string? couponCode, CancellationToken ct)
    {
        var subtotal = lines.Sum(l => l.LineTotal);
        var context = new PricingContext(lines, couponCode);

        var total = subtotal;
        foreach (var rule in _rules)
        {
            var applied = await rule.ApplyAsync(total, context, ct);
            if (applied.IsFailure)
                return applied.Error;

            total = applied.Value;
        }

        total = Math.Max(0m, Math.Round(total, 2, MidpointRounding.AwayFromZero));
        return new PriceBreakdown(subtotal, subtotal - total, total);
    }
}
