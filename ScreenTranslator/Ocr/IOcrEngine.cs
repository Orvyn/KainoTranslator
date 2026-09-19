using System.Collections.Generic;
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

/// <summary>One recognized line of text with its bounding box in the recognized bitmap's pixels.</summary>
public sealed record OcrTextLine(string Text, Rectangle Bounds);

public interface IOcrEngine : IAsyncDisposable
{
    /// <summary>Human readable name shown in Settings.</summary>
    string Name { get; }

    /// <summary>
    /// Recognize text in the given bitmap for the given internal language key (see LanguageCatalog).
    /// Implementations should return "" (not throw) when no text is found.
    /// </summary>
    Task<string> RecognizeAsync(Bitmap bitmap, string languageKey, CancellationToken ct);

    /// <summary>True if <see cref="RecognizeLinesAsync"/> reports line positions - lets callers
    /// find the block of text around a point instead of taking everything in the bitmap.</summary>
    bool ProvidesLineBounds => false;

    /// <summary>
    /// Like <see cref="RecognizeAsync"/> but keeps each line's position. Returns null (the default)
    /// for engines that only produce plain text.
    /// </summary>
    Task<IReadOnlyList<OcrTextLine>?> RecognizeLinesAsync(Bitmap bitmap, string languageKey, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<OcrTextLine>?>(null);
}
