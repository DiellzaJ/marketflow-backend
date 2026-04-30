namespace MarketFlow.Infrastructure.BackgroundJobs;

public class EmailJob
{
    public Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
