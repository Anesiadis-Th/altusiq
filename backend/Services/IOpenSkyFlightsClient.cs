namespace AltusIQ.Api.Services;

public interface IOpenSkyFlightsClient
{
    Task<IReadOnlyList<OpenSkyFlightInfo>> GetFlightsByAircraftAsync(
        string icao24,
        long beginUnix,
        long endUnix,
        CancellationToken cancellationToken = default);
}

public record OpenSkyFlightInfo(
    long FirstSeen,
    long LastSeen,
    string? DepartureAirport,
    string? ArrivalAirport
);
