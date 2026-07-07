using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Tesseract;

namespace ScreenTranslator.Ocr;

/// <summary>
/// Legacy OCR option using the open-source Tesseract engine via the "Tesseract" NuGet wrapper.
/// Kept available for users who prefer it (offline, no Windows language packs needed) but it is
/// generally slower and less accurate on stylized game fonts than Windows OCR.
///
/// Requires .traineddata files for each language in the configured TesseractDataPath
/// (download from https://github.com/tesseract-ocr/tessdata_fast or tessdata_best).
/// </summary>
public sealed class TesseractOcrEngine : IOcrEngine
{
    public string Name => "Tesseract (legacy)";

    private readonly string _tessDataPath;
    private readonly ConcurrentDictionary<string, TesseractEngine> _engines = new();

    public TesseractOcrEngine(string tessDataPath)
    {
        _tessDataPath = tessDataPath;
    }

    public Task<string> RecognizeAsync(Bitmap bitmap, string languageKey, CancellationToken ct)
    {
        var info = Models.LanguageCatalog.ByKey(languageKey);
        if (info.TesseractCode == null)
            throw new NotSupportedException($"Tesseract has no mapping for '{info.DisplayName}'.");

        var traineddata = Path.Combine(_tessDataPath, info.TesseractCode + ".traineddata");
        if (!File.Exists(traineddata))
            throw new FileNotFoundException(
                $"Missing Tesseract language data '{info.TesseractCode}.traineddata'. " +
                $"Download it from https://github.com/tesseract-ocr/tessdata_fast and place it in '{_tessDataPath}'.",
                traineddata);

        var engine = _engines.GetOrAdd(info.TesseractCode, code =>
            new TesseractEngine(_tessDataPath, code, EngineMode.Default));

        return Task.Run(() =>
        {
            using var pix = PixConverter.ToPix(bitmap);
            using var page = engine.Process(pix);
            return page.GetText()?.Trim() ?? "";
        }, ct);
    }

    public ValueTask DisposeAsync()
    {
        foreach (var engine in _engines.Values) engine.Dispose();
        _engines.Clear();
        return ValueTask.CompletedTask;
    }
}
