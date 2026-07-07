using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace ScreenTranslator.Ocr;

/// <summary>
/// Recommended, default OCR engine. Uses the OCR that ships with Windows 10/11
/// (Windows.Media.Ocr). No downloads, no models to manage, very fast, and it uses the
/// language packs already installed under Settings > Time & Language > Language &amp; region.
///
/// If a language isn't recognized, the user needs to install its optional "Language pack" /
/// "handwriting and OCR" feature from Windows Settings - this class surfaces a clear error
/// telling them exactly that instead of failing silently.
/// </summary>
public sealed class WindowsOcrEngine : IOcrEngine
{
    public string Name => "Windows OCR (recommended)";

    public static bool IsLanguageAvailable(string bcp47Tag)
    {
        try
        {
            var lang = new Language(bcp47Tag);
            return OcrEngine.IsLanguageSupported(lang) &&
                   OcrEngine.AvailableRecognizerLanguages.Any(l => l.LanguageTag.Equals(lang.LanguageTag, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    public async Task<string> RecognizeAsync(Bitmap bitmap, string languageKey, CancellationToken ct)
    {
        var info = Models.LanguageCatalog.ByKey(languageKey);
        if (info.WindowsOcrTag == null)
            throw new NotSupportedException($"Windows OCR has no mapping for '{info.DisplayName}'.");

        var language = new Language(info.WindowsOcrTag);
        var engine = OcrEngine.TryCreateFromLanguage(language)
            ?? throw new InvalidOperationException(
                $"The Windows OCR language pack for '{info.DisplayName}' ({info.WindowsOcrTag}) is not installed. " +
                "Install it from Settings > Time & Language > Language & region > Add a language " +
                "(enable the \"Optical character recognition\" feature for that language), then restart the app.");

        using var softwareBitmap = await ConvertToSoftwareBitmapAsync(bitmap, ct);
        var result = await engine.RecognizeAsync(softwareBitmap).AsTask(ct);

        // Windows OCR returns text line-by-line; join with spaces/newlines to keep subtitle
        // blocks readable while still separating clearly distinct lines.
        var sb = new StringBuilder();
        foreach (var line in result.Lines)
        {
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(line.Text);
        }
        return sb.ToString().Trim();
    }

    private static async Task<SoftwareBitmap> ConvertToSoftwareBitmapAsync(Bitmap bitmap, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Png);
        ms.Position = 0;

        using IRandomAccessStream stream = ms.AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(stream);
        var softwareBitmap = await decoder.GetSoftwareBitmapAsync();

        // Windows OCR requires Bgra8 + Premultiplied (or Straight) alpha.
        if (softwareBitmap.BitmapPixelFormat != BitmapPixelFormat.Bgra8 ||
            softwareBitmap.BitmapAlphaMode == BitmapAlphaMode.Straight)
        {
            var converted = SoftwareBitmap.Convert(softwareBitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            softwareBitmap.Dispose();
            return converted;
        }
        return softwareBitmap;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
