using System.Text.Json;
using AltusIQ.Api.Investigation;

namespace AltusIQ.Evals;

/// <summary>Tool outputs recorded in the case file. Nothing leaves the process.</summary>
public sealed class RecordedToolSource : IInvestigationToolSource
{
    private readonly IReadOnlyDictionary<string, JsonElement?> _outputs;

    public RecordedToolSource(IReadOnlyDictionary<string, JsonElement?> outputs)
    {
        _outputs = outputs;
    }

    public List<string> Calls { get; } = [];

    public Task<string?> GetTrailAsync(string icao24, CancellationToken ct) => Answer(ToolNames.GetTrail);
    public Task<string?> NearestAirportsAsync(double latitude, double longitude, int limit, CancellationToken ct) => Answer(ToolNames.NearestAirports);
    public Task<string?> GetMetarAsync(string icao, CancellationToken ct) => Answer(ToolNames.GetMetar);
    public Task<string?> GetRouteAsync(string callsign, CancellationToken ct) => Answer(ToolNames.GetRoute);
    public Task<string?> RecentFlightsAsync(string icao24, CancellationToken ct) => Answer(ToolNames.RecentFlights);

    private Task<string?> Answer(string tool)
    {
        Calls.Add(tool);

        if (!_outputs.TryGetValue(tool, out var value) || value is null || value.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return Task.FromResult<string?>(null);

        return Task.FromResult<string?>(value.Value.GetRawText());
    }
}
