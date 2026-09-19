using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ScreenTranslator.Models;

public static class GlossaryProcessor
{
    /// <summary>Looks up a recognized speaker name against the glossary (trimmed, case-insensitive).</summary>
    public static GlossaryEntry? FindSpeakerEntry(string? speaker, IReadOnlyList<GlossaryEntry> glossary)
    {
        if (string.IsNullOrWhiteSpace(speaker)) return null;
        var trimmed = speaker.Trim();
        return glossary.FirstOrDefault(e =>
            !string.IsNullOrWhiteSpace(e.SourceTerm) &&
            string.Equals(e.SourceTerm.Trim(), trimmed, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Replaces every glossary term found in the body with a placeholder before it's sent to the
    /// translator, so the translator can't mistranslate/mis-transliterate it - the placeholder is
    /// swapped back for the glossary's fixed translation (or the original term, if none was
    /// given) once the translation comes back.
    /// </summary>
    public static string ProtectTerms(string body, IReadOnlyList<GlossaryEntry> glossary, out List<(string Placeholder, string Replacement)> restoreList)
    {
        restoreList = new List<(string, string)>();
        if (string.IsNullOrEmpty(body) || glossary.Count == 0) return body;

        var result = body;
        var index = 0;
        foreach (var entry in glossary)
        {
            if (string.IsNullOrWhiteSpace(entry.SourceTerm)) continue;
            if (result.IndexOf(entry.SourceTerm, StringComparison.OrdinalIgnoreCase) < 0) continue;

            var placeholder = $"\u27e6{index}\u27e7"; // ⟦0⟧ - distinctive, very unlikely to occur naturally or get "translated"
            var replacement = string.IsNullOrWhiteSpace(entry.TargetTerm) ? entry.SourceTerm : entry.TargetTerm;

            result = Regex.Replace(result, Regex.Escape(entry.SourceTerm), placeholder.Replace("$", "$$"), RegexOptions.IgnoreCase);
            restoreList.Add((placeholder, replacement));
            index++;
        }
        return result;
    }

    /// <summary>Swaps glossary placeholders back for their intended text after translation.</summary>
    public static string RestoreTerms(string translated, List<(string Placeholder, string Replacement)> restoreList)
    {
        if (restoreList.Count == 0) return translated;
        var result = translated;
        foreach (var (placeholder, replacement) in restoreList)
            result = result.Replace(placeholder, replacement);
        return result;
    }
}
