using System.Text.Json;
using AltusIQ.Api.Models;

namespace AltusIQ.Api.Investigation;

/// <summary>Everything one investigation produced, in a shape both the store and the eval harness can read.</summary>
public sealed record InvestigationResult(
    string Status,
    InvestigationBrief? Brief,
    bool Grounded,
    IReadOnlyList<string> UnsupportedTokens,
    string PromptVersion,
    string? Provider,
    string? Model,
    int InputTokens,
    int OutputTokens,
    int LatencyMs,
    int ModelCalls,
    IReadOnlyList<string> ToolsCalled,
    IReadOnlyDictionary<string, string?> ToolOutputs,
    string? Error,
    string? RawOutput)
{
    public static InvestigationResult Skipped(string promptVersion) => new(
        InvestigationStatus.Skipped, null, false, [], promptVersion, null, null, 0, 0, 0, 0, [], new Dictionary<string, string?>(), "sensitive event", null);

    /// <summary>What lands in InvestigationJson: the brief, or the rejection when there is none.</summary>
    public string? ToStoredJson()
    {
        if (Brief is not null)
            return Brief.ToJson();

        if (Status == InvestigationStatus.Skipped)
            return null;

        return JsonSerializer.Serialize(new { error = Error, raw = RawOutput });
    }

    public void ApplyTo(AnomalyEvent evt)
    {
        evt.InvestigationStatus = Status;
        evt.InvestigationJson = ToStoredJson();
        evt.Grounded = Grounded;
        evt.PromptVersion = PromptVersion;
        evt.Provider = Provider;
        evt.Model = Model;
        evt.InputTokens = InputTokens;
        evt.OutputTokens = OutputTokens;
        evt.LatencyMs = LatencyMs;
    }
}
