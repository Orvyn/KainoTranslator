using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ScreenTranslator.Models;

namespace ScreenTranslator.Translation;

/// <summary>
/// DeepL API v2 translator. Endpoint auto-selected based on key format is *not* done here -
/// instead we honor the explicit "use Pro endpoint" setting, since free keys end in ":fx" and
/// work only against api-free.deepl.com while Pro keys work against api.deepl.com.
/// Docs: https://developers.deepl.com/api-reference/translate
/// </summary>
public sealed class DeepLTranslator : ITranslator
{
    public string Name => "DeepL";

    private readonly string _apiKey;
    private readonly bool _usePro;
    private readonly ProxyManager _proxies;

    public DeepLTranslator(string apiKey, bool usePro, ProxyManager proxies)
    {
        _apiKey = apiKey;
        _usePro = usePro;
        _proxies = proxies;
    }

    public async Task<string> TranslateAsync(string text, string? sourceLanguageKey, string targetLanguageKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
            throw new InvalidOperationException("DeepL API key is not set. Add it in Settings > Translators > DeepL.");

        var target = LanguageCatalog.ByKey(targetLanguageKey).DeepLCode
            ?? throw new NotSupportedException($"DeepL doesn't support '{targetLanguageKey}' as a target language.");

        string? source = null;
        if (sourceLanguageKey != null)
        {
            var info = LanguageCatalog.TryByKey(sourceLanguageKey);
            // DeepL source codes are the base language without regional variant (e.g. "EN" not "EN-US").
            source = info?.DeepLCode?.Split('-')[0];
        }

        var host = _usePro ? "api.deepl.com" : "api-free.deepl.com";
        var url = $"https://{host}/v2/translate";

        var payload = new Dictionary<string, object?>
        {
            ["text"] = new[] { text },
            ["target_lang"] = target,
        };
        if (source != null) payload["source_lang"] = source;

        var client = _proxies.GetClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("DeepL-Auth-Key", _apiKey);

        using var response = await client.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"DeepL error {(int)response.StatusCode}: {body}");

        var result = JsonSerializer.Deserialize<DeepLResponse>(body, JsonOpts);
        return result?.Translations?.Count > 0 ? result.Translations[0].Text : "";
    }

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private sealed class DeepLResponse
    {
        [JsonPropertyName("translations")]
        public List<DeepLTranslation>? Translations { get; set; }
    }

    private sealed class DeepLTranslation
    {
        [JsonPropertyName("text")]
        public string Text { get; set; } = "";
        [JsonPropertyName("detected_source_language")]
        public string? DetectedSourceLanguage { get; set; }
    }
}
