using OrderProcessing.Application.Jobs;

namespace OrderProcessing.Infrastructure;

/// <summary>Runs <typeparamref name="TJob"/> in a fresh DI scope on a fixed interval.</summary>
public sealed class PeriodicJobService<TJob>(
    IServiceScopeFactory scopeFactory,
    TimeSpan interval,
    ILogger<PeriodicJobService<TJob>> logger) : BackgroundService
    where TJob : IPeriodicJob
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<TJob>().RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "{Job} run failed", typeof(TJob).Name);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
