using OrderProcessing.Domain;
using OrderProcessing.Infrastructure.Persistence;

namespace OrderProcessing.Infrastructure;

public static class DemoData
{
    /// <summary>Creates the schema and sample data. Production would use EF migrations instead.</summary>
    public static async Task SeedAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await db.Database.EnsureCreatedAsync();
        if (db.Users.Any())
            return;

        db.Users.AddRange(
            new User { Id = 1, Email = "somchai@example.com", IsActive = true, PaymentMethodToken = "tok_visa" },
            new User { Id = 2, Email = "inactive@example.com", IsActive = false, PaymentMethodToken = "tok_visa" },
            new User { Id = 3, Email = "declined@example.com", IsActive = true, PaymentMethodToken = FakePaymentGateway.DeclinedToken });

        db.Products.AddRange(
            new Product { Id = 1, Name = "Laptop", Price = 45_000m, Stock = 10 },
            new Product { Id = 2, Name = "Mouse", Price = 500m, Stock = 100 },
            new Product { Id = 3, Name = "Limited Edition Watch", Price = 60_000m, Stock = 1 });

        db.Coupons.AddRange(
            new Coupon { Code = "DIS10", PercentOff = 10m },
            new Coupon { Code = "DIS20", PercentOff = 20m });

        await db.SaveChangesAsync();
    }
}
