using System.Globalization;
using System.Text.Json;
using AltusIQ.Api.Models;
using AltusIQ.Evals;

// Usage: dotnet run --project backend/AltusIQ.Evals [-- --cases <dir>] [--results <dir>]
// Replay mode by default. Live mode when LLM_API_KEY and LLM_PROVIDER are set
// (LLM_MODEL and LLM_BASE_URL optionally override the provider table).
// Exit code is non-zero only for harness errors, or for failed expectations in
// replay mode, which are code regressions. Live-mode quality never fails the run.

var casesDir = Arg(args, "--cases") ?? Path.Combine(AppContext.BaseDirectory, "cases");
var resultsDir = Arg(args, "--results") ?? Path.Combine(Directory.GetCurrentDirectory(), "results");

try
{
    var cases = Directory.GetFiles(casesDir, "*.json")
        .OrderBy(p => p, StringComparer.Ordinal)
        .Select(EvalCase.Load)
        .ToList();

    if (cases.Count == 0)
    {
        Console.Error.WriteLine($"No cases found in {casesDir}");
        return 2;
    }

    var live = LiveProvider();
    var runner = new EvalRunner(live);

    Console.WriteLine($"AltusIQ eval: {cases.Count} case(s), mode {runner.Mode}" +
        (live is null ? "" : $", {live.Name}/{live.Model}, capped at {EvalRunner.LiveCallCap} calls"));
    Console.WriteLine();

    var report = await runner.RunAsync(cases, CancellationToken.None);

    Directory.CreateDirectory(resultsDir);
    var file = Path.Combine(resultsDir, $"{report.RunAt:yyyyMMddTHHmmssZ}-{report.PromptVersion}.json");
    await File.WriteAllTextAsync(file, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));

    PrintTable(report);
    Console.WriteLine();
    Console.WriteLine($"Results written to {file}");

    if (live is null && report.Summary.ExpectationFailures > 0)
    {
        Console.Error.WriteLine($"{report.Summary.ExpectationFailures} replay expectation(s) failed; in replay mode that is a code regression.");
        return 1;
    }

    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Harness error: {ex}");
    return 1;
}

static string? Arg(string[] args, string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

static LlmProviderSettings? LiveProvider()
{
    var key = Environment.GetEnvironmentVariable("LLM_API_KEY");
    var provider = Environment.GetEnvironmentVariable("LLM_PROVIDER");
    if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(provider))
        return null;

    // Same table as backend/appsettings.json Llm:Providers.
    var (baseUrl, model) = provider.ToLowerInvariant() switch
    {
        "groq" => ("https://api.groq.com/openai/v1", "llama-3.3-70b-versatile"),
        "openrouter" => ("https://openrouter.ai/api/v1", "google/gemma-4-31b-it:free"),
        _ => (Environment.GetEnvironmentVariable("LLM_BASE_URL") ?? throw new InvalidOperationException($"Unknown LLM_PROVIDER {provider}; set LLM_BASE_URL"), "")
    };

    return new LlmProviderSettings
    {
        Name = provider,
        BaseUrl = Environment.GetEnvironmentVariable("LLM_BASE_URL") ?? baseUrl,
        Model = Environment.GetEnvironmentVariable("LLM_MODEL") ?? (model.Length > 0 ? model : throw new InvalidOperationException("Set LLM_MODEL")),
        ApiKeyEnv = "LLM_API_KEY"
    };
}

static void PrintTable(RunReport report)
{
    var inv = CultureInfo.InvariantCulture;
    var s = report.Summary;

    Console.WriteLine("| case | status | schema | grounded | tools ok | speculative | latency ms | tokens | notes |");
    Console.WriteLine("| --- | --- | --- | --- | --- | --- | ---: | ---: | --- |");
    foreach (var c in report.Cases)
    {
        var notes = c.ExpectationFailures.Count > 0
            ? string.Join("; ", c.ExpectationFailures)
            : c.UnsupportedTokens.Count > 0 ? $"ungrounded: {string.Join(" ", c.UnsupportedTokens)}" : "";
        Console.WriteLine(string.Format(inv, "| {0} | {1} | {2} | {3} | {4} | {5} | {6} | {7} | {8} |",
            c.Name, c.Status, Mark(c.SchemaValid), Mark(c.Grounded), Mark(c.ToolSelectionOk), Mark(c.Speculative), c.LatencyMs, c.Tokens, notes));
    }

    Console.WriteLine();
    Console.WriteLine($"| metric ({report.Mode}, {report.PromptVersion}) | value |");
    Console.WriteLine("| --- | ---: |");
    Console.WriteLine(string.Format(inv, "| cases | {0} ({1} investigated, {2} skipped) |", s.Cases, s.Investigated, s.Skipped));
    Console.WriteLine(string.Format(inv, "| schema-valid rate | {0:P0} |", s.SchemaValidRate));
    Console.WriteLine(string.Format(inv, "| grounded rate | {0:P0} |", s.GroundedRate));
    Console.WriteLine(string.Format(inv, "| tool-selection accuracy | {0:P0} |", s.ToolSelectionAccuracy));
    Console.WriteLine(string.Format(inv, "| speculation rate | {0:P0} |", s.SpeculationRate));
    Console.WriteLine(string.Format(inv, "| mean latency | {0:F0} ms |", s.MeanLatencyMs));
    Console.WriteLine(string.Format(inv, "| mean tokens | {0:F0} |", s.MeanTokens));
    Console.WriteLine(string.Format(inv, "| expectation failures | {0} |", s.ExpectationFailures));
}

static string Mark(bool b) => b ? "yes" : "no";
