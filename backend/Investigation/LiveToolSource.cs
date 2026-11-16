using System.Text.Json;
using AltusIQ.Api.Data;
using AltusIQ.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace AltusIQ.Api.Investigation;

/// <summary>Production tool source: ingestion trails, the embedded airport set, METAR, adsbdb and the Flights table.</summary>
public sealed class LiveToolSource : IInvestigationToolSource
{
    public const int MaxTrailPoints = 120;

    private readonly IActiveTrackSource _tracks;
    private readonly AirportIndex _airports;
    private readonly MetarClient _metar;
    private readonly IRouteLookupService _routes;
    private readonly IServiceScopeFactory _scopeFactory;

    public LiveToolSource(
        IActiveTrackSource tracks,
        AirportIndex airports,
        MetarClient metar,
        IRouteLookupService routes,
        IServiceScopeFactory scopeFactory)
    {
        _tracks = tracks;
        _airports = airports;
        _metar = metar;
        _routes = routes;
        _scopeFactory = scopeFactory;
    }

    public async Task<string?> GetTrailAsync(string icao24, CancellationToken ct)
    {
        var track = await _tracks.GetActiveTrackAsync(icao24, ct);
        if (track is null || track.TrackPoints.Count == 0)
            return null;

        return TrailJson.Compact(track.Icao24, track.Callsign, track.TrackPoints, MaxTrailPoints);
    }

    public Task<string?> NearestAirportsAsync(double latitude, double longitude, int limit, CancellationToken ct)
    {
        var nearby = _airports.Nearest(latitude, longitude, limit)
            .Select(n => new
            {
                icao = n.Airport.Icao,
                iata = n.Airport.Iata,
                name = n.Airport.Name,
                distance_km = Math.Round(n.DistanceKm, 1),
                bearing_deg = Math.Round(n.BearingDeg)
            });

        return Task.FromResult<string?>(JsonSerializer.Serialize(nearby));
    }

    public async Task<string?> GetMetarAsync(string icao, CancellationToken ct) =>
        await _metar.GetMetarJsonAsync(icao, ct);

    public async Task<string?> GetRouteAsync(string callsign, CancellationToken ct)
    {
        var route = await _routes.GetRouteAsync(callsign, ct);
        if (route is null)
            return null;

        return JsonSerializer.Serialize(new
        {
            callsign = route.Callsign,
            airline = route.AirlineName,
            origin = new { icao = route.Origin.IcaoCode, name = route.Origin.Name, municipality = route.Origin.Municipality, country = route.Origin.CountryName },
            destination = new { icao = route.Destination.IcaoCode, name = route.Destination.Name, municipality = route.Destination.Municipality, country = route.Destination.CountryName }
        });
    }

    public async Task<string?> RecentFlightsAsync(string icao24, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AltusIqDbContext>();

        var key = icao24.ToLowerInvariant();
        var flights = await db.Flights
            .Where(f => f.Icao24 == key && f.ClosedAt != null)
            .OrderByDescending(f => f.ClosedAt)
            .Take(5)
            .Select(f => new
            {
                callsign = f.Callsign,
                opened_at = f.OpenedAt,
                closed_at = f.ClosedAt,
                departure = f.DepartureAirport,
                arrival = f.ArrivalAirport,
                max_altitude_m = f.MaxAltitude
            })
            .ToListAsync(ct);

        return flights.Count == 0 ? null : JsonSerializer.Serialize(flights);
    }
}

public static class TrailJson
{
    /// <summary>
    /// Rows of [unix_ts, lon, lat, alt_m, heading, velocity_ms], thinned evenly.
    /// 120 rows is ~5 KB, inside the tool cap with room for the header.
    /// </summary>
    public static string Compact(string icao24, string? callsign, IReadOnlyList<Models.Dtos.TrackPointDto> points, int max)
    {
        var thinned = Services.EmergencySquawkDetector.Thin(points, max);

        return JsonSerializer.Serialize(new
        {
            icao24,
            callsign,
            point_count = points.Count,
            returned = thinned.Count,
            columns = new[] { "unix_ts", "lon", "lat", "alt_m", "heading", "velocity_ms" },
            points = thinned.Select(p => new object?[]
            {
                p.Timestamp,
                Math.Round(p.Longitude, 4),
                Math.Round(p.Latitude, 4),
                p.Altitude is null ? null : Math.Round(p.Altitude.Value),
                p.Heading is null ? null : Math.Round(p.Heading.Value),
                p.Velocity is null ? null : Math.Round(p.Velocity.Value)
            })
        });
    }
}
