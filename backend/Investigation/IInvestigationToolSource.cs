namespace AltusIQ.Api.Investigation;

/// <summary>
/// The five facts the investigator can ask for. Each returns a JSON string, or
/// null when the source has nothing. The agent caps every output at 8 KB, so
/// implementations keep their shapes compact. Live in production, recorded in
/// the eval harness and tests.
/// </summary>
public interface IInvestigationToolSource
{
    Task<string?> GetTrailAsync(string icao24, CancellationToken ct);
    Task<string?> NearestAirportsAsync(double latitude, double longitude, int limit, CancellationToken ct);
    Task<string?> GetMetarAsync(string icao, CancellationToken ct);
    Task<string?> GetRouteAsync(string callsign, CancellationToken ct);
    Task<string?> RecentFlightsAsync(string icao24, CancellationToken ct);
}

public static class ToolNames
{
    public const string GetTrail = "get_trail";
    public const string NearestAirports = "nearest_airports";
    public const string GetMetar = "get_metar";
    public const string GetRoute = "get_route";
    public const string RecentFlights = "recent_flights";

    public static readonly IReadOnlyList<string> All =
        [GetTrail, NearestAirports, GetMetar, GetRoute, RecentFlights];
}
