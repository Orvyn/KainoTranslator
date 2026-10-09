namespace ScreenTranslator.Translation;

/// <summary>
/// Known OpenAI-compatible providers, so the person picks a name instead of typing a URL by
/// hand. "Custom" is the escape hatch for anything else (Groq, a local Ollama/LM Studio server,
/// another provider entirely) - its URL comes from the settings field instead of this table.
/// Single source of truth for both the Settings UI (SettingsWindow.xaml.cs) and
/// TranslatorFactory, so the two can't drift apart.
/// </summary>
public static class OpenAiCompatiblePresets
{
    public const string Gemini = "Gemini";
    public const string DeepSeek = "DeepSeek";
    public const string OpenAi = "OpenAI";
    public const string Custom = "Custom";

    /// <summary>The base URL to send requests to - for a known provider, always this value
    /// regardless of whatever is sitting in the custom-URL field; for "Custom", that field's
    /// value verbatim.</summary>
    public static string ResolveBaseUrl(string provider, string customBaseUrl) => provider switch
    {
        Gemini => "https://generativelanguage.googleapis.com/v1beta/openai",
        DeepSeek => "https://api.deepseek.com/v1",
        OpenAi => "https://api.openai.com/v1",
        _ => customBaseUrl,
    };

    /// <summary>Where to get an API key for this provider, or null when there isn't one fixed
    /// page to point at (Custom).</summary>
    public static string? KeyPageUrl(string provider) => provider switch
    {
        Gemini => "https://aistudio.google.com/apikey",
        DeepSeek => "https://platform.deepseek.com/api_keys",
        OpenAi => "https://platform.openai.com/api-keys",
        _ => null,
    };
}
