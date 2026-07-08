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

    /// <summary>
    /// 0 (completely different) to 1 (identical) similarity between two already-normalized
    /// strings, tolerant of minor OCR noise (a misread character, a dropped space, etc.) -
    /// used instead of strict equality so two near-identical OCR reads of the same on-screen
    /// text are treated as "the same" even if OCR didn't read them byte-for-byte identically.
    /// </summary>
    public static double Similarity(string a, string b)
    {
        if (a.Length == 0 && b.Length == 0) return 1;
        if (a.Length == 0 || b.Length == 0) return 0;
        if (a == b) return 1;

        var distance = LevenshteinDistance(a, b);
        var maxLen = Math.Max(a.Length, b.Length);
        return 1.0 - (double)distance / maxLen;
    }

    /// <summary>
    /// True if one string looks like an in-progress extension of the other - the signature of a
    /// "typewriter" subtitle effect where each poll captures a bit more text than the last.
    /// </summary>
    public static bool IsGrowth(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0) return false;
        var shorter = a.Length <= b.Length ? a : b;
        var longer = a.Length <= b.Length ? b : a;
        if (longer.StartsWith(shorter, StringComparison.Ordinal)) return true;

        // Tolerate a bit of OCR noise in the already-typed prefix too.
        var prefixOfLonger = longer[..Math.Min(shorter.Length, longer.Length)];
        return Similarity(prefixOfLonger, shorter) >= 0.85;
    }

    private static int LevenshteinDistance(string a, string b)
    {
        var dp = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) dp[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) dp[0, j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                dp[i, j] = Math.Min(Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1), dp[i - 1, j - 1] + cost);
            }
        }
        return dp[a.Length, b.Length];
    }
}
