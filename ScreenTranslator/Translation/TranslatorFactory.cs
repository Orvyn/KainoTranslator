using ScreenTranslator.Models;

namespace ScreenTranslator.Translation;

public static class TranslatorFactory
{
    public static ITranslator Create(AppSettings settings)
    {
        var proxies = new ProxyManager(settings.ProxyList, settings.RotateProxyPerRequest);
        return settings.Translator switch
        {
            TranslatorKind.DeepL => new DeepLTranslator(settings.ApiKeys.DeepLApiKey, settings.ApiKeys.DeepLUseProEndpoint, proxies),
            TranslatorKind.Google => new GoogleTranslator(settings.ApiKeys.GoogleApiKey, proxies),
            TranslatorKind.Yandex => new YandexTranslator(settings.ApiKeys.YandexApiKey, settings.ApiKeys.YandexFolderId, proxies),
            TranslatorKind.Papago => new PapagoTranslator(settings.ApiKeys.PapagoClientId, settings.ApiKeys.PapagoClientSecret, proxies),
            _ => new DeepLTranslator(settings.ApiKeys.DeepLApiKey, settings.ApiKeys.DeepLUseProEndpoint, proxies)
        };
    }
}
