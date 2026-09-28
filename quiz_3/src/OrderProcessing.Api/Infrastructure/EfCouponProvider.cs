using Microsoft.EntityFrameworkCore;
using OrderProcessing.Application;
using OrderProcessing.Domain;
using OrderProcessing.Infrastructure.Persistence;

namespace OrderProcessing.Infrastructure;

public sealed class EfCouponProvider(AppDbContext db) : ICouponProvider
{
    public Task<Coupon?> FindAsync(string code, CancellationToken ct) =>
        db.Coupons.AsNoTracking().FirstOrDefaultAsync(c => c.Code == code, ct);
}
