using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AltusIQ.Api.Investigation;
using AltusIQ.Api.Models;
using AltusIQ.Api.Models.Dtos;
using Microsoft.Extensions.Options;

namespace AltusIQ.Api.Services;

/// <summary>
/// Turns emergency transponder codes seen across polls into AnomalyEvents.
/// Runs after ingestion on the poll path, inside its own try/catch there, so it
/// can never fail a poll. State is per icao24, keyed like the ingestion trails.
///
/// Rules (ADR-018):
///  - first poll with 7500/7600/7700: candidate, nothing persisted;
///  - second consecutive poll with the same code: confirmed, event opened;
///  - the squawk absent for one poll, or the aircraft unseen longer than
///    Ingestion:GapThresholdSeconds, closes the event;
///  - the event id is derived from icao24 + confirmed-at unix seconds, so a
///    retried write lands on the same row.
/// </summary>
public sealed class EmergencySquawkDetector
{
    public const string Hijack = "7500";
    public const string RadioFailure = "7600";
    public const string GeneralEmergency = "7700";

    internal static readonly IReadOnlyDictionary<string, (string Kind, bool Sensitive)> EmergencyCodes =
        new Dictionary<string, (string, bool)>
        {
            [Hijack] = ("unlawful_interference", true),
            [RadioFailure] = ("radio_failure", false),
            [GeneralEmergency] = ("general_emergency", false)
        };

    private static readonly JsonSerializerOptions EvidenceJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly IAnomalyEventWriter _writer;
    private readonly IActiveTrackSource _tracks;
    private readonly IngestionSettings _ingestion;
    private readonly EventsSettings _events;
    private readonly TimeProvider _clock;
    private readonly IInvestigationQueue _queue;
    private readonly ILogger<EmergencySquawkDetector> _logger;

    private readonly Dictionary<string, SquawkState> _states = new();
    private readonly List<AnomalyEvent> _pendingClosedWrites = [];
    private readonly HashSet<Guid> _queuedBeforeClose = [];
    private readonly SemaphoreSlim _lock = new(1, 1);

    public EmergencySquawkDetector(
        IAnomalyEventWriter writer,
        IActiveTrackSource tracks,
        IOptions<IngestionSettings> ingestion,
        IOptions<EventsSettings> events,
        TimeProvider clock,
        IInvestigationQueue queue,
        ILogger<EmergencySquawkDetector> logger)
    {
        _writer = writer;
        _tracks = tracks;
        _ingestion = ingestion.Value;
        _events = events.Value;
        _clock = clock;
        _queue = queue;
        _logger = logger;
    }

    /// <summary>Events currently open or awaiting a write. For tests and the health of the detector only.</summary>
    public async Task<IReadOnlyList<AnomalyEvent>> GetOpenEventsAsync(CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            return _states.Values
                .Where(s => s.Event is not null)
                .Select(s => s.Event!)
                .ToList();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task ProcessAsync(IReadOnlyList<Aircraft> aircraft, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var now = _clock.GetUtcNow().UtcDateTime;

            await RetryPendingWritesAsync(ct);

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var squawking = new Dictionary<string, Aircraft>();

            foreach (var plane in aircraft)
            {
                var key = plane.Icao24.ToLowerInvariant();
                seen.Add(key);

                if (plane.Squawk is not null
                    && EmergencyCodes.ContainsKey(plane.Squawk)
                    && !squawking.ContainsKey(key))
                    squawking[key] = plane;
            }

            foreach (var (key, state) in _states.ToList())
            {
                if (squawking.TryGetValue(key, out var plane) && plane.Squawk == state.Code)
                {
                    state.LastSeenAt = now;
                    state.LastFix = plane;

                    if (state.Event is null)
                        await ConfirmAsync(state, now, ct);
                    else
                        UpdateLastFix(state.Event, state);

                    EnqueueIfLongOpen(state, now);
                    continue;
                }

                if (seen.Contains(key))
                {
                    // Present, but no longer on this code: one poll is enough.
                    if (state.Event is null)
                        _states.Remove(key);
                    else
                        await CloseAsync(key, state, now, "squawk cleared", ct);

                    continue;
                }

                if (state.Event is null)
                {
                    // A candidate that misses a poll was not "consecutive".
                    _states.Remove(key);
                }
                else if ((now - state.LastSeenAt).TotalSeconds > _ingestion.GapThresholdSeconds)
                {
                    await CloseAsync(key, state, now, "unseen past gap threshold", ct);
                }
                else
                {
                    EnqueueIfLongOpen(state, now);
                }
            }

            foreach (var (key, plane) in squawking)
            {
                if (_states.ContainsKey(key))
                    continue;

                _states[key] = new SquawkState
                {
                    Icao24 = key,
                    Code = plane.Squawk!,
                    FirstSeenAt = now,
                    LastSeenAt = now,
                    FirstFix = plane,
                    LastFix = plane
                };
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task ConfirmAsync(SquawkState state, DateTime now, CancellationToken ct)
    {
        var (kind, sensitive) = EmergencyCodes[state.Code];

        var evt = new AnomalyEvent
        {
            Id = DeterministicId(state.Icao24, now),
            Icao24 = state.Icao24,
            Callsign = state.LastFix.Callsign?.Trim(),
            OriginCountry = state.LastFix.OriginCountry,
            Squawk = state.Code,
            Kind = kind,
            Sensitive = sensitive,
            FirstSeenAt = state.FirstSeenAt,
            ConfirmedAt = now,
            LastSeenAt = now,
            Status = AnomalyEventStatus.Open,
            FirstLongitude = state.FirstFix.Longitude,
            FirstLatitude = state.FirstFix.Latitude,
            FirstAltitude = state.FirstFix.BarometricAltitude,
            CreatedAt = now
        };

        UpdateLastFix(evt, state);

        var snapshot = await SnapshotAsync(state.Icao24, now, ct);
        evt.EvidenceJson = Serialize(new AnomalyEvidence(snapshot, null));

        state.Event = evt;

        _logger.LogWarning(
            "Emergency squawk {Squawk} confirmed for {Icao24} ({Callsign}), event {EventId}",
            evt.Squawk, evt.Icao24, evt.Callsign ?? "no callsign", evt.Id);

        await TryWriteAsync(evt, state, ct);
    }

    private async Task CloseAsync(string key, SquawkState state, DateTime now, string reason, CancellationToken ct)
    {
        var evt = state.Event!;

        // LastSeenAt is the last fix on the code, never the moment the gap expired.
        evt.LastSeenAt = state.LastSeenAt;
        evt.ClosedAt = now;
        evt.Status = AnomalyEventStatus.Closed;
        UpdateLastFix(evt, state);

        var atClose = await SnapshotAsync(key, now, ct);
        var evidence = Deserialize(evt.EvidenceJson);
        evt.EvidenceJson = Serialize(new AnomalyEvidence(evidence?.AtConfirm, atClose ?? evidence?.AtClose));

        _states.Remove(key);

        _logger.LogInformation(
            "Emergency squawk event {EventId} for {Icao24} closed: {Reason}",
            evt.Id, evt.Icao24, reason);

        try
        {
            await _writer.UpsertAsync(evt, ct);
            OnEventClosed(evt, state.InvestigationQueued);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to persist closed event {EventId}, retrying next poll", evt.Id);
            _pendingClosedWrites.Add(evt);
            if (state.InvestigationQueued)
                _queuedBeforeClose.Add(evt.Id);
        }
    }

    private async Task TryWriteAsync(AnomalyEvent evt, SquawkState state, CancellationToken ct)
    {
        try
        {
            await _writer.UpsertAsync(evt, ct);
            state.PendingWrite = false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Keep it in memory; the next poll retries with the same id.
            _logger.LogError(ex, "Failed to persist event {EventId}, retrying next poll", evt.Id);
            state.PendingWrite = true;
        }
    }

    private async Task RetryPendingWritesAsync(CancellationToken ct)
    {
        foreach (var state in _states.Values.Where(s => s.PendingWrite && s.Event is not null))
            await TryWriteAsync(state.Event!, state, ct);

        foreach (var evt in _pendingClosedWrites.ToList())
        {
            try
            {
                await _writer.UpsertAsync(evt, ct);
                _pendingClosedWrites.Remove(evt);
                OnEventClosed(evt, _queuedBeforeClose.Remove(evt.Id));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Retry of closed event {EventId} failed", evt.Id);
            }
        }
    }

    /// <summary>Hands the id to the investigator once the row exists. Nothing on the poll path waits on it.</summary>
    private void OnEventClosed(AnomalyEvent evt, bool alreadyQueued)
    {
        if (!alreadyQueued)
            _queue.Enqueue(evt.Id);
    }

    /// <summary>An event still open after Events:InvestigateOpenAfterMinutes is investigated as it stands.</summary>
    private void EnqueueIfLongOpen(SquawkState state, DateTime now)
    {
        if (state.Event is null || state.InvestigationQueued || state.PendingWrite)
            return;

        if ((now - state.Event.ConfirmedAt).TotalMinutes < _events.InvestigateOpenAfterMinutes)
            return;

        state.InvestigationQueued = true;
        _queue.Enqueue(state.Event.Id);
    }

    private static void UpdateLastFix(AnomalyEvent evt, SquawkState state)
    {
        evt.LastSeenAt = state.LastSeenAt;
        evt.LastLongitude = state.LastFix.Longitude;
        evt.LastLatitude = state.LastFix.Latitude;
        evt.LastAltitude = state.LastFix.BarometricAltitude;
        evt.Callsign ??= state.LastFix.Callsign?.Trim();
    }

    private async Task<EvidenceSnapshot?> SnapshotAsync(string icao24, DateTime now, CancellationToken ct)
    {
        FlightTrackDto? track;
        try
        {
            track = await _tracks.GetActiveTrackAsync(icao24, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not snapshot trail for {Icao24}", icao24);
            return null;
        }

        if (track is null)
            return null;

        return new EvidenceSnapshot(
            now,
            track.TrackPoints.Count,
            Thin(track.TrackPoints, _events.MaxEvidencePoints));
    }

    /// <summary>Evenly spaced subset keeping the first and last fix.</summary>
    internal static IReadOnlyList<TrackPointDto> Thin(IReadOnlyList<TrackPointDto> points, int max)
    {
        if (max < 2 || points.Count <= max)
            return points.ToList();

        var kept = new List<TrackPointDto>(max);
        var step = (points.Count - 1) / (double)(max - 1);

        for (var i = 0; i < max; i++)
            kept.Add(points[(int)Math.Round(i * step)]);

        return kept;
    }

    /// <summary>Same icao24 and the same confirm second always give the same id.</summary>
    internal static Guid DeterministicId(string icao24, DateTime confirmedAtUtc)
    {
        var seconds = new DateTimeOffset(confirmedAtUtc, TimeSpan.Zero).ToUnixTimeSeconds();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"anomaly:{icao24.ToLowerInvariant()}:{seconds}"));
        return new Guid(hash.AsSpan(0, 16));
    }

    private static string Serialize(AnomalyEvidence evidence) =>
        JsonSerializer.Serialize(evidence, EvidenceJsonOptions);

    private static AnomalyEvidence? Deserialize(string? json) =>
        json is null ? null : JsonSerializer.Deserialize<AnomalyEvidence>(json, EvidenceJsonOptions);

    private sealed class SquawkState
    {
        public required string Icao24 { get; init; }
        public required string Code { get; init; }
        public DateTime FirstSeenAt { get; init; }
        public DateTime LastSeenAt { get; set; }
        public required Aircraft FirstFix { get; init; }
        public required Aircraft LastFix { get; set; }
        public AnomalyEvent? Event { get; set; }
        public bool PendingWrite { get; set; }
        public bool InvestigationQueued { get; set; }
    }
}
