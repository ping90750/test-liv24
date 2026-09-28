using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using OrderProcessing.Application;
using OrderProcessing.Application.Jobs;
using OrderProcessing.Common;
using OrderProcessing.Domain;
using OrderProcessing.Infrastructure.Persistence;

namespace OrderProcessing.Tests.Support;

/// <summary>
/// Real DI wiring and a real SQLite database, with fakes only at the edges
/// (payment gateway, email, clock).
/// </summary>
internal sealed class TestHost : IAsyncDisposable
{
    public const int ActiveUserId = 1;
    public const int InactiveUserId = 2;
    public const int LaptopId = 1;      // 30,000 x 10 in stock
    public const int MouseId = 2;       //    500 x 5 in stock

    private readonly ServiceProvider _services;
    private readonly SqliteConnection? _sharedConnection;
    private readonly string? _databaseFile;

    private TestHost(ServiceProvider services, SqliteConnection? sharedConnection, string? databaseFile)
    {
        _services = services;
        _sharedConnection = sharedConnection;
        _databaseFile = databaseFile;
    }

    public StubPaymentGateway Payment { get; } = new();

    public RecordingEmailSender Email { get; } = new();

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    /// <param name="onDisk">
    /// Use a database file with a connection per scope, so parallel requests really run concurrently.
    /// The default is one shared in-memory connection.
    /// </param>
    public static async Task<TestHost> CreateAsync(bool onDisk = false)
    {
        SqliteConnection? sharedConnection = null;
        string? databaseFile = null;
        var services = new ServiceCollection();

        if (onDisk)
        {
            databaseFile = Path.Combine(Path.GetTempPath(), $"orders-{Guid.NewGuid():N}.db");
            var connectionString = $"Data Source={databaseFile};Default Timeout=60";
            services.AddDbContext<AppDbContext>(o => o.UseSqlite(connectionString));
        }
        else
        {
            sharedConnection = new SqliteConnection("Data Source=:memory:");
            sharedConnection.Open();
            services.AddDbContext<AppDbContext>(o => o.UseSqlite(sharedConnection));
        }

        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        services.AddOrderProcessing(new ConfigurationBuilder().Build());

        TestHost? host = null;
        services.AddSingleton<IPaymentGateway>(_ => host!.Payment);
        services.AddSingleton<IEmailSender>(_ => host!.Email);
        services.AddSingleton<TimeProvider>(_ => host!.Clock);

        host = new TestHost(services.BuildServiceProvider(validateScopes: true), sharedConnection, databaseFile);
        await host.SeedAsync(onDisk);
        return host;
    }

    public Task<Result<OrderResult>> PlaceOrderAsync(string idempotencyKey, params OrderLine[] items) =>
        PlaceOrderAsync(new PlaceOrderCommand(ActiveUserId, items, null, idempotencyKey));

    public Task<Result<OrderResult>> PlaceOrderAsync(PlaceOrderCommand command) =>
        RunAsync(sp => sp.GetRequiredService<PlaceOrderHandler>().HandleAsync(command, CancellationToken.None));

    public Task RunJobAsync<TJob>() where TJob : IPeriodicJob =>
        RunAsync(sp => sp.GetRequiredService<TJob>().RunAsync(CancellationToken.None));

    public Task<int> GetStockAsync(int productId) =>
        QueryAsync(db => db.Products.Where(p => p.Id == productId).Select(p => p.Stock).SingleAsync());

    public Task<Order> GetOrderAsync(Guid orderId) =>
        QueryAsync(db => db.Orders.Include(o => o.Items).SingleAsync(o => o.Id == orderId));

    public Task<List<OutboxMessage>> GetOutboxAsync() =>
        QueryAsync(db => db.OutboxMessages.ToListAsync());

    public Task<int> CountOrdersAsync() => QueryAsync(db => db.Orders.CountAsync());

    public Task<T> QueryAsync<T>(Func<AppDbContext, Task<T>> query) =>
        RunAsync(sp => query(sp.GetRequiredService<AppDbContext>()));

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        if (_sharedConnection is not null)
            await _sharedConnection.DisposeAsync();

        if (_databaseFile is not null)
        {
            SqliteConnection.ClearAllPools();
            foreach (var file in new[] { _databaseFile, _databaseFile + "-wal", _databaseFile + "-shm" })
                File.Delete(file);
        }
    }

    private async Task<T> RunAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = _services.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    private Task RunAsync(Func<IServiceProvider, Task> action) =>
        RunAsync(async sp =>
        {
            await action(sp);
            return true;
        });

    private Task SeedAsync(bool onDisk) => RunAsync(async sp =>
    {
        var db = sp.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();
        if (onDisk)
            await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");

        db.Users.AddRange(
            new User { Id = ActiveUserId, Email = "active@example.com", IsActive = true, PaymentMethodToken = "tok_ok" },
            new User { Id = InactiveUserId, Email = "inactive@example.com", IsActive = false, PaymentMethodToken = "tok_ok" });
        db.Products.AddRange(
            new Product { Id = LaptopId, Name = "Laptop", Price = 30_000m, Stock = 10 },
            new Product { Id = MouseId, Name = "Mouse", Price = 500m, Stock = 5 });
        db.Coupons.AddRange(
            new Coupon { Code = "DIS10", PercentOff = 10m },
            new Coupon { Code = "DIS20", PercentOff = 20m });
        await db.SaveChangesAsync();
    });
}
