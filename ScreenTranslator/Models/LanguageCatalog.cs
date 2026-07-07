using System.Collections.Generic;
using System.Linq;
using ScreenTranslator.Ocr;
using ScreenTranslator.Translation;

namespace ScreenTranslator.Models;

/// <summary>
/// One language entry with the specific codes each backend expects.
/// Codes were verified against each provider's current docs (DeepL v2/v3, Google Cloud
/// Translation v2, Yandex Cloud Translate v2, Naver Papago NMT v1) as of mid-2026.
/// </summary>
public sealed class LanguageInfo
{
    public required string Key { get; init; }          // internal stable id, e.g. "ja"
    public required string DisplayName { get; init; }   // shown in the UI

    public string? WindowsOcrTag { get; init; }         // BCP-47 tag for Windows.Media.Ocr / Windows.Globalization.Language
    public string? TesseractCode { get; init; }         // traineddata file name (without .traineddata)
    public string? EasyOcrCode { get; init; }           // easyocr language code

    public string? DeepLCode { get; init; }             // target code for DeepL (source codes are the same minus regional suffix)
    public string? GoogleCode { get; init; }            // Google Cloud Translation v2 code
    public string? YandexCode { get; init; }            // Yandex Cloud Translate v2 code
    public string? PapagoCode { get; init; }            // Papago NMT code

    public bool PapagoSupported { get; init; }          // Papago only covers a fixed set of language pairs
}

public static class LanguageCatalog
{
    // The five languages the user asked to prioritize come first; the rest fill out the
    // language lists published by DeepL / Google / Yandex / Papago docs.
    public static readonly IReadOnlyList<LanguageInfo> All = new List<LanguageInfo>
    {
        new() { Key = "en", DisplayName = "English",            WindowsOcrTag = "en-US", TesseractCode = "eng",     EasyOcrCode = "en", DeepLCode = "EN-US", GoogleCode = "en",    YandexCode = "en", PapagoCode = "en",    PapagoSupported = true },
        new() { Key = "ja", DisplayName = "Japanese",            WindowsOcrTag = "ja-JP", TesseractCode = "jpn",     EasyOcrCode = "ja", DeepLCode = "JA",    GoogleCode = "ja",    YandexCode = "ja", PapagoCode = "ja",    PapagoSupported = true },
        new() { Key = "zh-Hans", DisplayName = "Chinese (Simplified)", WindowsOcrTag = "zh-Hans-CN", TesseractCode = "chi_sim", EasyOcrCode = "ch_sim", DeepLCode = "ZH-HANS", GoogleCode = "zh-CN", YandexCode = "zh", PapagoCode = "zh-CN", PapagoSupported = true },
        new() { Key = "zh-Hant", DisplayName = "Chinese (Traditional)", WindowsOcrTag = "zh-Hant-TW", TesseractCode = "chi_tra", EasyOcrCode = "ch_tra", DeepLCode = "ZH-HANT", GoogleCode = "zh-TW", YandexCode = "zh", PapagoCode = "zh-TW", PapagoSupported = true },
        new() { Key = "ko", DisplayName = "Korean",              WindowsOcrTag = "ko-KR", TesseractCode = "kor",     EasyOcrCode = "ko", DeepLCode = "KO",    GoogleCode = "ko",    YandexCode = "ko", PapagoCode = "ko",    PapagoSupported = true },
        new() { Key = "ru", DisplayName = "Russian",             WindowsOcrTag = "ru-RU", TesseractCode = "rus",     EasyOcrCode = "ru", DeepLCode = "RU",    GoogleCode = "ru",    YandexCode = "ru", PapagoCode = "ru",    PapagoSupported = true },

        new() { Key = "fr", DisplayName = "French",       WindowsOcrTag = "fr-FR", TesseractCode = "fra", EasyOcrCode = "fr", DeepLCode = "FR",    GoogleCode = "fr", YandexCode = "fr", PapagoCode = "fr", PapagoSupported = true },
        new() { Key = "de", DisplayName = "German",       WindowsOcrTag = "de-DE", TesseractCode = "deu", EasyOcrCode = "de", DeepLCode = "DE",    GoogleCode = "de", YandexCode = "de", PapagoCode = "de", PapagoSupported = true },
        new() { Key = "es", DisplayName = "Spanish",      WindowsOcrTag = "es-ES", TesseractCode = "spa", EasyOcrCode = "es", DeepLCode = "ES",    GoogleCode = "es", YandexCode = "es", PapagoCode = "es", PapagoSupported = true },
        new() { Key = "it", DisplayName = "Italian",      WindowsOcrTag = "it-IT", TesseractCode = "ita", EasyOcrCode = "it", DeepLCode = "IT",    GoogleCode = "it", YandexCode = "it", PapagoCode = "it", PapagoSupported = true },
        new() { Key = "pt-BR", DisplayName = "Portuguese (Brazil)", WindowsOcrTag = "pt-BR", TesseractCode = "por", EasyOcrCode = "pt", DeepLCode = "PT-BR", GoogleCode = "pt", YandexCode = "pt", PapagoCode = "pt", PapagoSupported = false },
        new() { Key = "pt-PT", DisplayName = "Portuguese (Portugal)", WindowsOcrTag = "pt-PT", TesseractCode = "por", EasyOcrCode = "pt", DeepLCode = "PT-PT", GoogleCode = "pt", YandexCode = "pt", PapagoCode = "pt", PapagoSupported = false },
        new() { Key = "nl", DisplayName = "Dutch",        WindowsOcrTag = "nl-NL", TesseractCode = "nld", EasyOcrCode = "nl", DeepLCode = "NL", GoogleCode = "nl", YandexCode = "nl", PapagoCode = null, PapagoSupported = false },
        new() { Key = "pl", DisplayName = "Polish",       WindowsOcrTag = "pl-PL", TesseractCode = "pol", EasyOcrCode = "pl", DeepLCode = "PL", GoogleCode = "pl", YandexCode = "pl", PapagoCode = null, PapagoSupported = false },
        new() { Key = "tr", DisplayName = "Turkish",      WindowsOcrTag = "tr-TR", TesseractCode = "tur", EasyOcrCode = "tr", DeepLCode = "TR", GoogleCode = "tr", YandexCode = "tr", PapagoCode = null, PapagoSupported = false },
        new() { Key = "vi", DisplayName = "Vietnamese",   WindowsOcrTag = "vi-VN", TesseractCode = "vie", EasyOcrCode = "vi", DeepLCode = null, GoogleCode = "vi", YandexCode = "vi", PapagoCode = "vi", PapagoSupported = true },
        new() { Key = "th", DisplayName = "Thai",         WindowsOcrTag = "th-TH", TesseractCode = "tha", EasyOcrCode = "th", DeepLCode = null, GoogleCode = "th", YandexCode = "th", PapagoCode = "th", PapagoSupported = true },
        new() { Key = "id", DisplayName = "Indonesian",   WindowsOcrTag = "id-ID", TesseractCode = "ind", EasyOcrCode = "id", DeepLCode = "ID", GoogleCode = "id", YandexCode = "id", PapagoCode = "id", PapagoSupported = true },
        new() { Key = "ar", DisplayName = "Arabic",       WindowsOcrTag = "ar-SA", TesseractCode = "ara", EasyOcrCode = "ar", DeepLCode = "AR", GoogleCode = "ar", YandexCode = "ar", PapagoCode = null, PapagoSupported = false },
        new() { Key = "hi", DisplayName = "Hindi",        WindowsOcrTag = "hi-IN", TesseractCode = "hin", EasyOcrCode = "hi", DeepLCode = null, GoogleCode = "hi", YandexCode = "hi", PapagoCode = null, PapagoSupported = false },
        new() { Key = "uk", DisplayName = "Ukrainian",    WindowsOcrTag = "uk-UA", TesseractCode = "ukr", EasyOcrCode = "uk", DeepLCode = "UK", GoogleCode = "uk", YandexCode = "uk", PapagoCode = null, PapagoSupported = false },
        new() { Key = "cs", DisplayName = "Czech",        WindowsOcrTag = "cs-CZ", TesseractCode = "ces", EasyOcrCode = "cs", DeepLCode = "CS", GoogleCode = "cs", YandexCode = "cs", PapagoCode = null, PapagoSupported = false },
        new() { Key = "sv", DisplayName = "Swedish",      WindowsOcrTag = "sv-SE", TesseractCode = "swe", EasyOcrCode = "sv", DeepLCode = "SV", GoogleCode = "sv", YandexCode = "sv", PapagoCode = null, PapagoSupported = false },
        new() { Key = "da", DisplayName = "Danish",       WindowsOcrTag = "da-DK", TesseractCode = "dan", EasyOcrCode = "da", DeepLCode = "DA", GoogleCode = "da", YandexCode = "da", PapagoCode = null, PapagoSupported = false },
        new() { Key = "fi", DisplayName = "Finnish",      WindowsOcrTag = "fi-FI", TesseractCode = "fin", EasyOcrCode = "fi", DeepLCode = "FI", GoogleCode = "fi", YandexCode = "fi", PapagoCode = null, PapagoSupported = false },
        new() { Key = "nb", DisplayName = "Norwegian",    WindowsOcrTag = "nb-NO", TesseractCode = "nor", EasyOcrCode = "no", DeepLCode = "NB", GoogleCode = "no", YandexCode = "no", PapagoCode = null, PapagoSupported = false },
        new() { Key = "el", DisplayName = "Greek",        WindowsOcrTag = "el-GR", TesseractCode = "ell", EasyOcrCode = "el", DeepLCode = "EL", GoogleCode = "el", YandexCode = "el", PapagoCode = null, PapagoSupported = false },
        new() { Key = "hu", DisplayName = "Hungarian",    WindowsOcrTag = "hu-HU", TesseractCode = "hun", EasyOcrCode = "hu", DeepLCode = "HU", GoogleCode = "hu", YandexCode = "hu", PapagoCode = null, PapagoSupported = false },
        new() { Key = "ro", DisplayName = "Romanian",     WindowsOcrTag = "ro-RO", TesseractCode = "ron", EasyOcrCode = "ro", DeepLCode = "RO", GoogleCode = "ro", YandexCode = "ro", PapagoCode = null, PapagoSupported = false },
        new() { Key = "bg", DisplayName = "Bulgarian",    WindowsOcrTag = "bg-BG", TesseractCode = "bul", EasyOcrCode = "bg", DeepLCode = "BG", GoogleCode = "bg", YandexCode = "bg", PapagoCode = null, PapagoSupported = false },
        new() { Key = "sk", DisplayName = "Slovak",       WindowsOcrTag = "sk-SK", TesseractCode = "slk", EasyOcrCode = "sk", DeepLCode = "SK", GoogleCode = "sk", YandexCode = "sk", PapagoCode = null, PapagoSupported = false },
        new() { Key = "sl", DisplayName = "Slovenian",    WindowsOcrTag = "sl-SI", TesseractCode = "slv", EasyOcrCode = "sl", DeepLCode = "SL", GoogleCode = "sl", YandexCode = "sl", PapagoCode = null, PapagoSupported = false },
        new() { Key = "et", DisplayName = "Estonian",     WindowsOcrTag = "et-EE", TesseractCode = "est", EasyOcrCode = "et", DeepLCode = "ET", GoogleCode = "et", YandexCode = "et", PapagoCode = null, PapagoSupported = false },
        new() { Key = "lv", DisplayName = "Latvian",      WindowsOcrTag = "lv-LV", TesseractCode = "lav", EasyOcrCode = "lv", DeepLCode = "LV", GoogleCode = "lv", YandexCode = "lv", PapagoCode = null, PapagoSupported = false },
        new() { Key = "lt", DisplayName = "Lithuanian",   WindowsOcrTag = "lt-LT", TesseractCode = "lit", EasyOcrCode = "lt", DeepLCode = "LT", GoogleCode = "lt", YandexCode = "lt", PapagoCode = null, PapagoSupported = false },
    };

    public static LanguageInfo ByKey(string key) => All.First(l => l.Key == key);

    public static LanguageInfo? TryByKey(string key) => All.FirstOrDefault(l => l.Key == key);

    /// <summary>Languages that can be selected as an OCR *source* language for a given engine.</summary>
    public static IEnumerable<LanguageInfo> ForOcrEngine(OcrEngineKind kind) => kind switch
    {
        OcrEngineKind.WindowsOcr => All.Where(l => l.WindowsOcrTag != null),
        OcrEngineKind.Tesseract => All.Where(l => l.TesseractCode != null),
        OcrEngineKind.EasyOcr => All.Where(l => l.EasyOcrCode != null),
        _ => All
    };

    /// <summary>Languages that can be selected as a translation *target* for a given translator.</summary>
    public static IEnumerable<LanguageInfo> ForTranslator(TranslatorKind kind) => kind switch
    {
        TranslatorKind.DeepL => All.Where(l => l.DeepLCode != null),
        TranslatorKind.Google => All.Where(l => l.GoogleCode != null),
        TranslatorKind.Yandex => All.Where(l => l.YandexCode != null),
        TranslatorKind.Papago => All.Where(l => l.PapagoSupported),
        _ => All
    };
}
