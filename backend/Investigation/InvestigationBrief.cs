using System.Text.Json;
using System.Text.Json.Serialization;

namespace AltusIQ.Api.Investigation;

/// <summary>
/// The only shape the model may answer in. No "cause" and no "confidence"
/// anywhere: ADS-B cannot show a cause, and a number dressed as confidence
/// would be read as one. Plausibility is a word, and it is the model's.
/// </summary>
public sealed record InvestigationBrief(
    [property: JsonPropertyName("summary")] string Summary,
    [property: JsonPropertyName("observations")] IReadOnlyList<string> Observations,
    [property: JsonPropertyName("explanations")] IReadOnlyList<Explanation> Explanations,
    [property: JsonPropertyName("unknowns")] IReadOnlyList<string> Unknowns,
    [property: JsonPropertyName("evidence_quality")] string EvidenceQuality,
    [property: JsonPropertyName("recommended_follow_up")] string RecommendedFollowUp)
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>A high-plausibility explanation on poor evidence: the pattern the eval harness counts as speculation.</summary>
    public bool IsSpeculative =>
        EvidenceQuality == "poor" && Explanations.Any(e => e.Plausibility == "high");
}

public sealed record Explanation(
    [property: JsonPropertyName("hypothesis")] string Hypothesis,
    [property: JsonPropertyName("evidence")] IReadOnlyList<string> Evidence,
    [property: JsonPropertyName("plausibility")] string Plausibility);

public static class BriefValidator
{
    private static readonly string[] Plausibilities = ["low", "medium", "high"];
    private static readonly string[] Qualities = ["poor", "fair", "good"];
    private static readonly string[] ForbiddenKeys = ["cause", "confidence"];

    /// <summary>
    /// Parses the model's text into a brief. Tolerates a fenced code block or
    /// prose around the object; rejects anything that is not exactly the schema.
    /// </summary>
    public static bool TryParse(string? text, out InvestigationBrief? brief, out string error)
    {
        brief = null;

        var json = ExtractJsonObject(text);
        if (json is null)
        {
            error = "no JSON object found in the reply";
            return false;
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            error = $"malformed JSON: {ex.Message}";
            return false;
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "top level is not an object";
                return false;
            }

            foreach (var key in ForbiddenKeys)
            {
                if (ContainsKey(root, key))
                {
                    error = $"the key \"{key}\" is not allowed anywhere in the brief";
                    return false;
                }
            }

            if (!RequireString(root, "summary", out var summary, out error)) return false;
            if (!RequireStringArray(root, "observations", out var observations, out error)) return false;
            if (!RequireStringArray(root, "unknowns", out var unknowns, out error)) return false;
            if (!RequireString(root, "recommended_follow_up", out var followUp, out error)) return false;
            if (!RequireEnum(root, "evidence_quality", Qualities, out var quality, out error)) return false;

            if (!root.TryGetProperty("explanations", out var explanationsEl) || explanationsEl.ValueKind != JsonValueKind.Array)
            {
                error = "\"explanations\" must be an array";
                return false;
            }

            var explanations = new List<Explanation>();
            var index = 0;
            foreach (var item in explanationsEl.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    error = $"explanations[{index}] is not an object";
                    return false;
                }
                if (!RequireString(item, "hypothesis", out var hypothesis, out error)) return false;
                if (!RequireStringArray(item, "evidence", out var evidence, out error)) return false;
                if (!RequireEnum(item, "plausibility", Plausibilities, out var plausibility, out error)) return false;
                explanations.Add(new Explanation(hypothesis, evidence, plausibility));
                index++;
            }

            brief = new InvestigationBrief(summary, observations, explanations, unknowns, quality, followUp);
            error = string.Empty;
            return true;
        }
    }

    private static bool ContainsKey(JsonElement element, string key)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    if (string.Equals(prop.Name, key, StringComparison.OrdinalIgnoreCase) || ContainsKey(prop.Value, key))
                        return true;
                }
                return false;
            case JsonValueKind.Array:
                return element.EnumerateArray().Any(e => ContainsKey(e, key));
            default:
                return false;
        }
    }

    private static bool RequireString(JsonElement obj, string name, out string value, out string error)
    {
        value = string.Empty;
        if (!obj.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(el.GetString()))
        {
            error = $"\"{name}\" must be a non-empty string";
            return false;
        }
        value = el.GetString()!;
        error = string.Empty;
        return true;
    }

    private static bool RequireStringArray(JsonElement obj, string name, out IReadOnlyList<string> value, out string error)
    {
        value = [];
        if (!obj.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.Array)
        {
            error = $"\"{name}\" must be an array of strings";
            return false;
        }
        var list = new List<string>();
        foreach (var item in el.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                error = $"\"{name}\" must contain only strings";
                return false;
            }
            list.Add(item.GetString()!);
        }
        value = list;
        error = string.Empty;
        return true;
    }

    private static bool RequireEnum(JsonElement obj, string name, string[] allowed, out string value, out string error)
    {
        value = string.Empty;
        if (!obj.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.String)
        {
            error = $"\"{name}\" must be one of {string.Join("|", allowed)}";
            return false;
        }
        var s = el.GetString()!.Trim().ToLowerInvariant();
        if (!allowed.Contains(s))
        {
            error = $"\"{name}\" must be one of {string.Join("|", allowed)}, got \"{el.GetString()}\"";
            return false;
        }
        value = s;
        error = string.Empty;
        return true;
    }

    /// <summary>First balanced {...} in the text, ignoring braces inside strings.</summary>
    internal static string? ExtractJsonObject(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var start = text.IndexOf('{');
        if (start < 0)
            return null;

        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];

            if (inString)
            {
                if (escaped) escaped = false;
                else if (c == '\\') escaped = true;
                else if (c == '"') inString = false;
                continue;
            }

            if (c == '"') inString = true;
            else if (c == '{') depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                    return text[start..(i + 1)];
            }
        }

        return null;
    }
}
