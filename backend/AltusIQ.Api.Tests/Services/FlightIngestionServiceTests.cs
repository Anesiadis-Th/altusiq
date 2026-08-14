using AltusIQ.Api.Models;
using AltusIQ.Api.Models.Dtos;
using AltusIQ.Api.Services;
using AltusIQ.Api.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace AltusIQ.Api.Tests.Services;

public class FlightIngestionServiceTests
{
    private const string Icao = "abc123";

    // The configured box is lon 4-32, lat 54-72. These two latitudes sit well
    // inside and well outside it, so no test depends on edge precision.
    private const double InRegionLat = 60;
    private const double OutOfRegionLat = 45;

    // Settings values are repeated in the assertions on purpose: changing one in
    // appsettings should fail the suite and force a deliberate decision.
    private sealed class Harness
    {
        public FakeFlightWriter Writer { get; } = new();
        public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero));
        public IngestionSettings Settings { get; }
        public FlightIngestionService Service { get; }

        public Harness(Action<IngestionSettings>? configure = null)
        {
            Settings = new IngestionSettings
            {
                MinLon = 4,
                MaxLon = 32,
                MinLat = 54,
                MaxLat = 72,
                PollIntervalSeconds = 120,
                GapThresholdSeconds = 360,
                MinPointIntervalSeconds = 30,
                OutOfRegionPointIntervalSeconds = 300,
                MaxTrackPoints = 300
            };

            configure?.Invoke(Settings);

            Service = new FlightIngestionService(
                Writer,
                Options.Create(Settings),
                NullLogger<FlightIngestionService>.Instance,
                Clock);
        }

        public Task Poll(params Aircraft[] aircraft) =>
            Service.ProcessAsync(aircraft, CancellationToken.None);

        public async Task PollAfter(int seconds, params Aircraft[] aircraft)
        {
            Clock.Advance(TimeSpan.FromSeconds(seconds));
            await Poll(aircraft);
        }

        public Task<FlightTrackDto?> ActiveTrack(string icao = Icao) =>
            Service.GetActiveTrackAsync(icao, CancellationToken.None);

        /// <summary>Idle past the gap threshold so any open flight closes.</summary>
        public Task Idle() => PollAfter(Settings.GapThresholdSeconds + 1);
    }

    private static Aircraft Plane(
        double lat = InRegionLat,
        double? altitude = 10_000,
        bool onGround = false,
        string icao = Icao) => new()
        {
            Icao24 = icao,
            Callsign = "SAS123",
            OriginCountry = "Sweden",
            Longitude = 10,
            Latitude = lat,
            BarometricAltitude = altitude,
            OnGround = onGround,
            Velocity = 220,
            Heading = 180,
            VerticalRate = 0
        };

    // ---- gap boundary: when one flight closes and the next opens ----

    [Fact]
    public async Task Flight_stays_open_at_exactly_the_gap_threshold()
    {
        var h = new Harness();
        await h.Poll(Plane());
        await h.PollAfter(30, Plane());

        await h.PollAfter(h.Settings.GapThresholdSeconds);

        Assert.Empty(h.Writer.Persisted);
        Assert.NotNull(await h.ActiveTrack());
    }

    [Fact]
    public async Task Flight_closes_one_second_past_the_gap_threshold()
    {
        var h = new Harness();
        await h.Poll(Plane());
        await h.PollAfter(30, Plane());

        await h.PollAfter(h.Settings.GapThresholdSeconds + 1);

        Assert.Single(h.Writer.Persisted);
        Assert.Null(await h.ActiveTrack());
    }

    [Fact]
    public async Task Two_consecutive_missed_polls_do_not_close_a_flight()
    {
        // 120s polling against a 360s threshold. If the threshold ever drops back
        // toward the poll interval, one absent poll fragments a real flight.
        var h = new Harness();
        await h.Poll(Plane());
        await h.PollAfter(30, Plane());

        await h.PollAfter(120);
        await h.PollAfter(120);

        Assert.Empty(h.Writer.Persisted);
        Assert.NotNull(await h.ActiveTrack());
    }

    [Fact]
    public async Task A_continuously_reported_aircraft_is_never_treated_as_stale()
    {
        // LastSeen tracks reports, LastRecordedAt tracks recorded fixes, and they
        // are deliberately separate clocks. If the throttled path stops refreshing
        // LastSeen, a plane still arriving in every poll gets closed and reopened,
        // splitting one real flight across several rows. The interval is pushed
        // past the gap threshold here so only the throttled path can keep it open.
        var h = new Harness(s =>
        {
            s.MinPointIntervalSeconds = 400;
            s.GapThresholdSeconds = 360;
        });

        await h.Poll(Plane());
        var opened = await h.ActiveTrack();

        for (var i = 0; i < 5; i++)
            await h.PollAfter(120, Plane());

        var current = await h.ActiveTrack();

        Assert.NotNull(current);
        Assert.Equal(opened!.Id, current!.Id);
        Assert.Empty(h.Writer.Persisted);
    }

    [Fact]
    public async Task Aircraft_leaving_and_re_entering_the_region_stays_one_flight()
    {
        var h = new Harness();
        await h.Poll(Plane());
        await h.PollAfter(30, Plane());
        await h.PollAfter(30, Plane(lat: OutOfRegionLat));
        await h.PollAfter(30, Plane());

        await h.Idle();

        var flight = h.Writer.Single();
        Assert.Equal(4, flight.TrackPoints.Count);
    }

    // ---- eligibility: which flights are worth persisting ----

    [Fact]
    public async Task Flight_with_two_in_region_fixes_is_persisted()
    {
        var h = new Harness();
        await h.Poll(Plane());
        await h.PollAfter(30, Plane());

        await h.Idle();

        Assert.Single(h.Writer.Persisted);
    }

    [Fact]
    public async Task Flight_with_a_single_in_region_fix_is_dropped()
    {
        var h = new Harness();
        await h.Poll(Plane());
        await h.PollAfter(30, Plane(lat: OutOfRegionLat));
        await h.PollAfter(30, Plane(lat: OutOfRegionLat));

        await h.Idle();

        Assert.Empty(h.Writer.Persisted);
        Assert.Null(await h.ActiveTrack());
    }

    [Fact]
    public async Task Overflight_that_never_enters_the_region_is_never_persisted()
    {
        // The global poll sees ~10k aircraft. Persisting them all is ~100x the
        // volume and blows the Supabase free tier.
        var h = new Harness();
        await h.Poll(Plane(lat: OutOfRegionLat));
        await h.PollAfter(30, Plane(lat: OutOfRegionLat));
        await h.PollAfter(30, Plane(lat: OutOfRegionLat));

        await h.Idle();

        Assert.Empty(h.Writer.Persisted);
    }

    [Fact]
    public async Task Region_fix_count_survives_the_MaxTrackPoints_trim()
    {
        // Long-haul departure: the Scandinavian leg is trimmed out of the track
        // entirely, but the flight must still qualify. If the counter is ever
        // recomputed from TrackPoints instead of accumulated, this goes empty.
        var h = new Harness(s => s.MaxTrackPoints = 4);

        await h.Poll(Plane());
        await h.PollAfter(30, Plane());

        for (var i = 0; i < 6; i++)
            await h.PollAfter(30, Plane(lat: OutOfRegionLat));

        await h.Idle();

        var flight = h.Writer.Single();
        Assert.All(flight.TrackPoints, p => Assert.Equal(OutOfRegionLat, p.Latitude));
    }

    // ---- what gets written ----

    [Fact]
    public async Task OpenedAt_and_ClosedAt_come_from_the_track_not_the_timeout()
    {
        // ClosedAt must be the last fix, not the moment the gap expired, or every
        // duration in the history panel is inflated by the gap threshold.
        var h = new Harness();
        await h.Poll(Plane());
        await h.PollAfter(30, Plane());
        await h.PollAfter(30, Plane());

        await h.Idle();

        var flight = h.Writer.Single();
        var first = flight.TrackPoints[0].Timestamp;
        var last = flight.TrackPoints[^1].Timestamp;

        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(first).UtcDateTime, flight.OpenedAt);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(last).UtcDateTime, flight.ClosedAt);
        Assert.Equal(60, last - first);
    }

    [Fact]
    public async Task MaxAltitude_records_the_peak_and_LastAltitude_the_final_fix()
    {
        var h = new Harness();
        await h.Poll(Plane(altitude: 3_000));
        await h.PollAfter(30, Plane(altitude: 11_000));
        await h.PollAfter(30, Plane(altitude: 400));

        await h.Idle();

        var flight = h.Writer.Single();
        Assert.Equal(11_000, flight.MaxAltitude);
        Assert.Equal(400, flight.LastAltitude);
    }

    [Fact]
    public async Task LastPosition_is_the_final_fix()
    {
        var h = new Harness();
        await h.Poll(Plane());
        await h.PollAfter(30, Plane(lat: 65));

        await h.Idle();

        var flight = h.Writer.Single();
        Assert.NotNull(flight.LastPosition);
        Assert.Equal(65, flight.LastPosition!.Y);
        Assert.Equal(10, flight.LastPosition.X);
        Assert.Equal(4326, flight.LastPosition.SRID);
    }

    // ---- capture rules ----

    [Fact]
    public async Task Fixes_closer_together_than_MinPointIntervalSeconds_are_not_recorded()
    {
        var h = new Harness();
        await h.Poll(Plane());
        await h.PollAfter(10, Plane());
        await h.PollAfter(10, Plane());

        var track = await h.ActiveTrack();

        Assert.NotNull(track);
        Assert.Single(track!.TrackPoints);
    }

    [Fact]
    public async Task Aircraft_on_the_ground_are_not_tracked()
    {
        var h = new Harness();
        await h.Poll(Plane(onGround: true));
        await h.PollAfter(30, Plane(onGround: true));

        Assert.Null(await h.ActiveTrack());

        await h.Idle();
        Assert.Empty(h.Writer.Persisted);
    }

    [Fact]
    public async Task Track_capture_is_capped_at_MaxTrackPoints_dropping_the_oldest()
    {
        var h = new Harness(s => s.MaxTrackPoints = 3);

        await h.Poll(Plane(altitude: 1));
        await h.PollAfter(30, Plane(altitude: 2));
        await h.PollAfter(30, Plane(altitude: 3));
        await h.PollAfter(30, Plane(altitude: 4));

        var track = await h.ActiveTrack();

        Assert.NotNull(track);
        Assert.Equal(
            new double?[] { 2, 3, 4 },
            track!.TrackPoints.Select(p => p.Altitude));
    }

    [Fact]
    public async Task Duplicate_icao24_rows_in_one_poll_do_not_throw()
    {
        var h = new Harness();
        await h.Poll(Plane(), Plane());

        Assert.NotNull(await h.ActiveTrack());
    }

    [Fact]
    public async Task Active_track_lookup_is_case_insensitive()
    {
        var h = new Harness();
        await h.Poll(Plane());

        Assert.NotNull(await h.ActiveTrack("ABC123"));
    }

    // ---- out-of-region thinning ----

    [Fact]
    public async Task In_region_fixes_survive_thinning_at_full_resolution()
    {
        var h = new Harness();

        await h.Poll(Plane());
        for (var i = 0; i < 5; i++)
            await h.PollAfter(30, Plane());

        for (var i = 0; i < 10; i++)
            await h.PollAfter(30, Plane(lat: OutOfRegionLat));

        await h.Idle();

        var flight = h.Writer.Single();
        Assert.Equal(6, flight.TrackPoints.Count(p => p.Latitude == InRegionLat));
    }

    [Fact]
    public async Task Out_of_region_fixes_are_thinned_and_the_boundary_pair_is_kept()
    {
        var h = new Harness();

        await h.Poll(Plane());
        await h.PollAfter(30, Plane());
        await h.PollAfter(30, Plane());

        for (var i = 0; i < 20; i++)
            await h.PollAfter(30, Plane(lat: OutOfRegionLat));

        await h.Idle();

        var flight = h.Writer.Single();
        var offsets = flight.TrackPoints
            .Select(p => p.Timestamp - flight.TrackPoints[0].Timestamp)
            .ToArray();

        // 0/30/60 in region at full resolution; 90 is the first fix past the
        // boundary and is kept so the line does not cut a chord across it; 390 is
        // the next fix a full OutOfRegionPointIntervalSeconds later; 660 is the
        // last fix, always kept. 23 captured, 6 stored.
        Assert.Equal(new long[] { 0, 30, 60, 90, 390, 660 }, offsets);
    }

    // ---- failure handling ----

    [Fact]
    public async Task A_failed_write_keeps_the_flight_in_memory_for_the_next_poll()
    {
        var h = new Harness();
        await h.Poll(Plane());
        await h.PollAfter(30, Plane());

        h.Writer.FailWith = new InvalidOperationException("database unavailable");
        await h.Idle();

        Assert.Empty(h.Writer.Persisted);
        Assert.NotNull(await h.ActiveTrack());

        h.Writer.FailWith = null;
        await h.PollAfter(1);

        Assert.Single(h.Writer.Persisted);
    }
}
