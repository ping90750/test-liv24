using Microsoft.EntityFrameworkCore;
using OrderProcessing.Application;
using OrderProcessing.Domain;
using OrderProcessing.Infrastructure.Persistence;

namespace OrderProcessing.Infrastructure;

public sealed class EfInventoryService(AppDbContext db) : IInventoryService
{
    public async Task<bool> TryReserveAsync(IReadOnlyList<OrderLine> lines, CancellationToken ct)
    {
        EnsureTransaction();

        foreach (var line in lines.OrderBy(l => l.ProductId))
        {
            // UPDATE Products SET Stock = Stock - @qty WHERE Id = @id AND Stock >= @qty
            // Check and decrement happen in one statement, so two requests can't both take the last unit.
            var affected = await db.Products
                .Where(p => p.Id == line.ProductId && p.Stock >= line.Quantity)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Stock, p => p.Stock - line.Quantity), ct);

            if (affected == 0)
                return false;
        }

        return true;
    }

    public async Task ReleaseAsync(IReadOnlyList<OrderLine> lines, CancellationToken ct)
    {
        EnsureTransaction();

        foreach (var line in lines.OrderBy(l => l.ProductId))
        {
            await db.Products
                .Where(p => p.Id == line.ProductId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Stock, p => p.Stock + line.Quantity), ct);
        }
    }

    private void EnsureTransaction()
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Stock changes must run inside a database transaction.");
    }
}
