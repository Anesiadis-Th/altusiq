using AltusIQ.Api.Data;
using AltusIQ.Api.Models.Dtos;
using Microsoft.EntityFrameworkCore;

namespace AltusIQ.Api.Services;

public class AnalyticsService(AltusIqDbContext db)
{
    private const int WindowDays = 15;
    private const int TopN = 10;

    // Altitude band labels in display order. Must match the CASE expression below.
    private static readonly string[] BandOrder =
    [
        "0–2 km", "2–4 km", "4–6 km", "6–8 km", "8–10 km", "10–12 km", "12 km+"
    ];

    public async Task<AnalyticsResponseDto> GetAnalyticsAsync(CancellationToken ct)
    {
        var to = DateTime.UtcNow;
        var from = to.AddDays(-WindowDays);

        // Shared 15-day window over closed flights. ClosedAt is indexed.
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
        // An airport is "busy" as either an origin or a destination, so we
        // aggregate each column independently in SQL and merge the two small
        // result sets in memory rather than scanning the table twice with a UNION.
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
        // Project to an anonymous type before ordering: EF can translate an
        // OrderBy over an anonymous projection but not over a record constructor.
        var rows = await window
            // Exclude same-airport pairs (departure == arrival): these are local
            // training/skydiving/GA circuits, not origin→destination routes.
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
        // Group by the UTC calendar day of ClosedAt (translates to date_trunc).
        var rows = await window
            .GroupBy(f => f.ClosedAt!.Value.Date)
            .Select(g => new { Day = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var counts = rows.ToDictionary(r => DateOnly.FromDateTime(r.Day), r => r.Count);

        // Emit every calendar day in the window so a quiet/missing day shows as a
        // zero point rather than a gap in the time series.
        var days = new List<FlightsPerDayDto>();
        for (var day = DateOnly.FromDateTime(from); day <= DateOnly.FromDateTime(to); day = day.AddDays(1))
            days.Add(new FlightsPerDayDto(day, counts.GetValueOrDefault(day)));

        return days;
    }

    private static async Task<IReadOnlyList<FlightsPerHourDto>> GetFlightsPerHourAsync(
        IQueryable<Models.Flight> window, CancellationToken ct)
    {
        // Hour-of-day distribution (0–23, UTC) across the whole window.
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
        // Bucket by last reported altitude. The nested ternary translates to a SQL
        // CASE, so the bucketing happens server-side with no jsonb/raw SQL.
        var rows = await window
            .Where(f => f.LastAltitude != null)
            .GroupBy(f =>
                f.LastAltitude < 2000 ? "0–2 km" :
                f.LastAltitude < 4000 ? "2–4 km" :
                f.LastAltitude < 6000 ? "4–6 km" :
                f.LastAltitude < 8000 ? "6–8 km" :
                f.LastAltitude < 10000 ? "8–10 km" :
                f.LastAltitude < 12000 ? "10–12 km" : "12 km+")
            .Select(g => new { Band = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var counts = rows.ToDictionary(r => r.Band, r => r.Count);

        // Emit every band in display order, defaulting absent bands to zero so the
        // chart shows a continuous distribution.
        return BandOrder
            .Select(label => new AltitudeBandDto(label, counts.GetValueOrDefault(label)))
            .ToList();
    }
}
