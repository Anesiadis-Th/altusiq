using AltusIQ.Api.Data;
using AltusIQ.Api.Models;
using AltusIQ.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AltusIQ.Api.Background;

public class FlightEnrichmentService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly EnrichmentSettings _settings;
    private readonly ILogger<FlightEnrichmentService> _logger;

    public FlightEnrichmentService(
        IServiceScopeFactory scopeFactory,
        IOptions<EnrichmentSettings> settings,
        ILogger<FlightEnrichmentService> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Flight enrichment service started. Daily run hour (UTC): {Hour}, " +
            "min age: {MinAge}m, max attempts: {MaxAttempts}, lookback: {Lookback}d",
            _settings.RunAtHourUtc, _settings.MinAgeMinutes,
            _settings.MaxAttempts, _settings.MaxLookbackDays);

        await SafeRunAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = TimeUntilNextRun(DateTime.UtcNow);
            _logger.LogInformation(
                "Next enrichment run in {Hours:F1}h", delay.TotalHours);

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

        _logger.LogInformation("Flight enrichment service stopped");
    }

    private async Task SafeRunAsync(CancellationToken ct)
    {
        try
        {
            await RunNightlyAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during nightly enrichment run");
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

    private async Task RunNightlyAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AltusIqDbContext>();
        var flightsClient = scope.ServiceProvider.GetRequiredService<IOpenSkyFlightsClient>();

        var cutoff = DateTime.UtcNow.AddMinutes(-_settings.MinAgeMinutes);
        var earliest = DateTime.UtcNow.Date.AddDays(-_settings.MaxLookbackDays);

        var dates = await db.Flights
            .Where(f => f.ClosedAt != null
                && f.EnrichedAt == null
                && f.ClosedAt < cutoff
                && f.ClosedAt >= earliest
                && f.EnrichmentAttempts < _settings.MaxAttempts)
            .Select(f => f.ClosedAt!.Value.Date)
            .Distinct()
            .OrderBy(d => d)
            .ToListAsync(ct);

        if (dates.Count == 0)
        {
            _logger.LogInformation(
                "Enrichment run: no pending flights, skipping (0 credits spent)");
            return;
        }

        _logger.LogInformation(
            "Enrichment run: {Count} date(s) with pending flights: {Dates}",
            dates.Count, string.Join(", ", dates.Select(d => d.ToString("yyyy-MM-dd"))));

        foreach (var date in dates)
            await EnrichDateAsync(db, flightsClient, date, cutoff, ct);
    }

    private async Task EnrichDateAsync(
        AltusIqDbContext db,
        IOpenSkyFlightsClient flightsClient,
        DateTime date,
        DateTime cutoff,
        CancellationToken ct)
    {
        var legsByAircraft = await FetchDayAsync(flightsClient, date, ct);

        var pending = await db.Flights
            .Where(f => f.ClosedAt != null
                && f.EnrichedAt == null
                && f.ClosedAt < cutoff
                && f.EnrichmentAttempts < _settings.MaxAttempts
                && f.ClosedAt!.Value.Date == date)
            .ToListAsync(ct);

        var enriched = 0;
        var noMatch = 0;

        foreach (var flight in pending)
        {
            flight.EnrichmentAttempts++;

            var transitBegin = new DateTimeOffset(flight.OpenedAt).ToUnixTimeSeconds();
            var transitEnd = new DateTimeOffset(flight.ClosedAt!.Value).ToUnixTimeSeconds();

            var match = legsByAircraft.TryGetValue(flight.Icao24, out var legs)
                ? BestMatch(legs, transitBegin, transitEnd)
                : null;

            if (match is not null)
            {
                flight.DepartureAirport = match.DepartureAirport;
                flight.ArrivalAirport = match.ArrivalAirport;
                flight.EnrichedAt = DateTime.UtcNow;
                enriched++;

                _logger.LogInformation(
                    "Enriched flight {Id} ({Icao24}): {Departure} -> {Arrival}",
                    flight.Id, flight.Icao24,
                    match.DepartureAirport ?? "unknown",
                    match.ArrivalAirport ?? "unknown");
            }
            else
            {
                noMatch++;
            }
        }

        await db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Enriched date {Date}: {Enriched} matched, {NoMatch} no-match of {Total} pending",
            date.ToString("yyyy-MM-dd"), enriched, noMatch, pending.Count);
    }

    private async Task<Dictionary<string, List<OpenSkyFlightInfo>>> FetchDayAsync(
        IOpenSkyFlightsClient flightsClient, DateTime date, CancellationToken ct)
    {
        var legsByAircraft = new Dictionary<string, List<OpenSkyFlightInfo>>();
        var dayStart = DateTime.SpecifyKind(date.Date, DateTimeKind.Utc);

        for (var hour = 0; hour < 24; hour += _settings.WindowHours)
        {
            var windowStart = dayStart.AddHours(hour);
            var windowEnd = windowStart.AddHours(_settings.WindowHours);

            if (windowEnd > dayStart.AddDays(1))
                windowEnd = dayStart.AddDays(1).AddSeconds(-1);

            var beginUnix = new DateTimeOffset(windowStart).ToUnixTimeSeconds();
            var endUnix = new DateTimeOffset(windowEnd).ToUnixTimeSeconds();

            var legs = await flightsClient.GetAllFlightsAsync(beginUnix, endUnix, ct);

            foreach (var leg in legs)
            {
                if (!legsByAircraft.TryGetValue(leg.Icao24, out var list))
                {
                    list = [];
                    legsByAircraft[leg.Icao24] = list;
                }

                list.Add(leg);
            }
        }

        return legsByAircraft;
    }

    private static OpenSkyFlightInfo? BestMatch(
        IReadOnlyList<OpenSkyFlightInfo> candidates, long transitBegin, long transitEnd)
    {
        OpenSkyFlightInfo? best = null;
        long bestOverlap = 0;

        foreach (var candidate in candidates)
        {
            var overlap = Math.Min(transitEnd, candidate.LastSeen)
                - Math.Max(transitBegin, candidate.FirstSeen);

            if (overlap > bestOverlap)
            {
                bestOverlap = overlap;
                best = candidate;
            }
        }

        return best;
    }
}
