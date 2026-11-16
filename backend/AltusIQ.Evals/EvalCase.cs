using System.Text.Json;
using System.Text.Json.Serialization;
using AltusIQ.Api.Models;

namespace AltusIQ.Evals;

/// <summary>One fixture in cases/*.json: an event, what every tool returned, and what we expect.</summary>
public sealed class EvalCase
{
    [JsonPropertyName("name")] public required string Name { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("event")] public required EventFixture Event { get; set; }
    [JsonPropertyName("tools")] public Dictionary<string, JsonElement?> Tools { get; set; } = new();
    [JsonPropertyName("expect")] public Expectations Expect { get; set; } = new();
    [JsonPropertyName("replay")] public Replay? ReplayScript { get; set; }

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static EvalCase Load(string path)
    {
        var c = JsonSerializer.Deserialize<EvalCase>(File.ReadAllText(path), Json)
            ?? throw new InvalidDataException($"{path}: empty case");
        return c;
    }

    public AnomalyEvent ToEvent()
    {
        var e = Event;
        var (kind, sensitive) = e.Squawk switch
        {
            "7500" => ("unlawful_interference", true),
            "7600" => ("radio_failure", false),
            _ => ("general_emergency", false)
        };

        return new AnomalyEvent
        {
            Id = e.Id ?? DeterministicId(Name),
            Icao24 = e.Icao24,
            Callsign = e.Callsign,
            OriginCountry = e.OriginCountry,
            Squawk = e.Squawk,
            Kind = e.Kind ?? kind,
            Sensitive = e.Sensitive ?? sensitive,
            FirstSeenAt = e.FirstSeenAt,
            ConfirmedAt = e.ConfirmedAt,
            LastSeenAt = e.LastSeenAt,
            ClosedAt = e.ClosedAt,
            Status = e.ClosedAt is null ? AnomalyEventStatus.Open : AnomalyEventStatus.Closed,
            FirstLongitude = e.FirstLongitude,
            FirstLatitude = e.FirstLatitude,
            FirstAltitude = e.FirstAltitude,
            LastLongitude = e.LastLongitude,
            LastLatitude = e.LastLatitude,
            LastAltitude = e.LastAltitude,
            EvidenceJson = e.Evidence is { } ev ? ev.GetRawText() : null,
            CreatedAt = e.ConfirmedAt
        };
    }

    private static Guid DeterministicId(string name)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("eval:" + name));
        return new Guid(hash.AsSpan(0, 16));
    }
}

public sealed class EventFixture
{
    [JsonPropertyName("id")] public Guid? Id { get; set; }
    [JsonPropertyName("icao24")] public required string Icao24 { get; set; }
    [JsonPropertyName("callsign")] public string? Callsign { get; set; }
    [JsonPropertyName("origin_country")] public string? OriginCountry { get; set; }
    [JsonPropertyName("squawk")] public required string Squawk { get; set; }
    [JsonPropertyName("kind")] public string? Kind { get; set; }
    [JsonPropertyName("sensitive")] public bool? Sensitive { get; set; }
    [JsonPropertyName("first_seen_at")] public DateTime FirstSeenAt { get; set; }
    [JsonPropertyName("confirmed_at")] public DateTime ConfirmedAt { get; set; }
    [JsonPropertyName("last_seen_at")] public DateTime LastSeenAt { get; set; }
    [JsonPropertyName("closed_at")] public DateTime? ClosedAt { get; set; }
    [JsonPropertyName("first_longitude")] public double? FirstLongitude { get; set; }
    [JsonPropertyName("first_latitude")] public double? FirstLatitude { get; set; }
    [JsonPropertyName("first_altitude")] public double? FirstAltitude { get; set; }
    [JsonPropertyName("last_longitude")] public double? LastLongitude { get; set; }
    [JsonPropertyName("last_latitude")] public double? LastLatitude { get; set; }
    [JsonPropertyName("last_altitude")] public double? LastAltitude { get; set; }
    [JsonPropertyName("evidence")] public JsonElement? Evidence { get; set; }
}

public sealed class Expectations
{
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("tools_called")] public List<string> ToolsCalled { get; set; } = [];
    [JsonPropertyName("must_not_contain")] public List<string> MustNotContain { get; set; } = [];
    [JsonPropertyName("grounded")] public bool? Grounded { get; set; }
}

public sealed class Replay
{
    [JsonPropertyName("steps")] public List<ReplayStep> Steps { get; set; } = [];
}

/// <summary>Either a round of tool calls or a final content reply.</summary>
public sealed class ReplayStep
{
    [JsonPropertyName("tool_calls")] public List<ReplayToolCall>? ToolCalls { get; set; }
    [JsonPropertyName("content")] public JsonElement? Content { get; set; }
}

public sealed class ReplayToolCall
{
    [JsonPropertyName("name")] public required string Name { get; set; }
    [JsonPropertyName("arguments")] public JsonElement? Arguments { get; set; }
}
