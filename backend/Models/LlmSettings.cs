namespace AltusIQ.Api.Models;

public class LlmSettings
{
    /// <summary>Off disables model calls entirely; the deterministic brief is still written.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Model calls per UTC day across all providers. OpenRouter's free models allow 50.</summary>
    public int MaxCallsPerDay { get; set; } = 40;

    public int MinSecondsBetweenCalls { get; set; } = 3;
    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>Tool-call rounds per investigation before the model is asked to conclude.</summary>
    public int MaxToolSteps { get; set; } = 6;

    /// <summary>Tried in order; a 429, 5xx, timeout or invalid output after one retry moves to the next.</summary>
    public List<LlmProviderSettings> Providers { get; set; } = [];
}

public class LlmProviderSettings
{
    public required string Name { get; set; }
    public required string BaseUrl { get; set; }
    public required string Model { get; set; }

    /// <summary>Name of the environment variable holding the key; never the key itself.</summary>
    public required string ApiKeyEnv { get; set; }
}
