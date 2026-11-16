namespace AltusIQ.Api.Investigation;

/// <summary>
/// Loads backend/Prompts/investigate.vN.md. The version string is the file
/// stem and is written onto every event the prompt produced, so a prompt
/// change is visible in the data and in the eval results.
/// </summary>
public sealed class PromptStore
{
    public const string CurrentVersion = "investigate.v1";

    private readonly Lazy<string> _text;

    public PromptStore() : this(Path.Combine(AppContext.BaseDirectory, "Prompts"))
    {
    }

    public PromptStore(string directory)
    {
        _text = new Lazy<string>(() => File.ReadAllText(Path.Combine(directory, CurrentVersion + ".md")));
    }

    public string Version => CurrentVersion;
    public string SystemPrompt => _text.Value;
}
