using AltusIQ.Api.Models;
using AltusIQ.Api.Services;

namespace AltusIQ.Api.Tests.Fakes;

internal sealed class FakeAnomalyEventWriter : IAnomalyEventWriter
{
    private readonly Dictionary<Guid, AnomalyEvent> _rows = new();

    /// <summary>What the "database" holds, keyed by id like the real upsert.</summary>
    public IReadOnlyDictionary<Guid, AnomalyEvent> Rows => _rows;
    public int WriteCallCount { get; private set; }

    /// <summary>Set to simulate the database being unavailable.</summary>
    public Exception? FailWith { get; set; }

    public Task UpsertAsync(AnomalyEvent anomalyEvent, CancellationToken ct)
    {
        WriteCallCount++;

        if (FailWith is not null)
            throw FailWith;

        _rows[anomalyEvent.Id] = Clone(anomalyEvent);
        return Task.CompletedTask;
    }

    public AnomalyEvent Single() => Assert.Single(_rows.Values);

    // Snapshot on write, so a later in-memory mutation cannot masquerade as a persisted one.
    private static AnomalyEvent Clone(AnomalyEvent e) => new()
    {
        Id = e.Id,
        Icao24 = e.Icao24,
        Callsign = e.Callsign,
        OriginCountry = e.OriginCountry,
        Squawk = e.Squawk,
        Kind = e.Kind,
        Sensitive = e.Sensitive,
        FirstSeenAt = e.FirstSeenAt,
        ConfirmedAt = e.ConfirmedAt,
        LastSeenAt = e.LastSeenAt,
        ClosedAt = e.ClosedAt,
        Status = e.Status,
        FirstLongitude = e.FirstLongitude,
        FirstLatitude = e.FirstLatitude,
        FirstAltitude = e.FirstAltitude,
        LastLongitude = e.LastLongitude,
        LastLatitude = e.LastLatitude,
        LastAltitude = e.LastAltitude,
        EvidenceJson = e.EvidenceJson,
        InvestigationJson = e.InvestigationJson,
        InvestigationStatus = e.InvestigationStatus,
        Grounded = e.Grounded,
        PromptVersion = e.PromptVersion,
        Provider = e.Provider,
        Model = e.Model,
        InputTokens = e.InputTokens,
        OutputTokens = e.OutputTokens,
        LatencyMs = e.LatencyMs,
        CreatedAt = e.CreatedAt
    };
}
