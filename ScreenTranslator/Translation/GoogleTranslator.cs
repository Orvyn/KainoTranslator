using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ScreenTranslator.Models;

namespace ScreenTranslator.Translation;

/// <summary>
/// Google translator. If an API key is configured (Settings > Translators > Google), uses the
/// official Cloud Translation v2 REST API (https://cloud.google.com/translate/docs/reference/rest).
/// Otherwise falls back to Google Translate's free/unofficial "gtx" web endpoint, the same one
/// used by many free translation tools - it has no official uptime/rate-limit guarantee, which is
/// exactly the scenario the proxy list setting is meant to help with.
/// </summary>
public sealed class GoogleTranslator : ITranslator
{
    public string Name => "Google Translate";

    private readonly string _apiKey;
    private readonly ProxyManager _proxies;

    public GoogleTranslator(string apiKey, ProxyManager proxies)
    {
        _apiKey = apiKey;
        _proxies = proxies;
    }

    public Task<string> TranslateAsync(string text, string? sourceLanguageKey, string targetLanguageKey, CancellationToken ct)
    {
        return string.IsNullOrWhiteSpace(_apiKey)
            ? TranslateUnofficialAsync(text, sourceLanguageKey, targetLanguageKey, ct)
            : TranslateOfficialAsync(text, sourceLanguageKey, targetLanguageKey, ct);
    }

    private async Task<string> TranslateOfficialAsync(string text, string? sourceLanguageKey, string targetLanguageKey, CancellationToken ct)
    {
        var target = LanguageCatalog.ByKey(targetLanguageKey).GoogleCode
            ?? throw new NotSupportedException($"Google doesn't support '{targetLanguageKey}' as a target language.");
        var source = sourceLanguageKey != null ? LanguageCatalog.TryByKey(sourceLanguageKey)?.GoogleCode : null;

        var url = "https://translation.googleapis.com/language/translate/v2" +
                  $"?key={Uri.EscapeDataString(_apiKey)}" +
                  $"&q={Uri.EscapeDataString(text)}" +
                  $"&target={target}" +
                  (source != null ? $"&source={source}" : "") +
                  "&format=text";

        var client = _proxies.GetClient();
        using var response = await client.PostAsync(url, null, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Google Translate API error {(int)response.StatusCode}: {body}");

        using var doc = JsonDocument.Parse(body);
        var translations = doc.RootElement.GetProperty("data").GetProperty("translations");
        return translations.GetArrayLength() > 0
            ? translations[0].GetProperty("translatedText").GetString() ?? ""
            : "";
    }

    private async Task<string> TranslateUnofficialAsync(string text, string? sourceLanguageKey, string targetLanguageKey, CancellationToken ct)
    {
        var target = LanguageCatalog.ByKey(targetLanguageKey).GoogleCode
            ?? throw new NotSupportedException($"Google doesn't support '{targetLanguageKey}' as a target language.");
        var source = sourceLanguageKey != null ? (LanguageCatalog.TryByKey(sourceLanguageKey)?.GoogleCode ?? "auto") : "auto";

        var query = Uri.EscapeDataString(text);
        var url = "https://translate.googleapis.com/translate_a/single" +
                  $"?client=gtx&sl={source}&tl={target}&dt=t&q={query}";

        var client = _proxies.GetClient();
        using var response = await client.GetAsync(url, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"Google Translate (free endpoint) error {(int)response.StatusCode}. " +
                "This free endpoint gets rate-limited under heavy use - add proxies in Settings > Proxies, " +
                "slow down the polling interval, or switch to an API key/another translator.");

        // Response looks like: [[["translated","original",null,null,...], ...], ...]
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var sb = new System.Text.StringBuilder();
        if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
        {
            foreach (var segment in root[0].EnumerateArray())
            {
                if (segment.ValueKind == JsonValueKind.Array && segment.GetArrayLength() > 0)
                    sb.Append(segment[0].GetString());
            }
        }
        return sb.ToString();
    }
}
