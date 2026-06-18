using AltusIQ.Api.Data;
using AltusIQ.Api.Models.Dtos;
using Microsoft.EntityFrameworkCore;

namespace AltusIQ.Api.Services;

public class FlightQueryService(AltusIqDbContext db)
{
    public async Task<IReadOnlyList<FlightSummaryDto>> GetFlightsAsync(
        DateTime from,
        DateTime to,
        CancellationToken ct)
    {
        return await db.Flights
            .Where(f => f.ClosedAt != null
                && f.ClosedAt >= from
                && f.ClosedAt <= to)
            .OrderByDescending(f => f.ClosedAt)
            .Take(200)
            .Select(f => new FlightSummaryDto(
                f.Id,
                f.Icao24,
                f.Callsign,
                f.OriginCountry,
                f.OpenedAt,
                f.ClosedAt,
                f.LastPosition != null ? f.LastPosition.Y : null,
                f.LastPosition != null ? f.LastPosition.X : null,
                f.DepartureAirport,
                f.ArrivalAirport
            ))
            .ToListAsync(ct);
    }

    public async Task<FlightTrackDto?> GetTrackAsync(Guid id, CancellationToken ct)
    {
        var flight = await db.Flights
            .Where(f => f.Id == id)
            .FirstOrDefaultAsync(ct);

        if (flight is null)
            return null;

        return new FlightTrackDto(
            flight.Id,
            flight.Icao24,
            flight.Callsign,
            flight.OriginCountry,
            flight.OpenedAt,
            flight.ClosedAt,
            flight.TrackPoints
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
}