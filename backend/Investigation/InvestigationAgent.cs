using System.Diagnostics;
using System.Text.Json;
using AltusIQ.Api.Llm;
using AltusIQ.Api.Models;
using AltusIQ.Api.Models.Dtos;
using Microsoft.Extensions.Options;

namespace AltusIQ.Api.Investigation;

/// <summary>
/// Runs one investigation: provider by provider, a tool loop of at most
/// MaxToolSteps rounds, a validated brief, and a groundedness check. Falls
/// back to single-shot mode when a model rejects tools, to the next provider on
/// 429/5xx/timeout or invalid output after one retry, and to a deterministic
/// brief when nothing is left. The same code path serves production and the
/// eval harness; only ILlmClient and IInvestigationToolSource differ.
/// </summary>
public sealed class InvestigationAgent
{
    public const int ToolOutputCapBytes = 8 * 1024;
    private const string TruncationMarker = "…[truncated at 8 KB]";

    private static readonly JsonSerializerOptions RecordJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false
    };

    private readonly ILlmClient _llm;
    private readonly LlmSettings _settings;
    private readonly PromptStore _prompts;
    private readonly LlmCallBudget _budget;
    private readonly TimeProvider _clock;
    private readonly ILogger<InvestigationAgent> _logger;

    public InvestigationAgent(
        ILlmClient llm,
        IOptions<LlmSettings> settings,
        PromptStore prompts,
        LlmCallBudget budget,
        TimeProvider clock,
        ILogger<InvestigationAgent> logger)
    {
        _llm = llm;
        _settings = settings.Value;
        _prompts = prompts;
        _budget = budget;
        _clock = clock;
        _logger = logger;
    }

    public async Task<InvestigationResult> InvestigateAsync(
        AnomalyEvent evt, IInvestigationToolSource tools, CancellationToken ct)
    {
        if (evt.Sensitive)
            return InvestigationResult.Skipped(_prompts.Version);

        var run = new ToolRun(evt, tools);
        var record = EventRecordJson(evt);

        InvestigationResult? invalid = null;

        if (_settings.Enabled)
        {
            foreach (var provider in _settings.Providers)
            {
                var outcome = await RunProviderAsync(provider, evt, record, run, ct);

                if (outcome.Result is not null && outcome.Result.Status == InvestigationStatus.Completed)
                    return outcome.Result;

                if (outcome.Result is not null && outcome.Result.Status == InvestigationStatus.InvalidOutput)
                    invalid = outcome.Result;

                if (outcome.BudgetExhausted)
                {
                    _logger.LogWarning("Daily model budget spent ({Calls}); event {EventId} gets the deterministic brief",
                        _budget.CallsToday, evt.Id);
                    break;
                }
            }
        }

        if (invalid is not null)
            return invalid;

        await run.GatherAllAsync(ct);
        var brief = DeterministicBrief.Build(evt, run.Outputs);
        var grounded = GroundednessChecker.Check(brief.ToJson(), run.Outputs.Values.Append(record));

        return new InvestigationResult(
            InvestigationStatus.Uninvestigated, brief, grounded.Grounded, grounded.Unsupported,
            _prompts.Version, null, null, 0, 0, 0, 0, run.Calls, run.Outputs,
            _settings.Enabled ? "no provider produced a brief" : "Llm:Enabled is false", null);
    }

    private sealed record ProviderOutcome(InvestigationResult? Result, bool BudgetExhausted);

    private async Task<ProviderOutcome> RunProviderAsync(
        LlmProviderSettings provider, AnomalyEvent evt, string record, ToolRun run, CancellationToken ct)
    {
        var messages = new List<ChatMessage>
        {
            ChatMessage.System(_prompts.SystemPrompt),
            ChatMessage.User(UserPrompt(record, run))
        };

        var toolsSupported = true;
        var toolSteps = 0;
        var transportRetries = 0;
        var validationRetries = 0;
        var inputTokens = 0;
        var outputTokens = 0;
        var calls = 0;
        var stopwatch = Stopwatch.StartNew();

        while (true)
        {
            var offerTools = toolsSupported && toolSteps < _settings.MaxToolSteps;

            if (toolsSupported && toolSteps >= _settings.MaxToolSteps && messages[^1].Role == "tool")
                messages.Add(ChatMessage.User("You have used all six tool rounds. Reply now with only the JSON brief."));

            if (!_budget.TryReserve())
                return new ProviderOutcome(null, BudgetExhausted: true);

            var wait = _budget.TimeUntilNextAllowed();
            if (wait > TimeSpan.Zero)
                await Task.Delay(wait, _clock, ct);

            LlmResponse response;
            try
            {
                calls++;
                response = await _llm.CompleteAsync(
                    new LlmRequest(provider, messages, offerTools ? ToolDefinitions.All : null), ct);
            }
            catch (LlmException ex) when (ex.Kind == LlmFailureKind.ToolsUnsupported)
            {
                _logger.LogInformation("{Provider}/{Model} lacks tool calling, switching to single-shot mode",
                    provider.Name, provider.Model);
                toolsSupported = false;
                await run.GatherAllAsync(ct);
                messages =
                [
                    ChatMessage.System(_prompts.SystemPrompt),
                    ChatMessage.User(SingleShotPrompt(record, run))
                ];
                continue;
            }
            catch (LlmException ex) when (ex.IsTransient && transportRetries < 1)
            {
                transportRetries++;
                _logger.LogWarning(ex, "{Provider} failed ({Kind}), retrying once", provider.Name, ex.Kind);
                continue;
            }
            catch (LlmException ex)
            {
                _logger.LogWarning(ex, "{Provider} failed ({Kind}), moving to the next provider", provider.Name, ex.Kind);
                return new ProviderOutcome(null, false);
            }

            inputTokens += response.InputTokens;
            outputTokens += response.OutputTokens;

            if (offerTools && response.ToolCalls.Count > 0)
            {
                toolSteps++;
                messages.Add(ChatMessage.Assistant(response.Content, response.ToolCalls));

                foreach (var call in response.ToolCalls)
                {
                    var output = await run.ExecuteAsync(call.Name, call.ArgumentsJson, ct);
                    messages.Add(ChatMessage.Tool(call.Id, output));
                }

                continue;
            }

            if (BriefValidator.TryParse(response.Content, out var brief, out var error))
            {
                var grounded = GroundednessChecker.Check(brief!.ToJson(), run.Outputs.Values.Append(record));

                if (!grounded.Grounded)
                    _logger.LogInformation("Brief for {EventId} is ungrounded: {Tokens}", evt.Id, string.Join(", ", grounded.Unsupported));

                return new ProviderOutcome(new InvestigationResult(
                    InvestigationStatus.Completed, brief, grounded.Grounded, grounded.Unsupported,
                    _prompts.Version, provider.Name, provider.Model, inputTokens, outputTokens,
                    (int)stopwatch.ElapsedMilliseconds, calls, run.Calls, run.Outputs, null, response.Content), false);
            }

            if (validationRetries < 1)
            {
                validationRetries++;
                messages.Add(ChatMessage.Assistant(response.Content));
                messages.Add(ChatMessage.User(
                    $"That reply was rejected: {error}. Reply again with only the JSON object in the required shape."));
                continue;
            }

            _logger.LogWarning("{Provider} produced invalid output twice for {EventId}: {Error}", provider.Name, evt.Id, error);
            return new ProviderOutcome(new InvestigationResult(
                InvestigationStatus.InvalidOutput, null, false, [], _prompts.Version, provider.Name, provider.Model,
                inputTokens, outputTokens, (int)stopwatch.ElapsedMilliseconds, calls, run.Calls, run.Outputs,
                error, response.Content), false);
        }
    }

    private static string UserPrompt(string record, ToolRun run) =>
        $"Event record:\n{record}\n\nGather what you need with the tools, then reply with only the JSON brief.";

    private static string SingleShotPrompt(string record, ToolRun run)
    {
        var parts = run.Outputs.Select(kvp => $"### {kvp.Key}\n{kvp.Value ?? "(nothing returned)"}");
        return $"Event record:\n{record}\n\nTool outputs, gathered for you:\n{string.Join("\n\n", parts)}\n\nReply with only the JSON brief.";
    }

    /// <summary>The event as the model sees it: detector fields plus the evidence point counts, no investigation columns.</summary>
    internal static string EventRecordJson(AnomalyEvent evt)
    {
        var evidence = ParseEvidence(evt.EvidenceJson);

        return JsonSerializer.Serialize(new
        {
            evt.Id,
            evt.Icao24,
            evt.Callsign,
            evt.OriginCountry,
            evt.Squawk,
            evt.Kind,
            evt.FirstSeenAt,
            evt.ConfirmedAt,
            evt.LastSeenAt,
            evt.ClosedAt,
            evt.Status,
            evt.FirstLongitude,
            evt.FirstLatitude,
            evt.FirstAltitude,
            evt.LastLongitude,
            evt.LastLatitude,
            evt.LastAltitude,
            EvidencePointsAtConfirm = evidence?.AtConfirm?.SourcePointCount ?? 0,
            EvidencePointsAtClose = evidence?.AtClose?.SourcePointCount ?? 0,
            PollIntervalSeconds = 120
        }, RecordJson);
    }

    internal static AnomalyEvidence? ParseEvidence(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<AnomalyEvidence>(json, RecordJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static string Truncate(string output)
    {
        if (output.Length <= ToolOutputCapBytes)
            return output;

        return output[..(ToolOutputCapBytes - TruncationMarker.Length)] + TruncationMarker;
    }

    /// <summary>Per-investigation tool dispatch, call log and output cache, shared by both modes.</summary>
    private sealed class ToolRun
    {
        private readonly AnomalyEvent _evt;
        private readonly IInvestigationToolSource _tools;
        private readonly List<string> _calls = [];
        private readonly Dictionary<string, string?> _outputs = new();

        public ToolRun(AnomalyEvent evt, IInvestigationToolSource tools)
        {
            _evt = evt;
            _tools = tools;
        }

        public IReadOnlyList<string> Calls => _calls;
        public IReadOnlyDictionary<string, string?> Outputs => _outputs;

        public async Task<string> ExecuteAsync(string name, string argumentsJson, CancellationToken ct)
        {
            JsonElement args;
            try
            {
                using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
                args = doc.RootElement.Clone();
            }
            catch (JsonException)
            {
                args = JsonDocument.Parse("{}").RootElement.Clone();
            }

            string? raw;
            try
            {
                raw = name switch
                {
                    ToolNames.GetTrail => await TrailAsync(Str(args, "icao24") ?? _evt.Icao24, ct),
                    ToolNames.NearestAirports => await _tools.NearestAirportsAsync(
                        Num(args, "lat") ?? Num(args, "latitude") ?? _evt.LastLatitude ?? 0,
                        Num(args, "lon") ?? Num(args, "longitude") ?? _evt.LastLongitude ?? 0,
                        (int)(Num(args, "limit") ?? 3), ct),
                    ToolNames.GetMetar => await _tools.GetMetarAsync(Str(args, "icao") ?? string.Empty, ct),
                    ToolNames.GetRoute => await _tools.GetRouteAsync(Str(args, "callsign") ?? _evt.Callsign ?? string.Empty, ct),
                    ToolNames.RecentFlights => await _tools.RecentFlightsAsync(Str(args, "icao24") ?? _evt.Icao24, ct),
                    _ => JsonSerializer.Serialize(new { error = $"unknown tool {name}" })
                };
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                raw = JsonSerializer.Serialize(new { error = $"{name} failed: {ex.GetType().Name}" });
            }

            var output = Truncate(raw ?? JsonSerializer.Serialize(new { result = (object?)null, note = "nothing returned" }));

            _calls.Add(name);
            _outputs[name] = output;
            return output;
        }

        /// <summary>Code-driven gathering for single-shot mode and the deterministic brief.</summary>
        public async Task GatherAllAsync(CancellationToken ct)
        {
            if (!_outputs.ContainsKey(ToolNames.GetTrail))
                await ExecuteAsync(ToolNames.GetTrail, "{}", ct);

            if (!_outputs.ContainsKey(ToolNames.NearestAirports) && _evt.LastLatitude is not null && _evt.LastLongitude is not null)
                await ExecuteAsync(ToolNames.NearestAirports, "{\"limit\":3}", ct);

            if (!_outputs.ContainsKey(ToolNames.GetMetar))
            {
                var station = FirstAirport(_outputs.GetValueOrDefault(ToolNames.NearestAirports));
                if (station is not null)
                    await ExecuteAsync(ToolNames.GetMetar, JsonSerializer.Serialize(new { icao = station }), ct);
            }

            if (!_outputs.ContainsKey(ToolNames.GetRoute) && !string.IsNullOrWhiteSpace(_evt.Callsign))
                await ExecuteAsync(ToolNames.GetRoute, "{}", ct);

            if (!_outputs.ContainsKey(ToolNames.RecentFlights))
                await ExecuteAsync(ToolNames.RecentFlights, "{}", ct);
        }

        /// <summary>Live trail first; once the aircraft is gone, the evidence snapshot frozen at close or confirm.</summary>
        private async Task<string?> TrailAsync(string icao24, CancellationToken ct)
        {
            var live = await _tools.GetTrailAsync(icao24, ct);
            if (live is not null)
                return live;

            var evidence = ParseEvidence(_evt.EvidenceJson);
            var snapshot = evidence?.AtClose ?? evidence?.AtConfirm;
            if (snapshot is null || snapshot.TrackPoints.Count == 0)
                return null;

            return TrailJson.Compact(_evt.Icao24, _evt.Callsign, snapshot.TrackPoints, LiveToolSource.MaxTrailPoints);
        }

        private static string? FirstAirport(string? nearestJson)
        {
            if (nearestJson is null) return null;
            try
            {
                using var doc = JsonDocument.Parse(nearestJson);
                if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
                    return null;
                return doc.RootElement[0].GetProperty("icao").GetString();
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
            {
                return null;
            }
        }

        private static string? Str(JsonElement args, string name) =>
            args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString()
                : null;

        private static double? Num(JsonElement args, string name)
        {
            if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty(name, out var v))
                return null;
            if (v.ValueKind == JsonValueKind.Number) return v.GetDouble();
            if (v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d)) return d;
            return null;
        }
    }
}

public static class ToolDefinitions
{
    public static readonly IReadOnlyList<ToolDefinition> All =
    [
        new(ToolNames.GetTrail,
            "Recent track points for the aircraft: rows of [unix_ts, lon, lat, alt_m, heading, velocity_ms].",
            """{"type":"object","properties":{"icao24":{"type":"string","description":"ICAO 24-bit hex address"}},"required":["icao24"]}"""),
        new(ToolNames.NearestAirports,
            "Airports nearest a position, with distance in km and bearing.",
            """{"type":"object","properties":{"lat":{"type":"number"},"lon":{"type":"number"},"limit":{"type":"integer","minimum":1,"maximum":10}},"required":["lat","lon"]}"""),
        new(ToolNames.GetMetar,
            "Latest METAR weather report for a four-letter ICAO airport code.",
            """{"type":"object","properties":{"icao":{"type":"string","description":"Four-letter ICAO station, e.g. ESSA"}},"required":["icao"]}"""),
        new(ToolNames.GetRoute,
            "Planned origin and destination for a callsign from the adsbdb database, when listed.",
            """{"type":"object","properties":{"callsign":{"type":"string"}},"required":["callsign"]}"""),
        new(ToolNames.RecentFlights,
            "Up to five earlier stored flights for the same airframe.",
            """{"type":"object","properties":{"icao24":{"type":"string"}},"required":["icao24"]}""")
    ];
}
