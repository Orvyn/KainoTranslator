using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ScreenTranslator.Models;

namespace ScreenTranslator.Translation;

/// <summary>
/// Yandex Cloud Translate API v2.
/// Docs: https://yandex.cloud/en/docs/translate/api-ref/Translation/translate
/// Requires an API key and a Yandex Cloud "folder ID" (both configured in Settings).
/// </summary>
public sealed class YandexTranslator : ITranslator
{
    public string Name => "Yandex Translate";

    private const string Url = "https://translate.api.cloud.yandex.net/translate/v2/translate";

    private readonly string _apiKey;
    private readonly string _folderId;
    private readonly ProxyManager _proxies;

    public YandexTranslator(string apiKey, string folderId, ProxyManager proxies)
    {
        _apiKey = apiKey;
        _folderId = folderId;
        _proxies = proxies;
    }

    public async Task<string> TranslateAsync(string text, string? sourceLanguageKey, string targetLanguageKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_apiKey) || string.IsNullOrWhiteSpace(_folderId))
            throw new InvalidOperationException("Yandex API key and Folder ID are not set. Add them in Settings > Translators > Yandex.");

        var target = LanguageCatalog.ByKey(targetLanguageKey).YandexCode
            ?? throw new NotSupportedException($"Yandex doesn't support '{targetLanguageKey}' as a target language.");
        var source = sourceLanguageKey != null ? LanguageCatalog.TryByKey(sourceLanguageKey)?.YandexCode : null;

        var payload = new
        {
            sourceLanguageCode = source,
            targetLanguageCode = target,
            format = "PLAIN_TEXT",
            texts = new[] { text },
            folderId = _folderId
        };

        var client = _proxies.GetClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, Url)
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Api-Key", _apiKey);

        using var response = await client.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Yandex Translate error {(int)response.StatusCode}: {body}");

        using var doc = JsonDocument.Parse(body);
        var translations = doc.RootElement.GetProperty("translations");
        return translations.GetArrayLength() > 0
            ? translations[0].GetProperty("text").GetString() ?? ""
            : "";
    }
}
