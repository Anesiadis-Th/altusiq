using AltusIQ.Api.Services;

namespace AltusIQ.Api.Background;

// Keeps /api/analytics a memory read for everyone who asks for it.
public class AnalyticsCacheWarmer(
    IServiceScopeFactory scopeFactory,
    ILogger<AnalyticsCacheWarmer> logger) : BackgroundService
{
    private static readonly TimeSpan RefreshInterval =
        AnalyticsService.CacheTtl - TimeSpan.FromMinutes(1);

    // Let migrations and the first poll finish before touching the connection.
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Analytics cache warmer started, refreshing every {Minutes:F0}m",
            RefreshInterval.TotalMinutes);

        try
        {
            await Task.Delay(StartupDelay, stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                await RefreshAsync(stoppingToken);
                await Task.Delay(RefreshInterval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }

        logger.LogInformation("Analytics cache warmer stopped");
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var analytics = scope.ServiceProvider.GetRequiredService<AnalyticsService>();

            var started = DateTime.UtcNow;
            var result = await analytics.RefreshAsync(ct);

            logger.LogInformation(
                "Analytics cache refreshed in {Elapsed:F0}ms ({Flights} flights, {Days}d window)",
                (DateTime.UtcNow - started).TotalMilliseconds,
                result.TotalFlights,
                result.RangeDays);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The old entry stays until it expires, then a reader recomputes it.
            logger.LogError(ex, "Analytics cache refresh failed");
        }
    }
}
