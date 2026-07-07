using System.Drawing;
using System.Threading;
using System.Threading.Tasks;

namespace ScreenTranslator.Ocr;

public enum OcrEngineKind
{
    WindowsOcr,  // recommended default: built into Windows, fast, no setup, GPU/NPU accelerated where available
    Tesseract,   // legacy option: works offline, needs traineddata files, generally slower/less accurate on game fonts
    EasyOcr      // legacy option: deep-learning based, needs a local Python + easyocr install, higher latency
}

public interface IOcrEngine : IAsyncDisposable
{
    /// <summary>Human readable name shown in Settings.</summary>
    string Name { get; }

    /// <summary>
    /// Recognize text in the given bitmap for the given internal language key (see LanguageCatalog).
    /// Implementations should return "" (not throw) when no text is found.
    /// </summary>
    Task<string> RecognizeAsync(Bitmap bitmap, string languageKey, CancellationToken ct);
}
