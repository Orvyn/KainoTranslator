using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ScreenTranslator.Models;

public static class SubtitleTextProcessor
{
    /// <summary>
    /// OCR returns text line-by-line, which made translators treat each wrapped line as its
    /// own sentence. This merges everything into one continuous block of text (so translation
    /// only breaks at real sentence punctuation), while keeping a leading speaker/character
    /// name on its own line instead of merging it into the sentence.
    /// </summary>
    public static (string? Speaker, string Body) SplitSpeakerAndBody(string rawText)
    {
        var lines = rawText
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();

        if (lines.Count == 0) return (null, "");
        if (lines.Count == 1) return (null, lines[0]);

        string? speaker = null;
        var bodyLines = lines;

        var first = lines[0];
        if (LooksLikeSpeakerLabel(first, lines.Count))
        {
            speaker = first.TrimEnd(':', '：', ' ');
            bodyLines = lines.Skip(1).ToList();
        }

        // Join wrapped lines into one continuous piece of text so the translator sees full
        // sentences instead of isolated fragments. A single space is enough - OCR line breaks
        // are just word-wrap, not paragraph breaks.
        var body = string.Join(" ", bodyLines);
        body = Regex.Replace(body, @"\s+", " ").Trim();

        return (speaker, body);
    }

    /// <summary>Heuristic for "is this line a speaker/character name rather than dialogue?"</summary>
    private static bool LooksLikeSpeakerLabel(string line, int totalLines)
    {
        if (totalLines < 2) return false;
        if (line.Length == 0 || line.Length > 30) return false;

        // Ends with a colon - almost always a speaker label ("Geralt:", "ナルト：").
        if (line.EndsWith(':') || line.EndsWith('：')) return true;

        // Dialogue lines almost always end with sentence punctuation; a short line with none
        // of that, made of just a few words, is very likely a name rather than a sentence.
        var endsLikeSentence = Regex.IsMatch(line, @"[.!?…、。！？]\s*$");
        if (endsLikeSentence) return false;

        var wordCount = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        if (wordCount > 4) return false;

        return true;
    }

    /// <summary>Recombines a translated body with its translated speaker label for display.</summary>
    public static string Combine(string? speaker, string translatedBody)
    {
        if (string.IsNullOrWhiteSpace(speaker)) return translatedBody;
        return speaker + "\n" + translatedBody;
    }

    /// <summary>
    /// Collapses whitespace/line-break differences so two OCR reads of the *same* on-screen text
    /// compare equal even if OCR added/dropped a stray space or split lines slightly differently
    /// between frames - this is what stops the translation from flickering/changing on every poll.
    /// </summary>
    public static string NormalizeForComparison(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var collapsed = Regex.Replace(text, @"\s+", " ").Trim();
        return collapsed.ToLowerInvariant();
    }
}
