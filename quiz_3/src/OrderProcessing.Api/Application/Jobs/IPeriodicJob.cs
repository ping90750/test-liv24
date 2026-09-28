namespace OrderProcessing.Application.Jobs;

public interface IPeriodicJob
{
    Task RunAsync(CancellationToken ct);
}
