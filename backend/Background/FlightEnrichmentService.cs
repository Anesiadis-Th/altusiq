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
            "Flight enrichment service started. Interval: {Interval}s, batch: {Batch}",
            _settings.IntervalSeconds, _settings.BatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await EnrichBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during flight enrichment batch");
            }

            await Task.Delay(
                TimeSpan.FromSeconds(_settings.IntervalSeconds), stoppingToken);
        }

        _logger.LogInformation("Flight enrichment service stopped");
    }

    private async Task EnrichBatchAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AltusIqDbContext>();
        var flightsClient = scope.ServiceProvider.GetRequiredService<IOpenSkyFlightsClient>();

        var cutoff = DateTime.UtcNow.AddMinutes(-_settings.MinAgeMinutes);

        var pending = await db.Flights
            .Where(f => f.ClosedAt != null
                && f.EnrichedAt == null
                && f.ClosedAt < cutoff
                && f.EnrichmentAttempts < _settings.MaxAttempts)
            .OrderBy(f => f.ClosedAt)
            .Take(_settings.BatchSize)
            .ToListAsync(ct);

        if (pending.Count == 0)
            return;

        foreach (var flight in pending)
            await EnrichFlightAsync(flightsClient, flight, ct);

        await db.SaveChangesAsync(ct);
    }

    private async Task EnrichFlightAsync(
        IOpenSkyFlightsClient flightsClient, Flight flight, CancellationToken ct)
    {
        flight.EnrichmentAttempts++;

        var transitBegin = new DateTimeOffset(flight.OpenedAt).ToUnixTimeSeconds();
        var transitEnd = new DateTimeOffset(flight.ClosedAt!.Value).ToUnixTimeSeconds();

        var queryBegin = new DateTimeOffset(
            flight.OpenedAt.AddHours(-_settings.WindowBufferHours)).ToUnixTimeSeconds();
        var queryEnd = new DateTimeOffset(
            flight.ClosedAt.Value.AddHours(_settings.WindowBufferHours)).ToUnixTimeSeconds();

        try
        {
            var candidates = await flightsClient.GetFlightsByAircraftAsync(
                flight.Icao24, queryBegin, queryEnd, ct);

            var match = BestMatch(candidates, transitBegin, transitEnd);

            if (match is not null)
            {
                flight.DepartureAirport = match.DepartureAirport;
                flight.ArrivalAirport = match.ArrivalAirport;
                flight.EnrichedAt = DateTime.UtcNow;

                _logger.LogInformation(
                    "Enriched flight {Id} ({Icao24}): {Departure} -> {Arrival}",
                    flight.Id, flight.Icao24,
                    match.DepartureAirport ?? "unknown",
                    match.ArrivalAirport ?? "unknown");
            }
            else
            {
                _logger.LogInformation(
                    "No OpenSky flight match for {Id} ({Icao24}), attempt {Attempt}/{Max}",
                    flight.Id, flight.Icao24,
                    flight.EnrichmentAttempts, _settings.MaxAttempts);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Enrichment attempt {Attempt}/{Max} failed for flight {Id} ({Icao24})",
                flight.EnrichmentAttempts, _settings.MaxAttempts, flight.Id, flight.Icao24);
        }
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
