using AltusIQ.Api.Investigation;

namespace AltusIQ.Api.Tests.Fakes;

/// <summary>Recorded tool outputs keyed by tool name; null means the source had nothing.</summary>
internal sealed class FakeToolSource : IInvestigationToolSource
{
    public const string NearestEssa =
        """[{"icao":"ESSA","iata":"ARN","name":"Stockholm Arlanda","distance_km":12.4,"bearing_deg":210},{"icao":"ESSB","iata":"BMA","name":"Stockholm Bromma","distance_km":31.0,"bearing_deg":195}]""";

    public const string MetarEssaFog =
        """{"station":"ESSA","raw":"ESSA 011220Z 18004KT 0400 FG VV002 03/03 Q1012","report_time":"2026-08-01 12:20:00","temp_c":3,"dewpoint_c":3,"wind_dir_deg":180,"wind_kt":4,"visibility":"0.25","flight_category":"LIFR"}""";

    public Dictionary<string, string?> Outputs { get; } = new()
    {
        [ToolNames.GetTrail] = """{"icao24":"abc123","callsign":"SAS123","point_count":30,"returned":30,"columns":["unix_ts","lon","lat","alt_m","heading","velocity_ms"],"points":[[1754000000,17.9,59.6,3000,210,120]]}""",
        [ToolNames.NearestAirports] = NearestEssa,
        [ToolNames.GetMetar] = MetarEssaFog,
        [ToolNames.GetRoute] = """{"callsign":"SAS123","airline":"SAS","origin":{"icao":"ESSA","name":"Stockholm Arlanda"},"destination":{"icao":"EKCH","name":"Copenhagen"}}""",
        [ToolNames.RecentFlights] = null
    };

    public List<(string Tool, string Args)> Calls { get; } = [];

    public Task<string?> GetTrailAsync(string icao24, CancellationToken ct) => Answer(ToolNames.GetTrail, icao24);
    public Task<string?> NearestAirportsAsync(double latitude, double longitude, int limit, CancellationToken ct) => Answer(ToolNames.NearestAirports, $"{latitude},{longitude},{limit}");
    public Task<string?> GetMetarAsync(string icao, CancellationToken ct) => Answer(ToolNames.GetMetar, icao);
    public Task<string?> GetRouteAsync(string callsign, CancellationToken ct) => Answer(ToolNames.GetRoute, callsign);
    public Task<string?> RecentFlightsAsync(string icao24, CancellationToken ct) => Answer(ToolNames.RecentFlights, icao24);

    private Task<string?> Answer(string tool, string args)
    {
        Calls.Add((tool, args));
        return Task.FromResult(Outputs.GetValueOrDefault(tool));
    }
}
