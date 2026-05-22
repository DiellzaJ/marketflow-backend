using MarketFlow.Infrastructure.BackgroundJobs;
using Microsoft.Extensions.Options;

namespace MarketFlow.Api.Services;

public sealed class StockAlertHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<StockAlertJobOptions> _options;
    private readonly ILogger<StockAlertHostedService> _logger;

    public StockAlertHostedService(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<StockAlertJobOptions> options,
        ILogger<StockAlertHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.CurrentValue.Enabled)
        {
            _logger.LogInformation("Low-stock alert background job is disabled.");
            return;
        }

        if (_options.CurrentValue.RunOnStartup)
        {
            await RunOnceAsync(stoppingToken);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var interval = _options.CurrentValue.Interval;

            if (interval <= TimeSpan.Zero)
            {
                interval = TimeSpan.FromDays(1);
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            await RunOnceAsync(stoppingToken);
        }
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var job = scope.ServiceProvider.GetRequiredService<StockAlertJob>();

            await job.ExecuteAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Low-stock alert background job failed.");
        }
    }
}
