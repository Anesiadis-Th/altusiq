using System.Text.Json;
using AltusIQ.Api.Models;

namespace AltusIQ.Api.Services;

internal record ParsedStates(IReadOnlyList<Aircraft> Aircraft, int Skipped);

/// <summary>
/// Turns an OpenSky /states/all body into aircraft, dropping rows it cannot use
/// rather than letting one bad row throw away the whole poll.
/// </summary>
internal static class OpenSkyStateParser
{
    // Highest index read is 11 (vertical_rate), so anything shorter is unusable.
    private const int RequiredStateFields = 12;

    internal static ParsedStates Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // OpenSky sends "states": null when nothing matches. Any other non-array
        // shape is a broken response and is left to throw: a poll that quietly
        // returns zero aircraft would blank the map and still report healthy.
        if (!root.TryGetProperty("states", out var states)
            || states.ValueKind == JsonValueKind.Null)
            return new ParsedStates([], 0);

        var aircraft = new List<Aircraft>();
        var skipped = 0;

        foreach (var state in states.EnumerateArray())
        {
            var parsed = TryParseState(state);

            if (parsed is null)
                skipped++;
            else
                aircraft.Add(parsed);
        }

        return new ParsedStates(aircraft, skipped);
    }

    internal static Aircraft? TryParseState(JsonElement state)
    {
        if (state.ValueKind != JsonValueKind.Array
            || state.GetArrayLength() < RequiredStateFields)
            return null;

        var icao24 = ReadString(state[0]);

        // icao24 keys both in-memory dictionaries, so an identity-less row collides.
        if (string.IsNullOrWhiteSpace(icao24))
            return null;

        if (state[4].ValueKind != JsonValueKind.Number
            || state[5].ValueKind != JsonValueKind.Number
            || state[6].ValueKind != JsonValueKind.Number)
            return null;

        return new Aircraft
        {
            Icao24             = icao24,
            Callsign           = ReadString(state[1])?.Trim(),
            OriginCountry      = ReadString(state[2]),
            Longitude          = state[5].GetDouble(),
            Latitude           = state[6].GetDouble(),
            BarometricAltitude = ReadDouble(state[7]),
            // GetBoolean() throws on null, and a null here means "not reported".
            OnGround           = state[8].ValueKind == JsonValueKind.True,
            Velocity           = ReadDouble(state[9]),
            Heading            = ReadDouble(state[10]),
            VerticalRate       = ReadDouble(state[11]),
            LastContact        = state[4].GetInt64()
        };
    }

    private static string? ReadString(JsonElement element) =>
        element.ValueKind == JsonValueKind.String ? element.GetString() : null;

    private static double? ReadDouble(JsonElement element) =>
        element.ValueKind == JsonValueKind.Number ? element.GetDouble() : null;
}
