using System.Threading;
using System.Threading.Tasks;

namespace ScreenTranslator.Translation;

public enum TranslatorKind
{
    DeepL,
    Google,
    Yandex,
    Papago
}

public interface ITranslator
{
    string Name { get; }

    /// <summary>
    /// Translates text from the source language (internal LanguageCatalog key, or null/"auto"
    /// to let the provider auto-detect) into the target language.
    /// </summary>
    Task<string> TranslateAsync(string text, string? sourceLanguageKey, string targetLanguageKey, CancellationToken ct);
}
