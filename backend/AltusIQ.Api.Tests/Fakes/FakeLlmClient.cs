using AltusIQ.Api.Llm;

namespace AltusIQ.Api.Tests.Fakes;

/// <summary>
/// Scripted ILlmClient. Each scripted step answers one request, in order; once
/// the script runs out, Default answers. A step may throw to simulate a 429,
/// a 5xx or a timeout. Every request is recorded for assertions.
/// </summary>
internal sealed class FakeLlmClient : ILlmClient
{
    public const string ValidBrief =
        """
        {"summary":"Squawk 7700 held for about 10 minutes near ESSA at 3000 m.",
         "observations":["The trail descended toward ESSA."],
         "explanations":[{"hypothesis":"A precautionary return to the departure airport","evidence":["descent toward ESSA"],"plausibility":"medium"}],
         "unknowns":["Why the code was set."],
         "evidence_quality":"fair",
         "recommended_follow_up":"An operator statement would settle it."}
        """;

    private readonly Queue<Func<LlmRequest, LlmResponse>> _script = new();

    public List<LlmRequest> Requests { get; } = [];
    public Func<LlmRequest, LlmResponse> Default { get; set; } = _ => Brief(ValidBrief);

    public FakeLlmClient Then(Func<LlmRequest, LlmResponse> step)
    {
        _script.Enqueue(step);
        return this;
    }

    public FakeLlmClient ThenBrief(string json) => Then(_ => Brief(json));
    public FakeLlmClient ThenText(string text) => Then(_ => new LlmResponse(text, [], 10, 5));
    public FakeLlmClient ThenThrow(LlmFailureKind kind, int? status = null) =>
        Then(_ => throw new LlmException(kind, $"scripted {kind}", status));
    public FakeLlmClient ThenToolCall(string name, string argumentsJson = "{}") =>
        Then(_ => ToolCalls((name, argumentsJson)));

    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        Requests.Add(request);
        var step = _script.Count > 0 ? _script.Dequeue() : Default;
        return Task.FromResult(step(request));
    }

    public static LlmResponse Brief(string json) => new(json, [], 120, 80);

    public static LlmResponse ToolCalls(params (string Name, string Args)[] calls) => new(
        null,
        calls.Select((c, i) => new ToolCall($"call_{i}", c.Name, c.Args)).ToList(),
        50,
        20);
}
