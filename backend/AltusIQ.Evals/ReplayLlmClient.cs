using System.Text.Json;
using AltusIQ.Api.Llm;

namespace AltusIQ.Evals;

/// <summary>
/// Plays a recorded model response back, step by step. A case with no recording
/// behaves like a provider that is down, which exercises the deterministic path.
/// </summary>
public sealed class ReplayLlmClient : ILlmClient
{
    private readonly Queue<ReplayStep> _steps;
    private ReplayStep? _last;

    public ReplayLlmClient(Replay? replay)
    {
        _steps = new Queue<ReplayStep>(replay?.Steps ?? []);
    }

    public int Calls { get; private set; }

    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        Calls++;

        if (_steps.Count == 0 && _last is null)
            throw new LlmException(LlmFailureKind.ServerError, "no recorded response for this case", 503);

        // Past the end of the script, repeat the final step: that is what a
        // validation retry would see from a model that does not change its mind.
        var step = _steps.Count > 0 ? _steps.Dequeue() : _last!;
        _last = step;

        if (step.ToolCalls is { Count: > 0 } && request.Tools is not null)
        {
            var calls = step.ToolCalls
                .Select((c, i) => new ToolCall($"replay_{Calls}_{i}", c.Name, c.Arguments?.GetRawText() ?? "{}"))
                .ToList();
            return Task.FromResult(new LlmResponse(null, calls, 400, 40));
        }

        var content = step.Content switch
        {
            null => null,
            { ValueKind: JsonValueKind.String } s => s.GetString(),
            { } other => other.GetRawText()
        };

        return Task.FromResult(new LlmResponse(content, [], 1200, 350));
    }
}
