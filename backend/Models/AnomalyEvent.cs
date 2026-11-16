namespace AltusIQ.Api.Models;

/// <summary>
/// A confirmed emergency-squawk episode for one aircraft. Deliberately not
/// foreign-keyed to Flights: most of the globe never gets a Flights row, so the
/// evidence is snapshotted into the event instead (ADR-018).
/// </summary>
public class AnomalyEvent
{
    public Guid Id { get; set; }
    public required string Icao24 { get; set; }
    public string? Callsign { get; set; }
    public string? OriginCountry { get; set; }
    public required string Squawk { get; set; }
    public required string Kind { get; set; }
    public bool Sensitive { get; set; }

    public DateTime FirstSeenAt { get; set; }
    public DateTime ConfirmedAt { get; set; }
    public DateTime LastSeenAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public string Status { get; set; } = AnomalyEventStatus.Open;

    public double? FirstLongitude { get; set; }
    public double? FirstLatitude { get; set; }
    public double? FirstAltitude { get; set; }
    public double? LastLongitude { get; set; }
    public double? LastLatitude { get; set; }
    public double? LastAltitude { get; set; }

    public string? EvidenceJson { get; set; }

    public string? InvestigationJson { get; set; }
    public string InvestigationStatus { get; set; } = Models.InvestigationStatus.Pending;
    public bool Grounded { get; set; }
    public string? PromptVersion { get; set; }
    public string? Provider { get; set; }
    public string? Model { get; set; }
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public int? LatencyMs { get; set; }

    public DateTime CreatedAt { get; set; }
}

public static class AnomalyEventStatus
{
    public const string Open = "open";
    public const string Closed = "closed";
}

public static class InvestigationStatus
{
    public const string Pending = "pending";
    public const string Completed = "completed";
    public const string InvalidOutput = "invalid_output";
    public const string Uninvestigated = "uninvestigated";
    public const string Skipped = "skipped";
}
