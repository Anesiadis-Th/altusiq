using System.Text.Json;

namespace AltusIQ.Api.Models.Dtos;

public record AnomalyEventSummaryDto(
    Guid Id,
    string Icao24,
    string? Callsign,
    string? OriginCountry,
    string Squawk,
    string Kind,
    DateTime ConfirmedAt,
    DateTime LastSeenAt,
    DateTime? ClosedAt,
    string Status,
    double? LastLatitude,
    double? LastLongitude,
    double? LastAltitude,
    string InvestigationStatus,
    bool Grounded,
    bool HasBrief
);

public record AnomalyEventDetailDto(
    Guid Id,
    string Icao24,
    string? Callsign,
    string? OriginCountry,
    string Squawk,
    string Kind,
    DateTime FirstSeenAt,
    DateTime ConfirmedAt,
    DateTime LastSeenAt,
    DateTime? ClosedAt,
    string Status,
    double? FirstLatitude,
    double? FirstLongitude,
    double? FirstAltitude,
    double? LastLatitude,
    double? LastLongitude,
    double? LastAltitude,
    JsonElement? Evidence,
    JsonElement? Investigation,
    string InvestigationStatus,
    bool Grounded,
    string? PromptVersion,
    string? Provider,
    string? Model,
    int? InputTokens,
    int? OutputTokens,
    int? LatencyMs
);

/// <summary>What the detector freezes into EvidenceJson. Two snapshots, because the
/// trail at close can differ from (or be gone since) the trail at confirm.</summary>
public record AnomalyEvidence(
    EvidenceSnapshot? AtConfirm,
    EvidenceSnapshot? AtClose
);

public record EvidenceSnapshot(
    DateTime SnapshotAt,
    int SourcePointCount,
    IReadOnlyList<TrackPointDto> TrackPoints
);
