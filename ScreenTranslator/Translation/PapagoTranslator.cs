using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ScreenTranslator.Models;

namespace ScreenTranslator.Translation;

/// <summary>
/// Naver Papago NMT translator (NAVER Cloud Platform).
/// Docs: https://api.ncloud-docs.com/docs/en/ai-naver-papagonmt-translation
/// Requires a Client ID / Client Secret pair from the NCP console (Settings > Translators > Papago).
///
/// Papago only supports a fixed set of language pairs (mostly routed through Korean/English/
/// Japanese/Chinese as hubs) - see LanguageCatalog.PapagoSupported / LanguageCatalog.ForTranslator.
/// </summary>
public sealed class PapagoTranslator : ITranslator
{
    public string Name => "Papago";

    private const string Url = "https://papago.apigw.ntruss.com/nmt/v1/translation";

    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly ProxyManager _proxies;

    public PapagoTranslator(string clientId, string clientSecret, ProxyManager proxies)
    {
        _clientId = clientId;
        _clientSecret = clientSecret;
        _proxies = proxies;
    }

    public async Task<string> TranslateAsync(string text, string? sourceLanguageKey, string targetLanguageKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_clientId) || string.IsNullOrWhiteSpace(_clientSecret))
            throw new InvalidOperationException("Papago Client ID/Secret are not set. Add them in Settings > Translators > Papago.");

        var targetInfo = LanguageCatalog.ByKey(targetLanguageKey);
        var target = targetInfo.PapagoCode
            ?? throw new NotSupportedException($"Papago doesn't support '{targetInfo.DisplayName}' as a target language.");

        string source = "auto";
        if (sourceLanguageKey != null)
        {
            var sourceInfo = LanguageCatalog.TryByKey(sourceLanguageKey);
            if (sourceInfo?.PapagoCode != null) source = sourceInfo.PapagoCode;
        }

        var form = new Dictionary<string, string>
        {
            ["source"] = source,
            ["target"] = target,
            ["text"] = text
        };

        var client = _proxies.GetClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, Url)
        {
            Content = new FormUrlEncodedContent(form)
        };
        request.Headers.Add("X-NCP-APIGW-API-KEY-ID", _clientId);
        request.Headers.Add("X-NCP-APIGW-API-KEY", _clientSecret);

        using var response = await client.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"Papago error {(int)response.StatusCode}: {body}. " +
                "Note Papago only supports translation pairs routed through Korean/English/Japanese/Chinese - " +
                "check https://api.ncloud-docs.com/docs/en/ai-naver-papagonmt for the current supported pairs.");

        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("message").GetProperty("result").GetProperty("translatedText").GetString() ?? "";
    }
}
