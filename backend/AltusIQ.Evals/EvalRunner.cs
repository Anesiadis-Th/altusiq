using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using AltusIQ.Api.Investigation;
using AltusIQ.Api.Llm;
using AltusIQ.Api.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AltusIQ.Evals;

public sealed record CaseResult(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("schema_valid")] bool SchemaValid,
    [property: JsonPropertyName("grounded")] bool Grounded,
    [property: JsonPropertyName("unsupported_tokens")] IReadOnlyList<string> UnsupportedTokens,
    [property: JsonPropertyName("tools_called")] IReadOnlyList<string> ToolsCalled,
    [property: JsonPropertyName("tool_selection_ok")] bool ToolSelectionOk,
    [property: JsonPropertyName("speculative")] bool Speculative,
    [property: JsonPropertyName("latency_ms")] int LatencyMs,
    [property: JsonPropertyName("tokens")] int Tokens,
    [property: JsonPropertyName("model_calls")] int ModelCalls,
    [property: JsonPropertyName("expectation_failures")] IReadOnlyList<string> ExpectationFailures,
    [property: JsonPropertyName("brief")] InvestigationBrief? Brief,
    [property: JsonPropertyName("error")] string? Error);

public sealed record Summary(
    [property: JsonPropertyName("cases")] int Cases,
    [property: JsonPropertyName("investigated")] int Investigated,
    [property: JsonPropertyName("skipped")] int Skipped,
    [property: JsonPropertyName("budget_exhausted")] int BudgetExhausted,
    [property: JsonPropertyName("schema_valid_rate")] double SchemaValidRate,
    [property: JsonPropertyName("grounded_rate")] double GroundedRate,
    [property: JsonPropertyName("tool_selection_accuracy")] double ToolSelectionAccuracy,
    [property: JsonPropertyName("speculation_rate")] double SpeculationRate,
    [property: JsonPropertyName("mean_latency_ms")] double MeanLatencyMs,
    [property: JsonPropertyName("mean_tokens")] double MeanTokens,
    [property: JsonPropertyName("expectation_failures")] int ExpectationFailures);

public sealed record RunReport(
    [property: JsonPropertyName("run_at")] DateTime RunAt,
    [property: JsonPropertyName("prompt_version")] string PromptVersion,
    [property: JsonPropertyName("mode")] string Mode,
    [property: JsonPropertyName("git_commit")] string? GitCommit,
    [property: JsonPropertyName("provider")] string? Provider,
    [property: JsonPropertyName("model")] string? Model,
    [property: JsonPropertyName("summary")] Summary Summary,
    [property: JsonPropertyName("cases")] IReadOnlyList<CaseResult> Cases);

/// <summary>Runs every case through the production InvestigationAgent and scores the output.</summary>
public sealed class EvalRunner
{
    /// <summary>Maximum model calls in one live run; below OpenRouter's free limit of 50 requests a day.</summary>
    public const int RunCallCap = 40;

    /// <summary>
    /// Maximum model calls for one case: six tool rounds, the brief, one validation
    /// retry and one transport retry. Stops one case from using the calls of the
    /// cases after it.
    /// </summary>
    public const int PerCaseCallCap = 10;

    /// <summary>Status of a case the call cap stopped. Excluded from the rates instead of counted as a model failure.</summary>
    public const string BudgetExhaustedStatus = "budget_exhausted";

    private readonly LlmProviderSettings? _live;
    private readonly PromptStore _prompts = new();

    public EvalRunner(LlmProviderSettings? live)
    {
        _live = live;
    }

    public string Mode => _live is null ? "replay" : "live";

    public async Task<RunReport> RunAsync(IReadOnlyList<EvalCase> cases, CancellationToken ct)
    {
        // A live case that falls back says only "no provider produced a brief";
        // the provider's error is in the agent's log lines, so live mode prints
        // them. Replay stays quiet. Disposing the factory flushes the console.
        using var logs = _live is null
            ? NullLoggerFactory.Instance
            : LoggerFactory.Create(b => b.AddSimpleConsole(o => o.SingleLine = true).SetMinimumLevel(LogLevel.Information));

        ILlmClient? liveClient = _live is null
            ? null
            : new OpenAiCompatibleChatClient(new HttpClient { Timeout = TimeSpan.FromSeconds(60) }, logs.CreateLogger<OpenAiCompatibleChatClient>());

        var remaining = _live is null ? int.MaxValue : RunCallCap;
        var results = new List<CaseResult>();

        foreach (var c in cases)
        {
            ct.ThrowIfCancellationRequested();

            // Each case gets its own budget, capped by the calls left in the run.
            var settings = Settings(_live is null ? int.MaxValue : Math.Min(PerCaseCallCap, remaining));
            var budget = new LlmCallBudget(Options.Create(settings), TimeProvider.System);

            var llm = liveClient ?? new ReplayLlmClient(c.ReplayScript);
            var agent = new InvestigationAgent(
                llm, Options.Create(settings), _prompts, budget, TimeProvider.System,
                logs.CreateLogger<InvestigationAgent>());
            var tools = new RecordedToolSource(c.Tools);

            var sw = Stopwatch.StartNew();
            var result = await agent.InvestigateAsync(c.ToEvent(), tools, ct);
            sw.Stop();

            if (_live is not null)
                remaining = Math.Max(0, remaining - budget.CallsToday);

            results.Add(Score(c, result, _live is null ? result.LatencyMs : (int)sw.ElapsedMilliseconds));
        }

        var investigated = results
            .Where(r => r.Status != InvestigationStatus.Skipped && r.Status != BudgetExhaustedStatus)
            .ToList();
        double Rate(Func<CaseResult, bool> pred) => investigated.Count == 0 ? 0 : investigated.Count(pred) / (double)investigated.Count;

        var summary = new Summary(
            results.Count,
            investigated.Count,
            results.Count(r => r.Status == InvestigationStatus.Skipped),
            results.Count(r => r.Status == BudgetExhaustedStatus),
            Rate(r => r.SchemaValid),
            Rate(r => r.Grounded),
            Rate(r => r.ToolSelectionOk),
            Rate(r => r.Speculative),
            investigated.Count == 0 ? 0 : investigated.Average(r => r.LatencyMs),
            investigated.Count == 0 ? 0 : investigated.Average(r => r.Tokens),
            results.Sum(r => r.ExpectationFailures.Count));

        return new RunReport(DateTime.UtcNow, _prompts.Version, Mode, GitCommit(), _live?.Name, _live?.Model, summary, results);
    }

    private LlmSettings Settings(int callCap) => new()
    {
        Enabled = true,
        MaxCallsPerDay = callCap,
        MinSecondsBetweenCalls = _live is null ? 0 : 3,
        MaxToolSteps = 6,
        Providers = _live is null
            ? [new LlmProviderSettings { Name = "replay", BaseUrl = "replay://", Model = "recorded", ApiKeyEnv = "UNUSED" }]
            : [_live]
    };

    /// <summary>The commit under test: GITHUB_SHA in CI, otherwise <c>git rev-parse HEAD</c>, otherwise null.</summary>
    private static string? GitCommit()
    {
        var sha = Environment.GetEnvironmentVariable("GITHUB_SHA");
        if (!string.IsNullOrWhiteSpace(sha))
            return sha;

        try
        {
            using var git = Process.Start(new ProcessStartInfo("git", "rev-parse HEAD")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            });
            if (git is null)
                return null;
            var output = git.StandardOutput.ReadToEnd().Trim();
            git.WaitForExit(5000);
            return git.ExitCode == 0 && output.Length > 0 ? output : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static CaseResult Score(EvalCase c, InvestigationResult r, int latencyMs)
    {
        // The call cap stopped this case: there is no model output to score, so expectations are not checked.
        if (r.Error == InvestigationAgent.BudgetSpentReason)
            return new CaseResult(c.Name, BudgetExhaustedStatus, false, false, [], r.ToolsCalled, false, false,
                latencyMs, r.InputTokens + r.OutputTokens, r.ModelCalls, [], null, r.Error);

        var failures = new List<string>();

        if (c.Expect.Status is { } status && r.Status != status)
            failures.Add($"status: expected {status}, got {r.Status}");

        var missingTools = c.Expect.ToolsCalled.Except(r.ToolsCalled).ToList();
        var toolSelectionOk = missingTools.Count == 0;
        if (!toolSelectionOk)
            failures.Add($"tools not called: {string.Join(", ", missingTools)}");

        if (c.Expect.Grounded is { } grounded && r.Status != InvestigationStatus.Skipped && r.Grounded != grounded)
            failures.Add($"grounded: expected {grounded}, got {r.Grounded}");

        // Whole words, case-insensitive: "because" must not trip "cause".
        var text = r.Brief?.ToJson() ?? r.RawOutput ?? string.Empty;
        foreach (var banned in c.Expect.MustNotContain)
        {
            if (System.Text.RegularExpressions.Regex.IsMatch(text, $@"\b{System.Text.RegularExpressions.Regex.Escape(banned)}\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                failures.Add($"brief contains \"{banned}\"");
        }

        // Schema-valid means the model's own output parsed. A deterministic brief
        // is always valid by construction and must not inflate the rate.
        var schemaValid = r.Status == InvestigationStatus.Completed;

        return new CaseResult(
            c.Name,
            r.Status,
            schemaValid,
            r.Grounded,
            r.UnsupportedTokens,
            r.ToolsCalled,
            toolSelectionOk,
            r.Brief?.IsSpeculative ?? false,
            latencyMs,
            r.InputTokens + r.OutputTokens,
            r.ModelCalls,
            failures,
            r.Brief,
            r.Error);
    }
}
