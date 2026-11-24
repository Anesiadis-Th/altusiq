using System.Text.Json;
using AltusIQ.Api.Investigation;
using AltusIQ.Api.Llm;
using AltusIQ.Api.Models;
using AltusIQ.Api.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace AltusIQ.Api.Tests.Services;

public class InvestigationAgentTests
{
    private sealed class Harness
    {
        public FakeLlmClient Llm { get; } = new();
        public FakeToolSource Tools { get; } = new();
        public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero));
        public LlmSettings Settings { get; }
        public LlmCallBudget Budget { get; }
        public InvestigationAgent Agent { get; }

        public Harness(Action<LlmSettings>? configure = null)
        {
            Settings = new LlmSettings
            {
                Enabled = true,
                MaxCallsPerDay = 40,
                MinSecondsBetweenCalls = 0,
                MaxToolSteps = 6,
                Providers =
                [
                    new LlmProviderSettings { Name = "groq", BaseUrl = "https://groq.test", Model = "groq-model", ApiKeyEnv = "X" },
                    new LlmProviderSettings { Name = "openrouter", BaseUrl = "https://openrouter.test", Model = "or-model", ApiKeyEnv = "Y" }
                ]
            };
            configure?.Invoke(Settings);

            Budget = new LlmCallBudget(Options.Create(Settings), Clock);
            Agent = new InvestigationAgent(
                Llm, Options.Create(Settings), new PromptStore(), Budget, Clock,
                NullLogger<InvestigationAgent>.Instance);
        }

        public Task<InvestigationResult> Run(AnomalyEvent? evt = null) =>
            Agent.InvestigateAsync(evt ?? Event(), Tools, CancellationToken.None);
    }

    internal static AnomalyEvent Event(string squawk = "7700", bool sensitive = false) => new()
    {
        Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        Icao24 = "abc123",
        Callsign = "SAS123",
        OriginCountry = "Sweden",
        Squawk = squawk,
        Kind = sensitive ? "unlawful_interference" : "general_emergency",
        Sensitive = sensitive,
        FirstSeenAt = new DateTime(2026, 8, 1, 11, 50, 0, DateTimeKind.Utc),
        ConfirmedAt = new DateTime(2026, 8, 1, 11, 52, 0, DateTimeKind.Utc),
        LastSeenAt = new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc),
        ClosedAt = new DateTime(2026, 8, 1, 12, 2, 0, DateTimeKind.Utc),
        Status = AnomalyEventStatus.Closed,
        LastLongitude = 17.9,
        LastLatitude = 59.6,
        LastAltitude = 3000
    };

    // ---- the happy path ----

    [Fact]
    public async Task Valid_output_is_stored_with_prompt_version_provider_model_tokens_and_latency()
    {
        var h = new Harness();
        h.Llm.ThenToolCall(ToolNames.NearestAirports, """{"lat":59.6,"lon":17.9,"limit":2}""")
             .ThenBrief(FakeLlmClient.ValidBrief);

        var result = await h.Run();

        Assert.Equal(InvestigationStatus.Completed, result.Status);
        Assert.Equal("investigate.v1", result.PromptVersion);
        Assert.Equal("groq", result.Provider);
        Assert.Equal("groq-model", result.Model);
        Assert.Equal(50 + 120, result.InputTokens);
        Assert.Equal(20 + 80, result.OutputTokens);
        Assert.True(result.LatencyMs >= 0);
        Assert.Equal(2, result.ModelCalls);
        Assert.Equal([ToolNames.NearestAirports], result.ToolsCalled);
        Assert.True(result.Grounded, string.Join(",", result.UnsupportedTokens));
        Assert.NotNull(result.Brief);
        Assert.Equal("fair", result.Brief!.EvidenceQuality);

        // The stored JSON is the brief itself, the shape the UI reads.
        var stored = JsonDocument.Parse(result.ToStoredJson()!).RootElement;
        Assert.Equal("fair", stored.GetProperty("evidence_quality").GetString());
    }

    [Fact]
    public async Task The_first_request_carries_the_system_prompt_the_event_record_and_the_five_tools()
    {
        var h = new Harness();
        await h.Run();

        var first = h.Llm.Requests[0];
        Assert.Equal("system", first.Messages[0].Role);
        Assert.Contains("120 seconds", first.Messages[0].Content);
        Assert.Contains("\"squawk\":\"7700\"", first.Messages[1].Content);
        Assert.Equal(ToolNames.All, first.Tools!.Select(t => t.Name).ToList());
    }

    // ---- invalid output ----

    [Fact]
    public async Task Invalid_JSON_is_retried_once_with_the_error_then_stored_as_invalid_output()
    {
        var h = new Harness(s => s.Providers.RemoveAt(1));
        h.Llm.ThenText("Sure! Here is my analysis: it was probably a medical case.")
             .ThenText("""{"summary":"x","observations":[],"explanations":[],"unknowns":[],"evidence_quality":"excellent","recommended_follow_up":"y"}""");

        var result = await h.Run();

        Assert.Equal(InvestigationStatus.InvalidOutput, result.Status);
        Assert.Equal(2, h.Llm.Requests.Count);
        Assert.Contains("rejected", h.Llm.Requests[1].Messages[^1].Content);
        Assert.Contains("no JSON object", h.Llm.Requests[1].Messages[^1].Content);
        Assert.Contains("evidence_quality", result.Error);
        Assert.Null(result.Brief);
        Assert.Contains("\"raw\"", result.ToStoredJson());
    }

    [Fact]
    public async Task Invalid_output_after_the_retry_moves_to_the_next_provider()
    {
        var h = new Harness();
        h.Llm.ThenText("not json").ThenText("still not json").ThenBrief(FakeLlmClient.ValidBrief);

        var result = await h.Run();

        Assert.Equal(InvestigationStatus.Completed, result.Status);
        Assert.Equal("openrouter", result.Provider);
    }

    // ---- provider fallback ----

    [Fact]
    public async Task A_429_is_retried_once_then_moves_to_the_next_provider()
    {
        var h = new Harness();
        h.Llm.ThenThrow(LlmFailureKind.RateLimited, 429)
             .ThenThrow(LlmFailureKind.RateLimited, 429)
             .ThenBrief(FakeLlmClient.ValidBrief);

        var result = await h.Run();

        Assert.Equal(InvestigationStatus.Completed, result.Status);
        Assert.Equal("openrouter", result.Provider);
        Assert.Equal(["groq", "groq", "openrouter"], h.Llm.Requests.Select(r => r.Provider.Name));
        // ModelCalls counts every call across providers, failed ones included.
        Assert.Equal(3, result.ModelCalls);
    }

    [Theory]
    [InlineData(LlmFailureKind.ServerError)]
    [InlineData(LlmFailureKind.Timeout)]
    public async Task A_5xx_or_timeout_falls_through_the_same_way(LlmFailureKind kind)
    {
        var h = new Harness();
        h.Llm.ThenThrow(kind).ThenThrow(kind);

        var result = await h.Run();

        Assert.Equal("openrouter", result.Provider);
    }

    [Fact]
    public async Task All_providers_failing_yields_uninvestigated_with_the_deterministic_brief()
    {
        var h = new Harness();
        h.Llm.Default = _ => throw new LlmException(LlmFailureKind.ServerError, "down", 503);

        var result = await h.Run();

        Assert.Equal(InvestigationStatus.Uninvestigated, result.Status);
        Assert.Null(result.Provider);
        Assert.Equal("no provider produced a brief", result.Error);
        // Each provider was tried twice (one retry), and all four calls are counted.
        Assert.Equal(4, result.ModelCalls);
        Assert.NotNull(result.Brief);
        Assert.Contains("no model was consulted", result.Brief!.Summary);
        Assert.Empty(result.Brief.Explanations);
        Assert.Contains(result.Brief.Observations, o => o.Contains("ESSA"));
        Assert.Contains(result.Brief.Observations, o => o.Contains("FG"));
        Assert.True(result.Grounded, string.Join(",", result.UnsupportedTokens));
        // Code gathered every tool before writing.
        Assert.Contains(ToolNames.GetTrail, result.ToolsCalled);
        Assert.Contains(ToolNames.GetMetar, result.ToolsCalled);
        Assert.Contains(ToolNames.GetRoute, result.ToolsCalled);
    }

    [Theory]
    [InlineData(null)]                                                    // adsbdb has no route
    [InlineData("""{"error":"get_route failed: HttpRequestException"}""")] // the route tool threw
    [InlineData("""[1,2,3]""")]                                           // not an object
    public async Task The_deterministic_brief_survives_a_missing_or_malformed_route(string? routeOutput)
    {
        var h = new Harness();
        h.Llm.Default = _ => throw new LlmException(LlmFailureKind.ServerError, "down", 503);
        h.Tools.Outputs[ToolNames.GetRoute] = routeOutput;
        // The error object the tool loop writes when a tool throws; it has no station field.
        h.Tools.Outputs[ToolNames.GetMetar] = """{"error":"get_metar failed: TaskCanceledException"}""";

        var result = await h.Run();

        Assert.Equal(InvestigationStatus.Uninvestigated, result.Status);
        Assert.NotNull(result.Brief);
        Assert.Contains(result.Brief!.Unknowns, u => u.StartsWith("Planned route"));
        Assert.Contains(result.Brief.Unknowns, u => u.Contains("METAR fetch failed"));
        Assert.DoesNotContain(result.Brief.Observations, o => o.Contains("adsbdb"));
    }

    [Fact]
    public async Task An_unconfigured_provider_is_skipped_not_fatal()
    {
        var h = new Harness();
        h.Llm.ThenThrow(LlmFailureKind.NotConfigured).ThenBrief(FakeLlmClient.ValidBrief);

        var result = await h.Run();

        Assert.Equal("openrouter", result.Provider);
    }

    [Fact]
    public async Task Llm_disabled_writes_the_deterministic_brief_without_a_model_call()
    {
        var h = new Harness(s => s.Enabled = false);

        var result = await h.Run();

        Assert.Equal(InvestigationStatus.Uninvestigated, result.Status);
        Assert.Empty(h.Llm.Requests);
        Assert.NotNull(result.Brief);
        Assert.Equal("Llm:Enabled is false", result.Error);
    }

    // ---- single-shot mode ----

    [Fact]
    public async Task A_model_without_tool_calling_gets_every_tool_output_in_one_request()
    {
        var h = new Harness();
        h.Llm.ThenThrow(LlmFailureKind.ToolsUnsupported, 400).ThenBrief(FakeLlmClient.ValidBrief);

        var result = await h.Run();

        Assert.Equal(InvestigationStatus.Completed, result.Status);
        Assert.Equal("groq", result.Provider);
        var single = h.Llm.Requests[1];
        Assert.Null(single.Tools);
        Assert.Contains("### get_metar", single.Messages[1].Content);
        Assert.Contains("ESSA 011220Z", single.Messages[1].Content);
        Assert.Contains("### get_route", single.Messages[1].Content);
    }

    // ---- the tool loop ----

    [Fact]
    public async Task The_loop_stops_at_six_tool_steps_then_asks_for_the_brief_without_tools()
    {
        var h = new Harness();
        h.Llm.Default = req => req.Tools is not null
            ? FakeLlmClient.ToolCalls((ToolNames.GetMetar, """{"icao":"ESSA"}"""))
            : FakeLlmClient.Brief(FakeLlmClient.ValidBrief);

        var result = await h.Run();

        Assert.Equal(InvestigationStatus.Completed, result.Status);
        Assert.Equal(6, result.ToolsCalled.Count);
        Assert.Equal(7, h.Llm.Requests.Count);
        Assert.All(h.Llm.Requests.Take(6), r => Assert.NotNull(r.Tools));
        Assert.Null(h.Llm.Requests[6].Tools);
        Assert.Contains("tool rounds", h.Llm.Requests[6].Messages[^1].Content);
    }

    [Fact]
    public async Task Tool_output_is_truncated_at_the_8_KB_cap()
    {
        var h = new Harness();
        h.Tools.Outputs[ToolNames.GetTrail] = new string('x', 20_000);
        h.Llm.ThenToolCall(ToolNames.GetTrail).ThenBrief(FakeLlmClient.ValidBrief);

        var result = await h.Run();

        var toolMessage = h.Llm.Requests[1].Messages.Single(m => m.Role == "tool");
        Assert.Equal(InvestigationAgent.ToolOutputCapBytes, toolMessage.Content!.Length);
        Assert.EndsWith("[truncated at 8 KB]", toolMessage.Content);
        Assert.Equal(toolMessage.Content, result.ToolOutputs[ToolNames.GetTrail]);
    }

    [Fact]
    public async Task A_tool_that_returns_nothing_tells_the_model_so_instead_of_failing()
    {
        var h = new Harness();
        h.Llm.ThenToolCall(ToolNames.RecentFlights).ThenBrief(FakeLlmClient.ValidBrief);

        await h.Run();

        var toolMessage = h.Llm.Requests[1].Messages.Single(m => m.Role == "tool");
        Assert.Contains("nothing returned", toolMessage.Content);
    }

    [Fact]
    public async Task Get_trail_falls_back_to_the_evidence_snapshot_once_the_live_trail_is_gone()
    {
        var h = new Harness();
        h.Tools.Outputs[ToolNames.GetTrail] = null;
        var evt = Event();
        evt.EvidenceJson = """{"at_confirm":{"snapshot_at":"2026-08-01T11:52:00Z","source_point_count":2,"track_points":[{"timestamp":1754000000,"longitude":17.5,"latitude":59.4,"altitude":5000,"heading":30,"velocity":200},{"timestamp":1754000120,"longitude":17.6,"latitude":59.5,"altitude":4500,"heading":30,"velocity":190}]},"at_close":null}""";
        h.Llm.ThenToolCall(ToolNames.GetTrail).ThenBrief(FakeLlmClient.ValidBrief);

        await h.Run(evt);

        var toolMessage = h.Llm.Requests[1].Messages.Single(m => m.Role == "tool");
        Assert.Contains("\"point_count\":2", toolMessage.Content);
        Assert.Contains("4500", toolMessage.Content);
    }

    // ---- groundedness ----

    [Fact]
    public async Task An_airport_code_absent_from_the_evidence_marks_the_brief_ungrounded_but_still_stores_it()
    {
        var h = new Harness();
        h.Llm.ThenToolCall(ToolNames.NearestAirports)
             .ThenBrief(FakeLlmClient.ValidBrief.Replace("near ESSA", "near ENGM, diverting from ESSA"));

        var result = await h.Run();

        Assert.Equal(InvestigationStatus.Completed, result.Status);
        Assert.False(result.Grounded);
        Assert.Equal(["ENGM"], result.UnsupportedTokens);
        Assert.NotNull(result.Brief);
    }

    [Fact]
    public async Task A_number_absent_from_the_evidence_marks_the_brief_ungrounded()
    {
        var h = new Harness();
        h.Llm.ThenToolCall(ToolNames.NearestAirports)
             .ThenBrief(FakeLlmClient.ValidBrief.Replace("at 3000 m", "at 3000 m, then 12500 m"));

        var result = await h.Run();

        Assert.False(result.Grounded);
        Assert.Equal(["12500"], result.UnsupportedTokens);
    }

    // ---- budget ----

    [Fact]
    public async Task The_daily_budget_stops_calls_at_40_and_resumes_the_next_UTC_day()
    {
        var h = new Harness();

        for (var i = 0; i < 40; i++)
            Assert.Equal(InvestigationStatus.Completed, (await h.Run()).Status);

        Assert.Equal(40, h.Llm.Requests.Count);

        var spent = await h.Run();
        Assert.Equal(InvestigationStatus.Uninvestigated, spent.Status);
        Assert.Equal(InvestigationAgent.BudgetSpentReason, spent.Error);
        Assert.Equal(40, h.Llm.Requests.Count);
        Assert.NotNull(spent.Brief);

        // 12:00Z on 1 Aug plus 12 h is 00:00Z on 2 Aug: a fresh budget.
        h.Clock.Advance(TimeSpan.FromHours(12));
        var resumed = await h.Run();
        Assert.Equal(InvestigationStatus.Completed, resumed.Status);
        Assert.Equal(41, h.Llm.Requests.Count);
    }

    [Fact]
    public async Task The_budget_counts_every_model_call_not_every_event()
    {
        var h = new Harness(s => s.MaxCallsPerDay = 3);
        h.Llm.ThenToolCall(ToolNames.GetMetar, """{"icao":"ESSA"}""")
             .ThenToolCall(ToolNames.GetRoute)
             .ThenBrief(FakeLlmClient.ValidBrief);

        Assert.Equal(InvestigationStatus.Completed, (await h.Run()).Status);
        Assert.Equal(InvestigationStatus.Uninvestigated, (await h.Run()).Status);
        Assert.Equal(3, h.Llm.Requests.Count);
    }

    // ---- sensitivity ----

    [Fact]
    public async Task A_sensitive_event_is_skipped_without_any_tool_or_model_call()
    {
        var h = new Harness();

        var result = await h.Run(Event("7500", sensitive: true));

        Assert.Equal(InvestigationStatus.Skipped, result.Status);
        Assert.Empty(h.Llm.Requests);
        Assert.Empty(h.Tools.Calls);
        Assert.Null(result.Brief);
        Assert.Null(result.ToStoredJson());
        Assert.Equal("investigate.v1", result.PromptVersion);
    }
}
