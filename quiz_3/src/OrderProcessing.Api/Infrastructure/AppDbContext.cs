using Microsoft.EntityFrameworkCore;
using OrderProcessing.Domain;

namespace OrderProcessing.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<Product> Products => Set<Product>();

    public DbSet<Coupon> Coupons => Set<Coupon>();

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(user =>
        {
            user.Property(u => u.Email).HasMaxLength(320);
            user.Property(u => u.PaymentMethodToken).HasMaxLength(200);
        });

        modelBuilder.Entity<Product>(product =>
        {
            product.Property(p => p.Name).HasMaxLength(200);
            product.Property(p => p.Price).HasPrecision(18, 2);
            // Last line of defence: the database refuses negative stock.
            product.ToTable(t => t.HasCheckConstraint("CK_Products_Stock_NonNegative", "Stock >= 0"));
        });

        modelBuilder.Entity<Coupon>(coupon =>
        {
            coupon.HasKey(c => c.Code);
            coupon.Property(c => c.Code).HasMaxLength(50);
            coupon.Property(c => c.PercentOff).HasPrecision(5, 2);
        });

        modelBuilder.Entity<Order>(order =>
        {
            order.HasIndex(o => new { o.UserId, o.IdempotencyKey }).IsUnique();
            order.HasIndex(o => new { o.Status, o.UpdatedAt });
            order.Property(o => o.IdempotencyKey).HasMaxLength(100);
            order.Property(o => o.CouponCode).HasMaxLength(50);
            order.Property(o => o.Status).HasConversion<string>().HasMaxLength(32);
            order.Property(o => o.Subtotal).HasPrecision(18, 2);
            order.Property(o => o.Discount).HasPrecision(18, 2);
            order.Property(o => o.TotalAmount).HasPrecision(18, 2);
            order.Property(o => o.Version).IsConcurrencyToken();
            order.HasMany(o => o.Items).WithOne().HasForeignKey(i => i.OrderId);
            order.Navigation(o => o.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<OrderItem>(item =>
        {
            item.HasKey(i => new { i.OrderId, i.ProductId });
            item.Property(i => i.ProductName).HasMaxLength(200);
            item.Property(i => i.UnitPrice).HasPrecision(18, 2);
        });

        modelBuilder.Entity<OutboxMessage>(message =>
        {
            message.HasIndex(m => m.ProcessedAt);
            message.Property(m => m.Type).HasMaxLength(100);
        });
    }
}
