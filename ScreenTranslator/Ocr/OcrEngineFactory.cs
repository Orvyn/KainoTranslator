using ScreenTranslator.Models;

namespace ScreenTranslator.Ocr;

public static class OcrEngineFactory
{
    public static IOcrEngine Create(AppSettings settings) => settings.OcrEngine switch
    {
        OcrEngineKind.WindowsOcr => new WindowsOcrEngine(),
        OcrEngineKind.Tesseract => new TesseractOcrEngine(settings.TesseractDataPath),
        OcrEngineKind.EasyOcr => new EasyOcrEngine(settings.EasyOcrPythonExePath, settings.EasyOcrWorkerScriptPath, settings.EasyOcrUseGpu),
        _ => new WindowsOcrEngine()
    };
}
