using System.Globalization;
using System.Text.Json;
using AltusIQ.Api.Models;

namespace AltusIQ.Api.Investigation;

/// <summary>
/// The brief written when no model runs: every provider failed, the daily
/// budget is spent, or Llm:Enabled is false. Assembled by code from the tool
/// outputs. It states facts and lists unknowns; it never offers a hypothesis,
/// because code has no basis to.
/// </summary>
public static class DeterministicBrief
{
    public static InvestigationBrief Build(AnomalyEvent evt, IReadOnlyDictionary<string, string?> toolOutputs)
    {
        var inv = CultureInfo.InvariantCulture;
        var observations = new List<string>();
        var unknowns = new List<string>
        {
            "The reason for the squawk is not observable from ADS-B; only position, altitude and the code itself are."
        };

        var duration = (evt.LastSeenAt - evt.FirstSeenAt).TotalMinutes;
        observations.Add(string.Format(inv,
            "Squawk {0} ({1}) held for about {2} minutes, from {3:HH:mm}Z to {4:HH:mm}Z, polled every 120 seconds.",
            evt.Squawk, evt.Kind.Replace('_', ' '), Math.Round(duration), evt.FirstSeenAt, evt.LastSeenAt));

        if (evt.LastLatitude is not null && evt.LastLongitude is not null)
        {
            observations.Add(string.Format(inv,
                "Last fix on the code at {0:0.0000}, {1:0.0000}{2}.",
                evt.LastLatitude, evt.LastLongitude,
                evt.LastAltitude is null ? "" : string.Format(inv, " at {0} m barometric", Math.Round(evt.LastAltitude.Value))));
        }

        var trailPoints = 0;
        if (TryParse(toolOutputs, ToolNames.GetTrail, out var trail) && trail.TryGetProperty("point_count", out var pc))
        {
            trailPoints = pc.GetInt32();
            observations.Add(string.Format(inv, "{0} trail points were available for the aircraft.", trailPoints));
        }
        else
        {
            observations.Add("No trail was available for the aircraft.");
            unknowns.Add("Where the aircraft came from and went afterwards.");
        }

        string? nearestIcao = null;
        if (TryParse(toolOutputs, ToolNames.NearestAirports, out var airports) && airports.ValueKind == JsonValueKind.Array && airports.GetArrayLength() > 0)
        {
            var first = airports[0];
            nearestIcao = first.GetProperty("icao").GetString();
            observations.Add(string.Format(inv,
                "Nearest airport to the last fix: {0} {1}, {2} km away.",
                nearestIcao, first.TryGetProperty("name", out var n) ? n.GetString() : "", first.GetProperty("distance_km").GetDouble()));
        }

        var hasMetar = false;
        if (TryParse(toolOutputs, ToolNames.GetMetar, out var metar))
        {
            if (metar.TryGetProperty("raw", out var raw) && raw.ValueKind == JsonValueKind.String)
            {
                hasMetar = true;
                observations.Add($"METAR {metar.GetProperty("station").GetString()}: {raw.GetString()}");
            }
            else if (metar.TryGetProperty("error", out var err))
            {
                unknowns.Add($"Weather at {metar.GetProperty("station").GetString()}: METAR fetch failed ({err.GetString()}).");
            }
        }
        else
        {
            unknowns.Add("Weather near the last fix: no METAR was fetched.");
        }

        if (TryParse(toolOutputs, ToolNames.GetRoute, out var route))
        {
            observations.Add(string.Format(inv,
                "Callsign route from adsbdb: {0} to {1}.",
                route.GetProperty("origin").GetProperty("icao").GetString(),
                route.GetProperty("destination").GetProperty("icao").GetString()));
        }
        else
        {
            unknowns.Add("Planned route: no adsbdb entry for the callsign.");
        }

        if (TryParse(toolOutputs, ToolNames.RecentFlights, out var recent) && recent.ValueKind == JsonValueKind.Array)
            observations.Add(string.Format(inv, "{0} earlier stored flights for this airframe.", recent.GetArrayLength()));

        var quality = trailPoints >= 20 && hasMetar && nearestIcao is not null ? "good"
            : trailPoints >= 5 ? "fair"
            : "poor";

        var summary = string.Format(inv,
            "Data-only summary assembled by code; no model was consulted. Squawk {0} confirmed on {1} ({2}) at {3:yyyy-MM-dd HH:mm}Z. {4}",
            evt.Squawk, evt.Icao24, evt.Callsign ?? "no callsign", evt.ConfirmedAt,
            nearestIcao is null ? "No airport context." : $"Nearest airport {nearestIcao}.");

        return new InvestigationBrief(
            summary,
            observations,
            [],
            unknowns,
            quality,
            "Nothing here is verifiable from ADS-B alone; an operator or authority statement is the only source for why the code was set.");
    }

    private static bool TryParse(IReadOnlyDictionary<string, string?> outputs, string tool, out JsonElement element)
    {
        element = default;
        if (!outputs.TryGetValue(tool, out var json) || string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(json);
            element = doc.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
