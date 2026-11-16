using AltusIQ.Api.Models;

namespace AltusIQ.Api.Llm;

/// <summary>One chat completion round trip. The only seam between the agent and any provider.</summary>
public interface ILlmClient
{
    Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct);
}

public sealed record LlmRequest(
    LlmProviderSettings Provider,
    IReadOnlyList<ChatMessage> Messages,
    IReadOnlyList<ToolDefinition>? Tools);

public sealed record LlmResponse(
    string? Content,
    IReadOnlyList<ToolCall> ToolCalls,
    int InputTokens,
    int OutputTokens);

/// <summary>OpenAI-compatible message. Role is system, user, assistant or tool.</summary>
public sealed record ChatMessage(
    string Role,
    string? Content,
    IReadOnlyList<ToolCall>? ToolCalls = null,
    string? ToolCallId = null)
{
    public static ChatMessage System(string content) => new("system", content);
    public static ChatMessage User(string content) => new("user", content);
    public static ChatMessage Assistant(string? content, IReadOnlyList<ToolCall>? toolCalls = null) => new("assistant", content, toolCalls);
    public static ChatMessage Tool(string toolCallId, string content) => new("tool", content, null, toolCallId);
}

public sealed record ToolCall(string Id, string Name, string ArgumentsJson);

/// <summary>Parameters is a JSON Schema object, serialised as given.</summary>
public sealed record ToolDefinition(string Name, string Description, string ParametersJsonSchema);

public enum LlmFailureKind
{
    RateLimited,
    ServerError,
    Timeout,
    ToolsUnsupported,
    NotConfigured,
    Other
}

public sealed class LlmException(LlmFailureKind kind, string message, int? statusCode = null, Exception? inner = null)
    : Exception(message, inner)
{
    public LlmFailureKind Kind { get; } = kind;
    public int? StatusCode { get; } = statusCode;

    /// <summary>The kinds that mean "try the next provider" rather than "give up".</summary>
    public bool IsTransient => Kind is LlmFailureKind.RateLimited or LlmFailureKind.ServerError or LlmFailureKind.Timeout;
}
