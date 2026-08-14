using AltusIQ.Api.Data;
using AltusIQ.Api.Models.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace AltusIQ.Api.Services;

public class AnalyticsService(AltusIqDbContext db, IMemoryCache cache)
{
    private const int WindowDays = 15;
    private const int TopN = 10;
    private const string CacheKey = "analytics";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    // Display order. Labels must match the ones in GetAltitudeBandsAsync.
    private static readonly string[] BandOrder =
    [
        "0–2 km", "2–4 km", "4–6 km", "6–8 km", "8–10 km", "10–12 km", "12 km+"
    ];

    public async Task<AnalyticsResponseDto> GetAnalyticsAsync(CancellationToken ct)
    {
        var result = await cache.GetOrCreateAsync(CacheKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheTtl;
            return ComputeAnalyticsAsync(ct);
        });
        return result!;
    }

    private async Task<AnalyticsResponseDto> ComputeAnalyticsAsync(CancellationToken ct)
    {
        var to = DateTime.UtcNow;
        var from = to.AddDays(-WindowDays);

        // Every aggregation below builds on this. ClosedAt is indexed.
        var window = db.Flights.Where(f =>
            f.ClosedAt != null && f.ClosedAt >= from && f.ClosedAt <= to);

        var totalFlights = await window.CountAsync(ct);

        var enrichedFlights = await window
            .CountAsync(f => f.DepartureAirport != null || f.ArrivalAirport != null, ct);

        var busiestAirports = await GetBusiestAirportsAsync(window, ct);
        var topRoutes = await GetTopRoutesAsync(window, ct);
        var flightsPerDay = await GetFlightsPerDayAsync(window, from, to, ct);
        var flightsPerHour = await GetFlightsPerHourAsync(window, ct);
        var altitudeBands = await GetAltitudeBandsAsync(window, ct);

        return new AnalyticsResponseDto(
            WindowDays,
            from,
            to,
            totalFlights,
            enrichedFlights,
            busiestAirports,
            topRoutes,
            flightsPerDay,
            flightsPerHour,
            altitudeBands);
    }

    private static async Task<IReadOnlyList<AirportTrafficDto>> GetBusiestAirportsAsync(
        IQueryable<Models.Flight> window, CancellationToken ct)
    {
        // An airport counts as busy either way, so group each column in SQL and
        // merge the two small result sets here instead of a UNION.
        var departures = await window
            .Where(f => f.DepartureAirport != null)
            .GroupBy(f => f.DepartureAirport!)
            .Select(g => new { Icao = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var arrivals = await window
            .Where(f => f.ArrivalAirport != null)
            .GroupBy(f => f.ArrivalAirport!)
            .Select(g => new { Icao = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var byAirport = new Dictionary<string, (int Departures, int Arrivals)>();

        foreach (var d in departures)
        {
            byAirport.TryGetValue(d.Icao, out var v);
            byAirport[d.Icao] = (v.Departures + d.Count, v.Arrivals);
        }

        foreach (var a in arrivals)
        {
            byAirport.TryGetValue(a.Icao, out var v);
            byAirport[a.Icao] = (v.Departures, v.Arrivals + a.Count);
        }

        return byAirport
            .Select(kvp => new AirportTrafficDto(
                kvp.Key,
                kvp.Value.Departures,
                kvp.Value.Arrivals,
                kvp.Value.Departures + kvp.Value.Arrivals))
            .OrderByDescending(a => a.Total)
            .Take(TopN)
            .ToList();
    }

    private static async Task<IReadOnlyList<RouteDto>> GetTopRoutesAsync(
        IQueryable<Models.Flight> window, CancellationToken ct)
    {
        // EF can order an anonymous projection but not a record constructor, so
        // project first and map to the DTO afterwards.
        var rows = await window
            // Same-airport pairs are local GA and training circuits, not routes.
            .Where(f => f.DepartureAirport != null && f.ArrivalAirport != null
                && f.DepartureAirport != f.ArrivalAirport)
            .GroupBy(f => new { f.DepartureAirport, f.ArrivalAirport })
            .Select(g => new
            {
                g.Key.DepartureAirport,
                g.Key.ArrivalAirport,
                Count = g.Count()
            })
            .OrderByDescending(r => r.Count)
            .Take(TopN)
            .ToListAsync(ct);

        return rows
            .Select(r => new RouteDto(r.DepartureAirport!, r.ArrivalAirport!, r.Count))
            .ToList();
    }

    private static async Task<IReadOnlyList<FlightsPerDayDto>> GetFlightsPerDayAsync(
        IQueryable<Models.Flight> window, DateTime from, DateTime to, CancellationToken ct)
    {
        var rows = await window
            .GroupBy(f => f.ClosedAt!.Value.Date)
            .Select(g => new { Day = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var counts = rows.ToDictionary(r => DateOnly.FromDateTime(r.Day), r => r.Count);

        // Gap-fill so a quiet day is a zero, not a hole in the line.
        var days = new List<FlightsPerDayDto>();
        for (var day = DateOnly.FromDateTime(from); day <= DateOnly.FromDateTime(to); day = day.AddDays(1))
            days.Add(new FlightsPerDayDto(day, counts.GetValueOrDefault(day)));

        return days;
    }

    private static async Task<IReadOnlyList<FlightsPerHourDto>> GetFlightsPerHourAsync(
        IQueryable<Models.Flight> window, CancellationToken ct)
    {
        var rows = await window
            .GroupBy(f => f.ClosedAt!.Value.Hour)
            .Select(g => new { Hour = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var counts = rows.ToDictionary(r => r.Hour, r => r.Count);

        return Enumerable.Range(0, 24)
            .Select(h => new FlightsPerHourDto(h, counts.GetValueOrDefault(h)))
            .ToList();
    }

    private static async Task<IReadOnlyList<AltitudeBandDto>> GetAltitudeBandsAsync(
        IQueryable<Models.Flight> window, CancellationToken ct)
    {
        // Tracks run to landing now, so LastAltitude reports touchdown and dumps
        // everything in the bottom band. Only older rows still fall back to it.
        // The nested ternary becomes a SQL CASE, so bucketing stays server-side.
        var rows = await window
            .Select(f => new { Altitude = f.MaxAltitude ?? f.LastAltitude })
            .Where(f => f.Altitude != null)
            .GroupBy(f =>
                f.Altitude < 2000 ? "0–2 km" :
                f.Altitude < 4000 ? "2–4 km" :
                f.Altitude < 6000 ? "4–6 km" :
                f.Altitude < 8000 ? "6–8 km" :
                f.Altitude < 10000 ? "8–10 km" :
                f.Altitude < 12000 ? "10–12 km" : "12 km+")
            .Select(g => new { Band = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var counts = rows.ToDictionary(r => r.Band, r => r.Count);

        // Empty bands still get a zero so the chart stays continuous.
        return BandOrder
            .Select(label => new AltitudeBandDto(label, counts.GetValueOrDefault(label)))
            .ToList();
    }
}
