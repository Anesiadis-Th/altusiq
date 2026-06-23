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
    private readonly Dictionary<string, ActiveFlight> _activeFlights = new();
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

            var timedOut = _activeFlights
                .Where(kvp => (now - kvp.Value.LastSeen).TotalSeconds > _settings.GapThresholdSeconds)
                .Select(kvp => kvp.Key)
                .ToList();

            if (timedOut.Count > 0)
                await CloseFlightsAsync(timedOut, now, ct);

            var inRegion = aircraft
                .Where(a => !a.OnGround
                    && a.Longitude.HasValue
                    && a.Latitude.HasValue
                    && a.Longitude >= _settings.MinLon && a.Longitude <= _settings.MaxLon
                    && a.Latitude >= _settings.MinLat && a.Latitude <= _settings.MaxLat)
                .GroupBy(a => a.Icao24)
                .ToDictionary(g => g.Key, g => g.First());

            foreach (var (icao, plane) in inRegion)
            {
                if (!_activeFlights.TryGetValue(icao, out var active))
                {
                    active = new ActiveFlight(
                        Id: Guid.NewGuid(),
                        OpenedAt: now,
                        LastSeen: now,
                        Callsign: plane.Callsign?.Trim(),
                        OriginCountry: plane.OriginCountry,
                        TrackPoints: [],
                        LastRecordedAt: DateTime.MinValue,
                        LastLon: plane.Longitude,
                        LastLat: plane.Latitude
                    );
                }

                if ((now - active.LastRecordedAt).TotalSeconds >= _settings.MinPointIntervalSeconds)
                {
                    var point = new TrackPoint(
                        Timestamp: new DateTimeOffset(now).ToUnixTimeSeconds(),
                        Longitude: plane.Longitude!.Value,
                        Latitude: plane.Latitude!.Value,
                        Altitude: plane.BarometricAltitude,
                        Heading: plane.Heading,
                        Velocity: plane.Velocity
                    );

                    var updatedPoints = new List<TrackPoint>(active.TrackPoints);
                    updatedPoints.Add(point);

                    if (updatedPoints.Count > _settings.MaxTrackPoints)
                        updatedPoints.RemoveRange(0, updatedPoints.Count - _settings.MaxTrackPoints);

                    active = active with
                    {
                        LastSeen = now,
                        LastRecordedAt = now,
                        LastLon = plane.Longitude,
                        LastLat = plane.Latitude,
                        TrackPoints = updatedPoints
                    };
                }
                else
                {
                    active = active with { LastSeen = now };
                }

                _activeFlights[icao] = active;
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<FlightTrackDto?> GetActiveTrackAsync(string icao24, CancellationToken ct)
    {
        var key = icao24.ToLowerInvariant();
        await _lock.WaitAsync(ct);
        try
        {
            if (!_activeFlights.TryGetValue(key, out var active))
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

    private async Task CloseFlightsAsync(List<string> icaos, DateTime closedAt, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AltusIqDbContext>();

        foreach (var icao in icaos)
        {
            if (!_activeFlights.TryGetValue(icao, out var active))
                continue;

            if (active.TrackPoints.Count >= 2)
            {
                var last = active.TrackPoints[^1];

                db.Flights.Add(new Flight
                {
                    Id = active.Id,
                    Icao24 = icao,
                    Callsign = active.Callsign,
                    OriginCountry = active.OriginCountry,
                    OpenedAt = active.OpenedAt,
                    ClosedAt = closedAt,
                    LastPosition = _geometryFactory.CreatePoint(
                        new Coordinate(last.Longitude, last.Latitude)),
                    LastAltitude = last.Altitude,
                    TrackPoints = active.TrackPoints
                });
            }

            _activeFlights.Remove(icao);
        }

        var saved = await db.SaveChangesAsync(ct);
        if (saved > 0)
            _logger.LogInformation("Persisted {Count} completed flights", saved);
    }
}

internal record ActiveFlight(
    Guid Id,
    DateTime OpenedAt,
    DateTime LastSeen,
    string? Callsign,
    string? OriginCountry,
    List<TrackPoint> TrackPoints,
    DateTime LastRecordedAt,
    double? LastLon,
    double? LastLat
);