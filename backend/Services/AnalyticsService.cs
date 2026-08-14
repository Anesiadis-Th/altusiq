using AltusIQ.Api.Data;
using AltusIQ.Api.Models.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace AltusIQ.Api.Services;

public class AnalyticsService(AltusIqDbContext db, IMemoryCache cache)
{
    // 14 and not 15: retention measures its window from the moment it runs, so
    // the 15th day back is already partly purged. See ADR-013.
    private const int WindowDays = 14;
    private const int TopN = 10;
    private const string CacheKey = "analytics";

    public static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    public async Task<AnalyticsResponseDto> GetAnalyticsAsync(CancellationToken ct)
    {
        var result = await cache.GetOrCreateAsync(CacheKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheTtl;
            return ComputeAnalyticsAsync(ct);
        });
        return result!;
    }

    // The warmer calls this on a shorter period than the TTL, so the entry is
    // replaced before it can expire and no reader pays for the aggregation.
    public async Task<AnalyticsResponseDto> RefreshAsync(CancellationToken ct)
    {
        var result = await ComputeAnalyticsAsync(ct);
        cache.Set(CacheKey, result, CacheTtl);
        return result;
    }

    private async Task<AnalyticsResponseDto> ComputeAnalyticsAsync(CancellationToken ct)
    {
        // Whole UTC days, `to` exclusive. A window ending at "now" leaves the
        // first and last buckets partial and draws a cliff that isn't real.
        var to = DateTime.UtcNow.Date;
        var from = to.AddDays(-WindowDays);

        // Every aggregation below builds on this. ClosedAt is indexed.
        var window = db.Flights.Where(f =>
            f.ClosedAt != null && f.ClosedAt >= from && f.ClosedAt < to);

        var totalFlights = await window.CountAsync(ct);

        var enrichedFlights = await window
            .CountAsync(f => f.DepartureAirport != null || f.ArrivalAirport != null, ct);

        var airports = await GetAirportTrafficAsync(window, ct);
        var topRoutes = await GetTopRoutesAsync(window, ct);
        var distinctRoutes = await GetDistinctRouteCountAsync(window, ct);
        var flightsPerDay = await GetFlightsPerDayAsync(window, from, to, ct);
        var flightsPerHour = await GetFlightsPerHourAsync(window, ct);

        return new AnalyticsResponseDto(
            WindowDays,
            from,
            to,
            totalFlights,
            enrichedFlights,
            airports.Total,
            distinctRoutes,
            airports.Top,
            topRoutes,
            flightsPerDay,
            flightsPerHour);
    }

    private static async Task<(int Total, IReadOnlyList<AirportTrafficDto> Top)>
        GetAirportTrafficAsync(IQueryable<Models.Flight> window, CancellationToken ct)
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

        var top = byAirport
            .Select(kvp => new AirportTrafficDto(
                kvp.Key,
                kvp.Value.Departures,
                kvp.Value.Arrivals,
                kvp.Value.Departures + kvp.Value.Arrivals))
            .OrderByDescending(a => a.Total)
            .Take(TopN)
            .ToList();

        // The dictionary already holds every airport, so the denominator is free.
        return (byAirport.Count, top);
    }

    // Same-airport pairs are training circuits, not routes. Shared so the top 10
    // and the "of N" denominator agree on what counts.
    private static IQueryable<Models.Flight> RoutedFlights(IQueryable<Models.Flight> window) =>
        window.Where(f =>
            f.DepartureAirport != null
            && f.ArrivalAirport != null
            && f.DepartureAirport != f.ArrivalAirport);

    private static async Task<IReadOnlyList<RouteDto>> GetTopRoutesAsync(
        IQueryable<Models.Flight> window, CancellationToken ct)
    {
        // EF can order an anonymous projection but not a record constructor, so
        // project first and map to the DTO afterwards.
        var rows = await RoutedFlights(window)
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

    private static Task<int> GetDistinctRouteCountAsync(
        IQueryable<Models.Flight> window, CancellationToken ct) =>
        RoutedFlights(window)
            .Select(f => new { f.DepartureAirport, f.ArrivalAirport })
            .Distinct()
            .CountAsync(ct);

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
        for (var day = DateOnly.FromDateTime(from); day < DateOnly.FromDateTime(to); day = day.AddDays(1))
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
}
