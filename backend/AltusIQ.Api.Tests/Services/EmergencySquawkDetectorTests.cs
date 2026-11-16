using System.Text.Json;
using AltusIQ.Api.Investigation;
using AltusIQ.Api.Models;
using AltusIQ.Api.Services;
using AltusIQ.Api.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace AltusIQ.Api.Tests.Services;

public class EmergencySquawkDetectorTests
{
    private const string Icao = "abc123";

    private sealed class Harness
    {
        public FakeAnomalyEventWriter Writer { get; } = new();
        public FakeActiveTrackSource Tracks { get; } = new();
        public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero));
        public IngestionSettings Ingestion { get; } = new() { PollIntervalSeconds = 120, GapThresholdSeconds = 360 };
        public EventsSettings Events { get; } = new() { MaxEvidencePoints = 120, InvestigateOpenAfterMinutes = 30 };
        public InvestigationQueue Queue { get; } = new();
        public EmergencySquawkDetector Detector { get; }

        public Harness()
        {
            Detector = new EmergencySquawkDetector(
                Writer,
                Tracks,
                Options.Create(Ingestion),
                Options.Create(Events),
                Clock,
                Queue,
                NullLogger<EmergencySquawkDetector>.Instance);
        }

        public Task Poll(params Aircraft[] aircraft) =>
            Detector.ProcessAsync(aircraft, CancellationToken.None);

        public async Task PollAfter(int seconds, params Aircraft[] aircraft)
        {
            Clock.Advance(TimeSpan.FromSeconds(seconds));
            await Poll(aircraft);
        }

        public Task<IReadOnlyList<AnomalyEvent>> Open() =>
            Detector.GetOpenEventsAsync(CancellationToken.None);

        public List<Guid> Queued()
        {
            var ids = new List<Guid>();
            while (Queue.Reader.TryRead(out var id)) ids.Add(id);
            return ids;
        }
    }

    private static Aircraft Plane(string? squawk, string icao = Icao, double lat = 60, double? altitude = 10_000) => new()
    {
        Icao24 = icao,
        Callsign = "SAS123",
        OriginCountry = "Sweden",
        Longitude = 10,
        Latitude = lat,
        BarometricAltitude = altitude,
        Velocity = 220,
        Heading = 180,
        Squawk = squawk
    };

    // ---- the two-poll rule ----

    [Fact]
    public async Task One_poll_on_an_emergency_code_is_a_candidate_not_an_event()
    {
        // Pilots dial through codes; a single poll on 7700 is not an episode.
        var h = new Harness();
        await h.Poll(Plane("7700"));

        Assert.Empty(h.Writer.Rows);
        Assert.Empty(await h.Open());
    }

    [Fact]
    public async Task Two_consecutive_polls_on_the_same_code_confirm_an_event()
    {
        var h = new Harness();
        await h.Poll(Plane("7700"));
        await h.PollAfter(120, Plane("7700"));

        var evt = h.Writer.Single();
        Assert.Equal("7700", evt.Squawk);
        Assert.Equal("general_emergency", evt.Kind);
        Assert.Equal(AnomalyEventStatus.Open, evt.Status);
        Assert.Equal(h.Clock.GetUtcNow().UtcDateTime, evt.ConfirmedAt);
        Assert.Equal(evt.ConfirmedAt.AddSeconds(-120), evt.FirstSeenAt);
        Assert.Equal(Icao, evt.Icao24);
        Assert.Equal("SAS123", evt.Callsign);
    }

    [Fact]
    public async Task A_candidate_that_changes_code_between_polls_is_not_confirmed()
    {
        var h = new Harness();
        await h.Poll(Plane("7600"));
        await h.PollAfter(120, Plane("7700"));

        Assert.Empty(h.Writer.Rows);
    }

    [Theory]
    [InlineData("1200")]
    [InlineData("2000")]
    [InlineData("7000")]
    [InlineData(null)]
    public async Task Ordinary_codes_never_flag(string? squawk)
    {
        var h = new Harness();
        await h.Poll(Plane(squawk));
        await h.PollAfter(120, Plane(squawk));
        await h.PollAfter(120, Plane(squawk));

        Assert.Empty(h.Writer.Rows);
        Assert.Empty(await h.Open());
    }

    // ---- closing ----

    [Fact]
    public async Task Squawk_clearing_closes_the_event_with_LastSeenAt_from_the_last_fix()
    {
        var h = new Harness();
        await h.Poll(Plane("7700"));
        await h.PollAfter(120, Plane("7700"));
        await h.PollAfter(120, Plane("7700", lat: 61));
        var lastFixAt = h.Clock.GetUtcNow().UtcDateTime;

        await h.PollAfter(120, Plane("1200", lat: 62));

        var evt = h.Writer.Single();
        Assert.Equal(AnomalyEventStatus.Closed, evt.Status);
        Assert.Equal(lastFixAt, evt.LastSeenAt);
        Assert.Equal(h.Clock.GetUtcNow().UtcDateTime, evt.ClosedAt);
        // Last position is the last fix on the code, not the fix that cleared it.
        Assert.Equal(61, evt.LastLatitude);
        Assert.Empty(await h.Open());
    }

    [Fact]
    public async Task Unseen_past_the_gap_threshold_closes_the_event()
    {
        var h = new Harness();
        await h.Poll(Plane("7700"));
        await h.PollAfter(120, Plane("7700"));
        var lastFixAt = h.Clock.GetUtcNow().UtcDateTime;

        // Absent, but inside the gap: still open.
        await h.PollAfter(h.Ingestion.GapThresholdSeconds);
        Assert.Equal(AnomalyEventStatus.Open, h.Writer.Single().Status);
        Assert.Single(await h.Open());

        await h.PollAfter(1);
        var evt = h.Writer.Single();
        Assert.Equal(AnomalyEventStatus.Closed, evt.Status);
        Assert.Equal(lastFixAt, evt.LastSeenAt);
        Assert.Empty(await h.Open());
    }

    [Fact]
    public async Task A_new_squawk_after_close_opens_a_new_event()
    {
        var h = new Harness();
        await h.Poll(Plane("7700"));
        await h.PollAfter(120, Plane("7700"));
        await h.PollAfter(120, Plane("1200"));

        await h.PollAfter(120, Plane("7700"));
        await h.PollAfter(120, Plane("7700"));

        Assert.Equal(2, h.Writer.Rows.Count);
        Assert.Single(h.Writer.Rows.Values, e => e.Status == AnomalyEventStatus.Closed);
        Assert.Single(h.Writer.Rows.Values, e => e.Status == AnomalyEventStatus.Open);
    }

    // ---- identity and persistence ----

    [Fact]
    public async Task The_id_is_identical_when_the_same_confirm_is_replayed()
    {
        var a = new Harness();
        await a.Poll(Plane("7700"));
        await a.PollAfter(120, Plane("7700"));

        var b = new Harness();
        await b.Poll(Plane("7700"));
        await b.PollAfter(120, Plane("7700"));

        Assert.Equal(a.Writer.Single().Id, b.Writer.Single().Id);
        Assert.Equal(
            EmergencySquawkDetector.DeterministicId(Icao, a.Writer.Single().ConfirmedAt),
            a.Writer.Single().Id);
    }

    [Fact]
    public void Different_confirm_seconds_or_aircraft_give_different_ids()
    {
        var at = new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);

        Assert.NotEqual(
            EmergencySquawkDetector.DeterministicId("abc123", at),
            EmergencySquawkDetector.DeterministicId("abc123", at.AddSeconds(1)));
        Assert.NotEqual(
            EmergencySquawkDetector.DeterministicId("abc123", at),
            EmergencySquawkDetector.DeterministicId("abc124", at));
        // Case must not matter: trails key on the lower-cased icao24.
        Assert.Equal(
            EmergencySquawkDetector.DeterministicId("ABC123", at),
            EmergencySquawkDetector.DeterministicId("abc123", at));
    }

    [Fact]
    public async Task A_failed_write_keeps_the_event_in_memory_and_retries_next_poll()
    {
        var h = new Harness();
        await h.Poll(Plane("7700"));

        h.Writer.FailWith = new InvalidOperationException("database unavailable");
        await h.PollAfter(120, Plane("7700"));

        Assert.Empty(h.Writer.Rows);
        var inMemory = Assert.Single(await h.Open());

        h.Writer.FailWith = null;
        await h.PollAfter(120, Plane("7700"));

        Assert.Equal(inMemory.Id, h.Writer.Single().Id);
    }

    [Fact]
    public async Task A_failed_close_write_is_retried_on_the_next_poll()
    {
        var h = new Harness();
        await h.Poll(Plane("7700"));
        await h.PollAfter(120, Plane("7700"));

        h.Writer.FailWith = new InvalidOperationException("database unavailable");
        await h.PollAfter(120, Plane("1200"));
        Assert.Equal(AnomalyEventStatus.Open, h.Writer.Single().Status);

        h.Writer.FailWith = null;
        await h.PollAfter(120, Plane("1200"));
        Assert.Equal(AnomalyEventStatus.Closed, h.Writer.Single().Status);
    }

    // ---- evidence ----

    [Fact]
    public async Task Evidence_snapshot_contains_the_trail_at_confirm_and_at_close()
    {
        var h = new Harness();
        h.Tracks.Set(Icao, FakeActiveTrackSource.Track(Icao, points: 5));

        await h.Poll(Plane("7700"));
        await h.PollAfter(120, Plane("7700"));

        var confirmed = ParseEvidence(h.Writer.Single());
        Assert.Equal(5, confirmed.GetProperty("at_confirm").GetProperty("track_points").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, confirmed.GetProperty("at_close").ValueKind);

        h.Tracks.Set(Icao, FakeActiveTrackSource.Track(Icao, points: 7));
        await h.PollAfter(120, Plane("1200"));

        var closed = ParseEvidence(h.Writer.Single());
        Assert.Equal(5, closed.GetProperty("at_confirm").GetProperty("track_points").GetArrayLength());
        Assert.Equal(7, closed.GetProperty("at_close").GetProperty("track_points").GetArrayLength());
    }

    [Fact]
    public async Task Evidence_is_thinned_to_at_most_120_points_keeping_both_ends()
    {
        var h = new Harness();
        h.Tracks.Set(Icao, FakeActiveTrackSource.Track(Icao, points: 300));

        await h.Poll(Plane("7700"));
        await h.PollAfter(120, Plane("7700"));

        var snapshot = ParseEvidence(h.Writer.Single()).GetProperty("at_confirm");
        var points = snapshot.GetProperty("track_points");

        Assert.Equal(300, snapshot.GetProperty("source_point_count").GetInt32());
        Assert.Equal(120, points.GetArrayLength());
        Assert.Equal(1_754_000_000L, points[0].GetProperty("timestamp").GetInt64());
        Assert.Equal(1_754_000_000L + 299 * 120, points[119].GetProperty("timestamp").GetInt64());
    }

    [Fact]
    public async Task A_missing_trail_still_produces_an_event()
    {
        // Global events often have no ingestion trail worth the name; the event
        // record itself is still evidence.
        var h = new Harness();
        await h.Poll(Plane("7700"));
        await h.PollAfter(120, Plane("7700"));

        var evidence = ParseEvidence(h.Writer.Single());
        Assert.Equal(JsonValueKind.Null, evidence.GetProperty("at_confirm").ValueKind);
    }

    // ---- sensitivity ----

    [Fact]
    public async Task Code_7500_is_marked_sensitive_and_the_others_are_not()
    {
        var h = new Harness();
        await h.Poll(Plane("7500", icao: "aaa111"), Plane("7600", icao: "bbb222"), Plane("7700", icao: "ccc333"));
        await h.PollAfter(120, Plane("7500", icao: "aaa111"), Plane("7600", icao: "bbb222"), Plane("7700", icao: "ccc333"));

        Assert.Equal(3, h.Writer.Rows.Count);
        Assert.True(h.Writer.Rows.Values.Single(e => e.Squawk == "7500").Sensitive);
        Assert.Equal("unlawful_interference", h.Writer.Rows.Values.Single(e => e.Squawk == "7500").Kind);
        Assert.False(h.Writer.Rows.Values.Single(e => e.Squawk == "7600").Sensitive);
        Assert.False(h.Writer.Rows.Values.Single(e => e.Squawk == "7700").Sensitive);
    }

    // ---- hand-off to the investigator ----

    [Fact]
    public async Task Closing_enqueues_the_event_once_the_row_is_written()
    {
        var h = new Harness();
        await h.Poll(Plane("7700"));
        await h.PollAfter(120, Plane("7700"));
        Assert.Empty(h.Queued());

        await h.PollAfter(120, Plane("1200"));

        Assert.Equal([h.Writer.Single().Id], h.Queued());
    }

    [Fact]
    public async Task An_event_open_for_30_minutes_is_enqueued_while_still_open_and_not_again_at_close()
    {
        var h = new Harness();
        await h.Poll(Plane("7700"));
        await h.PollAfter(120, Plane("7700"));

        for (var i = 0; i < 14; i++)
            await h.PollAfter(120, Plane("7700"));
        Assert.Empty(h.Queued());

        await h.PollAfter(120, Plane("7700"));
        var id = Assert.Single(h.Queued());
        Assert.Equal(AnomalyEventStatus.Open, h.Writer.Rows[id].Status);

        await h.PollAfter(120, Plane("1200"));
        Assert.Empty(h.Queued());
    }

    [Fact]
    public async Task A_close_whose_write_failed_is_enqueued_only_after_the_retry_succeeds()
    {
        var h = new Harness();
        await h.Poll(Plane("7700"));
        await h.PollAfter(120, Plane("7700"));

        h.Writer.FailWith = new InvalidOperationException("database unavailable");
        await h.PollAfter(120, Plane("1200"));
        Assert.Empty(h.Queued());

        h.Writer.FailWith = null;
        await h.PollAfter(120);
        Assert.Single(h.Queued());
    }

    private static JsonElement ParseEvidence(AnomalyEvent evt)
    {
        Assert.NotNull(evt.EvidenceJson);
        return JsonDocument.Parse(evt.EvidenceJson!).RootElement.Clone();
    }
}
