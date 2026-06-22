namespace AltusIQ.Api.Services;

public interface IOpenSkyFlightsClient
{
    Task<IReadOnlyList<OpenSkyFlightInfo>> GetAllFlightsAsync(
        long beginUnix,
        long endUnix,
        CancellationToken cancellationToken = default);
}

public record OpenSkyFlightInfo(
    string Icao24,
    long FirstSeen,
    long LastSeen,
    string? DepartureAirport,
    string? ArrivalAirport
);
