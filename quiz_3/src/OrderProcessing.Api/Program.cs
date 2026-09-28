using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OrderProcessing;
using OrderProcessing.Api;
using OrderProcessing.Application;
using OrderProcessing.Application.Jobs;
using OrderProcessing.Infrastructure;
using OrderProcessing.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// SQLite keeps the demo self-contained; swap for UseSqlServer / UseNpgsql in production.
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Orders")));

builder.Services.AddOrderProcessing(builder.Configuration);
builder.Services.AddSingleton<IPaymentGateway, FakePaymentGateway>();
builder.Services.AddSingleton<IEmailSender, LoggingEmailSender>();

builder.Services.AddHostedService(sp => new PeriodicJobService<OutboxProcessor>(
    sp.GetRequiredService<IServiceScopeFactory>(),
    sp.GetRequiredService<IOptions<OutboxOptions>>().Value.Interval,
    sp.GetRequiredService<ILogger<PeriodicJobService<OutboxProcessor>>>()));
builder.Services.AddHostedService(sp => new PeriodicJobService<PaymentReconciler>(
    sp.GetRequiredService<IServiceScopeFactory>(),
    sp.GetRequiredService<IOptions<ReconciliationOptions>>().Value.Interval,
    sp.GetRequiredService<ILogger<PeriodicJobService<PaymentReconciler>>>()));

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

await DemoData.SeedAsync(app.Services);

app.MapOrderEndpoints();

app.Run();
