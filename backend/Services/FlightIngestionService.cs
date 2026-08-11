using AltusIQ.Api.Data;
using AltusIQ.Api.Models;
using AltusIQ.Api.Models.Dtos;
using Microsoft.Extensions.Options;
using NetTopologySuite.Geometries;

namespace AltusIQ.Api.Services;

public class FlightIngestionService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IngestionSettings _settings;
    private readonly ILogger<FlightIngestionService> _logger;
    private readonly GeometryFactory _geometryFactory = new(new PrecisionModel(), 4326);
    private readonly Dictionary<string, ActiveFlight> _trails = new();
    private readonly SemaphoreSlim _lock = new(1, 1);

    public FlightIngestionService(
        IServiceScopeFactory scopeFactory,
        IOptions<IngestionSettings> settings,
        ILogger<FlightIngestionService> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task ProcessAsync(IReadOnlyList<Aircraft> aircraft, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var now = DateTime.UtcNow;

            // A trail closes when the aircraft drops out of the global feed, not when
            // it leaves the ingestion bbox. Closing on bbox exit truncated every
            // outbound flight in mid-air and made ClosedAt the boundary crossing
            // rather than the landing.
            var timedOut = _trails
                .Where(kvp => (now - kvp.Value.LastSeen).TotalSeconds > _settings.GapThresholdSeconds)
                .Select(kvp => kvp.Key)
                .ToList();

            if (timedOut.Count > 0)
            {
                try
                {
                    await CloseFlightsAsync(timedOut, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex,
                        "Failed to persist {Count} closed flights, keeping them in memory to retry next poll",
                        timedOut.Count);
                }
            }

            var airborne = aircraft
                .Where(a => !a.OnGround
                    && a.Longitude.HasValue
                    && a.Latitude.HasValue)
                .GroupBy(a => a.Icao24)
                .ToDictionary(g => g.Key, g => g.First());

            foreach (var (icao, plane) in airborne)
                _trails[icao] = AppendPosition(_trails.GetValueOrDefault(icao), plane, now);
        }
        finally
        {
            _lock.Release();
        }
    }

    private ActiveFlight AppendPosition(ActiveFlight? active, Aircraft plane, DateTime now)
    {
        active ??= new ActiveFlight(
            Id: Guid.NewGuid(),
            OpenedAt: now,
            LastSeen: now,
            Callsign: plane.Callsign?.Trim(),
            OriginCountry: plane.OriginCountry,
            TrackPoints: [],
            LastRecordedAt: DateTime.MinValue,
            RegionPointCount: 0
        );

        if ((now - active.LastRecordedAt).TotalSeconds < _settings.MinPointIntervalSeconds)
            return active with { LastSeen = now };

        var point = new TrackPoint(
            Timestamp: new DateTimeOffset(now).ToUnixTimeSeconds(),
            Longitude: plane.Longitude!.Value,
            Latitude: plane.Latitude!.Value,
            Altitude: plane.BarometricAltitude,
            Heading: plane.Heading,
            Velocity: plane.Velocity
        );

        var updatedPoints = new List<TrackPoint>(active.TrackPoints) { point };

        if (updatedPoints.Count > _settings.MaxTrackPoints)
            updatedPoints.RemoveRange(0, updatedPoints.Count - _settings.MaxTrackPoints);

        // Sticky: trimming the oldest points must not erase the fact that this
        // aircraft was over the region, or a long-haul departure would stop
        // qualifying for persistence part-way through its own flight.
        var regionPointCount = active.RegionPointCount + (IsInRegion(point) ? 1 : 0);

        return active with
        {
            LastSeen = now,
            LastRecordedAt = now,
            TrackPoints = updatedPoints,
            RegionPointCount = regionPointCount
        };
    }

    public async Task<FlightTrackDto?> GetActiveTrackAsync(string icao24, CancellationToken ct)
    {
        var key = icao24.ToLowerInvariant();
        await _lock.WaitAsync(ct);
        try
        {
            var active = _trails.GetValueOrDefault(key);
            if (active is null)
                return null;

            return new FlightTrackDto(
                active.Id,
                key,
                active.Callsign,
                active.OriginCountry,
                active.OpenedAt,
                null,
                active.TrackPoints
                    .Select(p => new TrackPointDto(
                        p.Timestamp,
                        p.Longitude,
                        p.Latitude,
                        p.Altitude,
                        p.Heading,
                        p.Velocity))
                    .ToList()
            );
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task CloseFlightsAsync(List<string> icaos, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AltusIqDbContext>();

        var persisted = 0;

        foreach (var icao in icaos)
        {
            if (!_trails.TryGetValue(icao, out var active))
                continue;

            // Two in-region fixes is the same eligibility bar the bbox-filtered
            // dictionary used to apply, so the persisted flight population (and the
            // analytics built on it) stays comparable. Aircraft that only clipped a
            // corner of the region are still dropped rather than persisted at full
            // global length.
            if (active.RegionPointCount < 2)
                continue;

            var points = ThinOutOfRegionPoints(active.TrackPoints);
            if (points.Count < 2)
                continue;

            var first = points[0];
            var last = points[^1];

            db.Flights.Add(new Flight
            {
                Id = active.Id,
                Icao24 = icao,
                Callsign = active.Callsign,
                OriginCountry = active.OriginCountry,
                OpenedAt = DateTimeOffset.FromUnixTimeSeconds(first.Timestamp).UtcDateTime,
                ClosedAt = DateTimeOffset.FromUnixTimeSeconds(last.Timestamp).UtcDateTime,
                LastPosition = _geometryFactory.CreatePoint(
                    new Coordinate(last.Longitude, last.Latitude)),
                LastAltitude = last.Altitude,
                // Peak over the untrimmed track: LastAltitude is now a landing
                // altitude for most flights, so it no longer works as a cruise proxy.
                MaxAltitude = active.TrackPoints.Max(p => p.Altitude),
                TrackPoints = points
            });

            persisted++;
        }

        await db.SaveChangesAsync(ct);

        foreach (var icao in icaos)
            _trails.Remove(icao);

        if (persisted > 0)
            _logger.LogInformation("Persisted {Count} completed flights", persisted);
    }

    /// <summary>
    /// Keeps every in-region fix at full poll resolution and thins the rest to one
    /// point per OutOfRegionPointIntervalSeconds. The out-of-region leg is a straight
    /// line at cruise, so it draws identically at 5-minute spacing while costing a
    /// fraction of the jsonb.
    /// </summary>
    private List<TrackPoint> ThinOutOfRegionPoints(List<TrackPoint> points)
    {
        if (points.Count < 3 || _settings.OutOfRegionPointIntervalSeconds <= _settings.MinPointIntervalSeconds)
            return new List<TrackPoint>(points);

        var kept = new List<TrackPoint>(points.Count) { points[0] };

        for (var i = 1; i < points.Count - 1; i++)
        {
            var point = points[i];

            // The neighbour checks keep the fixes on either side of a boundary
            // crossing, so the line enters and leaves the region on the real track
            // rather than cutting a chord across it.
            if (IsInRegion(point) || IsInRegion(points[i - 1]) || IsInRegion(points[i + 1]))
            {
                kept.Add(point);
                continue;
            }

            if (point.Timestamp - kept[^1].Timestamp >= _settings.OutOfRegionPointIntervalSeconds)
                kept.Add(point);
        }

        kept.Add(points[^1]);
        return kept;
    }

    private bool IsInRegion(TrackPoint point) =>
        point.Longitude >= _settings.MinLon && point.Longitude <= _settings.MaxLon
        && point.Latitude >= _settings.MinLat && point.Latitude <= _settings.MaxLat;
}

internal record ActiveFlight(
    Guid Id,
    DateTime OpenedAt,
    DateTime LastSeen,
    string? Callsign,
    string? OriginCountry,
    List<TrackPoint> TrackPoints,
    DateTime LastRecordedAt,
    int RegionPointCount
);
