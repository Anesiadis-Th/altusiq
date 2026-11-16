using AltusIQ.Api.Background;
using AltusIQ.Api.Investigation;
using AltusIQ.Api.Models;
using AltusIQ.Api.Services;
using AltusIQ.Api.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace AltusIQ.Api.Tests.Services;

public class InvestigationServiceTests
{
    /// <summary>The whole pipeline minus HTTP and the database: detector, queue, consumer, agent.</summary>
    private sealed class Pipeline
    {
        public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero));
        public FakeAnomalyEventWriter Writer { get; } = new();
        public FakeAnomalyEventStore Store { get; } = new();
        public FakeLlmClient Llm { get; } = new();
        public FakeToolSource Tools { get; } = new();
        public InvestigationQueue Queue { get; } = new();
        public EmergencySquawkDetector Detector { get; }
        public InvestigationService Consumer { get; }

        public Pipeline()
        {
            var llmSettings = new LlmSettings
            {
                MinSecondsBetweenCalls = 0,
                Providers = [new LlmProviderSettings { Name = "groq", BaseUrl = "https://groq.test", Model = "m", ApiKeyEnv = "X" }]
            };

            Detector = new EmergencySquawkDetector(
                Writer,
                new FakeActiveTrackSource(),
                Options.Create(new IngestionSettings { GapThresholdSeconds = 360 }),
                Options.Create(new EventsSettings()),
                Clock,
                Queue,
                NullLogger<EmergencySquawkDetector>.Instance);

            var agent = new InvestigationAgent(
                Llm, Options.Create(llmSettings), new PromptStore(),
                new LlmCallBudget(Options.Create(llmSettings), Clock), Clock,
                NullLogger<InvestigationAgent>.Instance);

            Consumer = new InvestigationService(
                Queue, Store, agent, Tools, NullLogger<InvestigationService>.Instance);
        }

        public async Task PollAfter(int seconds, params Aircraft[] aircraft)
        {
            Clock.Advance(TimeSpan.FromSeconds(seconds));
            await Detector.ProcessAsync(aircraft, CancellationToken.None);
            // The fake writer stands in for the table the consumer reads from.
            foreach (var (id, row) in Writer.Rows)
                Store.Rows[id] = row;
        }

        public async Task<int> DrainAsync()
        {
            var processed = 0;
            while (Queue.Reader.TryRead(out var id))
            {
                await Consumer.ProcessAsync(id, CancellationToken.None);
                processed++;
            }
            return processed;
        }
    }

    private static Aircraft Plane(string? squawk) => new()
    {
        Icao24 = "abc123",
        Callsign = "SAS123",
        Longitude = 17.9,
        Latitude = 59.6,
        BarometricAltitude = 3000,
        Squawk = squawk
    };

    [Fact]
    public async Task The_consumer_runs_independently_of_the_poll()
    {
        var p = new Pipeline();

        // Confirm, hold through several polls, close. The clock moves 10 minutes
        // and nothing on the poll path touches the model.
        await p.PollAfter(0, Plane("7700"));
        await p.PollAfter(120, Plane("7700"));
        await p.PollAfter(120, Plane("7700"));
        await p.PollAfter(120, Plane("7700"));
        await p.PollAfter(120, Plane("1200"));

        Assert.Empty(p.Llm.Requests);
        Assert.Empty(p.Tools.Calls);
        Assert.Equal(InvestigationStatus.Pending, p.Writer.Single().InvestigationStatus);

        // Only when the consumer gets its turn does anything leave the process.
        Assert.Equal(1, await p.DrainAsync());

        Assert.NotEmpty(p.Llm.Requests);
        var (id, result) = Assert.Single(p.Store.Saved);
        Assert.Equal(p.Writer.Single().Id, id);
        Assert.Equal(InvestigationStatus.Completed, result.Status);
        Assert.Equal(InvestigationStatus.Completed, p.Store.Rows[id].InvestigationStatus);
        Assert.Equal("investigate.v1", p.Store.Rows[id].PromptVersion);
        Assert.NotNull(p.Store.Rows[id].InvestigationJson);
    }

    [Fact]
    public async Task A_sensitive_event_reaches_the_consumer_and_is_marked_skipped()
    {
        var p = new Pipeline();
        await p.PollAfter(0, Plane("7500"));
        await p.PollAfter(120, Plane("7500"));
        await p.PollAfter(120, Plane("1200"));

        await p.DrainAsync();

        var row = p.Store.Rows.Values.Single();
        Assert.Equal(InvestigationStatus.Skipped, row.InvestigationStatus);
        Assert.Null(row.InvestigationJson);
        Assert.Empty(p.Llm.Requests);
    }

    [Fact]
    public async Task An_event_already_investigated_is_not_investigated_again()
    {
        var p = new Pipeline();
        await p.PollAfter(0, Plane("7700"));
        await p.PollAfter(120, Plane("7700"));
        await p.PollAfter(120, Plane("1200"));
        await p.DrainAsync();

        var id = p.Store.Rows.Keys.Single();
        p.Queue.Enqueue(id);
        await p.DrainAsync();

        Assert.Single(p.Store.Saved);
    }

    [Fact]
    public async Task An_unknown_id_is_logged_and_skipped()
    {
        var p = new Pipeline();
        p.Queue.Enqueue(Guid.NewGuid());

        Assert.Equal(1, await p.DrainAsync());
        Assert.Empty(p.Store.Saved);
    }
}
