using System.Collections.Generic;
using System.Threading;
using ScreenTranslator.Models;
using System.Threading.Tasks;

namespace ScreenTranslator.Translation;

public enum TranslatorKind
{
    DeepL,
    Google,
    Yandex,
    Papago,
    OpenAiCompatible
}

/// <summary>
/// Extra information an AI translator can use to translate better than a plain translation API:
/// what the text is from, the glossary, and the lines that came right before it.
/// </summary>
public sealed record TranslationContext(
    string? GameContext,
    IReadOnlyList<GlossaryEntry> Glossary,
    IReadOnlyList<string> PreviousLines,
    bool IsCharacterName = false);

/// <summary>A translator that understands <see cref="TranslationContext"/> (the AI one).</summary>
public interface IContextualTranslator : ITranslator
{
    Task<string> TranslateAsync(string text, string? sourceLanguageKey, string targetLanguageKey, TranslationContext? context, CancellationToken ct);
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
