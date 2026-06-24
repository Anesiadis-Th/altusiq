using AltusIQ.Api.Data;
using AltusIQ.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AltusIQ.Api.Background;

public class RetentionService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RetentionSettings _settings;
    private readonly ILogger<RetentionService> _logger;

    public RetentionService(
        IServiceScopeFactory scopeFactory,
        IOptions<RetentionSettings> settings,
        ILogger<RetentionService> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled)
        {
            _logger.LogInformation(
                "Retention service disabled via config, no purges will run");
            return;
        }

        _logger.LogInformation(
            "Retention service started. Daily run hour (UTC): {Hour}, " +
            "window: {Days}d, batch size: {BatchSize}",
            _settings.RunAtHourUtc, _settings.RetentionDays, _settings.BatchSize);

        await SafeRunAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = TimeUntilNextRun(DateTime.UtcNow);
            _logger.LogInformation(
                "Next retention purge in {Hours:F1}h", delay.TotalHours);

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            await SafeRunAsync(stoppingToken);
        }

        _logger.LogInformation("Retention service stopped");
    }

    private async Task SafeRunAsync(CancellationToken ct)
    {
        try
        {
            await PurgeAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during retention purge");
        }
    }

    private TimeSpan TimeUntilNextRun(DateTime nowUtc)
    {
        var next = new DateTime(
            nowUtc.Year, nowUtc.Month, nowUtc.Day,
            _settings.RunAtHourUtc, 0, 0, DateTimeKind.Utc);

        if (next <= nowUtc)
            next = next.AddDays(1);

        return next - nowUtc;
    }

    private async Task PurgeAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AltusIqDbContext>();

        var cutoff = DateTime.UtcNow.AddDays(-_settings.RetentionDays);
        var totalDeleted = 0;
        var batches = 0;

        while (!ct.IsCancellationRequested)
        {
            var ids = await db.Flights
                .Where(f => f.ClosedAt != null && f.ClosedAt < cutoff)
                .OrderBy(f => f.ClosedAt)
                .Select(f => f.Id)
                .Take(_settings.BatchSize)
                .ToListAsync(ct);

            if (ids.Count == 0)
                break;

            var deleted = await db.Flights
                .Where(f => ids.Contains(f.Id))
                .ExecuteDeleteAsync(ct);

            totalDeleted += deleted;
            batches++;

            _logger.LogInformation(
                "Retention purge batch {Batch}: deleted {Deleted} flights " +
                "(running total {Total})",
                batches, deleted, totalDeleted);

            if (ids.Count < _settings.BatchSize)
                break;

            if (_settings.DelayBetweenBatchesMs > 0)
                await Task.Delay(_settings.DelayBetweenBatchesMs, ct);
        }

        _logger.LogInformation(
            "Retention purge complete: {Total} flight(s) older than {Days}d " +
            "removed in {Batches} batch(es)",
            totalDeleted, _settings.RetentionDays, batches);
    }
}
