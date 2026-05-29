using MarketFlow.Infrastructure.BackgroundJobs;
using Microsoft.Extensions.Options;

namespace MarketFlow.Api.Services;

public sealed class AiAnalysisHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<AiAnalysisBackgroundJobOptions> _options;
    private readonly ILogger<AiAnalysisHostedService> _logger;

    public AiAnalysisHostedService(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<AiAnalysisBackgroundJobOptions> options,
        ILogger<AiAnalysisHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.CurrentValue.Enabled)
        {
            _logger.LogInformation("AI analysis background jobs are disabled.");
            return;
        }

        if (_options.CurrentValue.RunOnStartup)
        {
            await RunAllAsync(stoppingToken);
        }

        var dailyTask = RunLoopAsync(
            "daily dashboard AI summary",
            options => options.DailyInterval,
            job => job.ExecuteDailyDashboardSummaryAsync(stoppingToken),
            stoppingToken);
        var lowStockTask = RunLoopAsync(
            "low-stock AI recommendation check",
            options => options.LowStockInterval,
            job => job.ExecuteLowStockRecommendationCheckAsync(stoppingToken),
            stoppingToken);
        var weeklyTask = RunLoopAsync(
            "weekly supplier AI summary",
            options => options.WeeklyInterval,
            job => job.ExecuteWeeklySupplierPerformanceSummaryAsync(stoppingToken),
            stoppingToken);

        await Task.WhenAll(dailyTask, lowStockTask, weeklyTask);
    }

    private async Task RunAllAsync(CancellationToken cancellationToken)
    {
        await RunOnceAsync(
            "daily dashboard AI summary",
            job => job.ExecuteDailyDashboardSummaryAsync(cancellationToken),
            cancellationToken);
        await RunOnceAsync(
            "low-stock AI recommendation check",
            job => job.ExecuteLowStockRecommendationCheckAsync(cancellationToken),
            cancellationToken);
        await RunOnceAsync(
            "weekly supplier AI summary",
            job => job.ExecuteWeeklySupplierPerformanceSummaryAsync(cancellationToken),
            cancellationToken);
    }

    private async Task RunLoopAsync(
        string jobName,
        Func<AiAnalysisBackgroundJobOptions, TimeSpan> getInterval,
        Func<AiAnalysisBackgroundJob, Task> executeAsync,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var interval = getInterval(_options.CurrentValue);

            if (interval <= TimeSpan.Zero)
            {
                interval = TimeSpan.FromDays(1);
            }

            try
            {
                await Task.Delay(interval, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            await RunOnceAsync(jobName, executeAsync, cancellationToken);
        }
    }

    private async Task RunOnceAsync(
        string jobName,
        Func<AiAnalysisBackgroundJob, Task> executeAsync,
        CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var job = scope.ServiceProvider.GetRequiredService<AiAnalysisBackgroundJob>();

            await executeAsync(job);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "{JobName} failed.", jobName);
        }
    }
}
