using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AltusIQ.Api.Models;

namespace AltusIQ.Api.Llm;

/// <summary>
/// Chat completions over the OpenAI-compatible shape that Groq and OpenRouter
/// both speak, with the tools parameter. Keys come from the environment
/// variable each provider names; nothing here knows a provider by name.
/// </summary>
public sealed class OpenAiCompatibleChatClient : ILlmClient
{
    private readonly HttpClient _http;
    private readonly ILogger<OpenAiCompatibleChatClient> _logger;

    public OpenAiCompatibleChatClient(HttpClient http, ILogger<OpenAiCompatibleChatClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        var apiKey = Environment.GetEnvironmentVariable(request.Provider.ApiKeyEnv);
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new LlmException(LlmFailureKind.NotConfigured,
                $"Environment variable {request.Provider.ApiKeyEnv} is not set for provider {request.Provider.Name}");

        var body = BuildBody(request);

        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            $"{request.Provider.BaseUrl.TrimEnd('/')}/chat/completions");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        message.Headers.TryAddWithoutValidation("X-Title", "AltusIQ");
        message.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(message, ct);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new LlmException(LlmFailureKind.Timeout, $"{request.Provider.Name} timed out", null, ex);
        }
        catch (HttpRequestException ex)
        {
            throw new LlmException(LlmFailureKind.ServerError, $"{request.Provider.Name} unreachable: {ex.Message}", null, ex);
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
                throw Classify(request.Provider.Name, response.StatusCode, text, request.Tools is not null);

            return ParseResponse(text);
        }
    }

    private static LlmException Classify(string provider, HttpStatusCode status, string body, bool sentTools)
    {
        var code = (int)status;
        var snippet = body.Length > 300 ? body[..300] : body;

        if (code == 429)
            return new LlmException(LlmFailureKind.RateLimited, $"{provider} rate limited: {snippet}", code);

        if (code >= 500)
            return new LlmException(LlmFailureKind.ServerError, $"{provider} returned {code}: {snippet}", code);

        // Groq and OpenRouter both reject tools on a model that lacks them with
        // a 4xx whose message names the feature. Fall back to single-shot mode.
        if (sentTools && code is 400 or 404 or 422
            && body.Contains("tool", StringComparison.OrdinalIgnoreCase))
            return new LlmException(LlmFailureKind.ToolsUnsupported, $"{provider} rejected tools: {snippet}", code);

        return new LlmException(LlmFailureKind.Other, $"{provider} returned {code}: {snippet}", code);
    }

    internal static JsonObject BuildBody(LlmRequest request)
    {
        var messages = new JsonArray();

        foreach (var m in request.Messages)
        {
            var obj = new JsonObject { ["role"] = m.Role };

            // OpenAI's shape wants content present even when the assistant only
            // called tools; null is accepted, a missing key is not everywhere.
            obj["content"] = m.Content;

            if (m.ToolCalls is { Count: > 0 })
            {
                var calls = new JsonArray();
                foreach (var call in m.ToolCalls)
                {
                    calls.Add(new JsonObject
                    {
                        ["id"] = call.Id,
                        ["type"] = "function",
                        ["function"] = new JsonObject
                        {
                            ["name"] = call.Name,
                            ["arguments"] = call.ArgumentsJson
                        }
                    });
                }
                obj["tool_calls"] = calls;
            }

            if (m.ToolCallId is not null)
                obj["tool_call_id"] = m.ToolCallId;

            messages.Add(obj);
        }

        var body = new JsonObject
        {
            ["model"] = request.Provider.Model,
            ["messages"] = messages,
            ["temperature"] = 0.2
        };

        if (request.Tools is { Count: > 0 })
        {
            var tools = new JsonArray();
            foreach (var tool in request.Tools)
            {
                tools.Add(new JsonObject
                {
                    ["type"] = "function",
                    ["function"] = new JsonObject
                    {
                        ["name"] = tool.Name,
                        ["description"] = tool.Description,
                        ["parameters"] = JsonNode.Parse(tool.ParametersJsonSchema)
                    }
                });
            }
            body["tools"] = tools;
            body["tool_choice"] = "auto";
        }

        return body;
    }

    internal static LlmResponse ParseResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
            throw new LlmException(LlmFailureKind.Other, "Response had no choices");

        var message = choices[0].GetProperty("message");
        var content = message.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String
            ? c.GetString()
            : null;

        var calls = new List<ToolCall>();
        if (message.TryGetProperty("tool_calls", out var toolCalls) && toolCalls.ValueKind == JsonValueKind.Array)
        {
            foreach (var call in toolCalls.EnumerateArray())
            {
                var fn = call.GetProperty("function");
                var args = fn.TryGetProperty("arguments", out var a) && a.ValueKind == JsonValueKind.String
                    ? a.GetString() ?? "{}"
                    : "{}";
                calls.Add(new ToolCall(
                    call.TryGetProperty("id", out var id) ? id.GetString() ?? Guid.NewGuid().ToString("N") : Guid.NewGuid().ToString("N"),
                    fn.GetProperty("name").GetString() ?? string.Empty,
                    args));
            }
        }

        var inputTokens = 0;
        var outputTokens = 0;
        if (root.TryGetProperty("usage", out var usage))
        {
            if (usage.TryGetProperty("prompt_tokens", out var p) && p.ValueKind == JsonValueKind.Number)
                inputTokens = p.GetInt32();
            if (usage.TryGetProperty("completion_tokens", out var o) && o.ValueKind == JsonValueKind.Number)
                outputTokens = o.GetInt32();
        }

        return new LlmResponse(content, calls, inputTokens, outputTokens);
    }
}
