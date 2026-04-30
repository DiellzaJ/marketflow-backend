namespace MarketFlow.Infrastructure.BackgroundJobs;

public class StockAlertJob
{
    public Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
