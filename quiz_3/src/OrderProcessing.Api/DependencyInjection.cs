using OrderProcessing.Application;
using OrderProcessing.Application.Jobs;
using OrderProcessing.Application.Pricing;
using OrderProcessing.Infrastructure;

namespace OrderProcessing;

public static class DependencyInjection
{
    /// <summary>
    /// Registers order processing services. The host supplies the <c>AppDbContext</c>,
    /// <see cref="IPaymentGateway"/> and <see cref="IEmailSender"/> implementations.
    /// </summary>
    public static IServiceCollection AddOrderProcessing(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PricingOptions>().Bind(configuration.GetSection("Pricing"));
        services.AddOptions<OutboxOptions>().Bind(configuration.GetSection("Outbox"));
        services.AddOptions<ReconciliationOptions>().Bind(configuration.GetSection("Reconciliation"));

        services.AddSingleton(TimeProvider.System);

        services.AddScoped<IDiscountRule, CouponDiscountRule>();
        services.AddScoped<IDiscountRule, BulkOrderDiscountRule>();
        services.AddScoped<IPricingService, PricingService>();
        services.AddScoped<ICouponProvider, EfCouponProvider>();
        services.AddScoped<IInventoryService, EfInventoryService>();

        services.AddScoped<OrderPaymentFinalizer>();
        services.AddScoped<PlaceOrderHandler>();
        services.AddScoped<OutboxProcessor>();
        services.AddScoped<PaymentReconciler>();

        return services;
    }
}
