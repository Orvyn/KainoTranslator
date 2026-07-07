using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ScreenTranslator.Ocr;

/// <summary>
/// Legacy OCR option. EasyOCR is a Python/PyTorch library, so this engine writes the
/// captured region to a temp PNG and calls a small worker script (Python\easyocr_worker.py)
/// once per recognition. It is the slowest of the three engines (model + Python startup
/// overhead), but can be more accurate on some fonts/handwriting.
///
/// Requires: Python 3.9+ with `pip install easyocr` available at EasyOcrPythonExePath.
/// The worker script keeps a small stdin/stdout loop so the (slow) model load only happens
/// once per app session rather than once per frame.
/// </summary>
public sealed class EasyOcrEngine : IOcrEngine
{
    public string Name => "EasyOCR (legacy)";

    private readonly string _pythonExe;
    private readonly string _workerScript;
    private readonly bool _useGpu;
    private Process? _worker;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public EasyOcrEngine(string pythonExe, string workerScript, bool useGpu)
    {
        _pythonExe = pythonExe;
        _workerScript = workerScript;
        _useGpu = useGpu;
    }

    private async Task EnsureWorkerAsync(string easyOcrLangCode, CancellationToken ct)
    {
        if (_worker is { HasExited: false }) return;

        if (!File.Exists(_workerScript))
            throw new FileNotFoundException(
                $"EasyOCR worker script not found at '{_workerScript}'. Reinstall the app or restore the Python\\easyocr_worker.py file.",
                _workerScript);

        var psi = new ProcessStartInfo
        {
            FileName = _pythonExe,
            Arguments = $"\"{_workerScript}\" --lang {easyOcrLangCode} {(_useGpu ? "--gpu" : "")}",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardInputEncoding = Encoding.UTF8,
        };

        try
        {
            _worker = Process.Start(psi);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Couldn't launch Python at '{_pythonExe}'. Install Python 3.9+, run 'pip install easyocr', " +
                "and set the correct path in Settings > OCR > EasyOCR Python path.", ex);
        }

        // Wait for the "READY" handshake line so callers don't send frames before the model loaded.
        var readyLine = await _worker!.StandardOutput.ReadLineAsync(ct);
        if (readyLine != "READY")
            throw new InvalidOperationException($"EasyOCR worker failed to start: {readyLine}");
    }

    public async Task<string> RecognizeAsync(Bitmap bitmap, string languageKey, CancellationToken ct)
    {
        var info = Models.LanguageCatalog.ByKey(languageKey);
        if (info.EasyOcrCode == null)
            throw new NotSupportedException($"EasyOCR has no mapping for '{info.DisplayName}'.");

        await _lock.WaitAsync(ct);
        try
        {
            await EnsureWorkerAsync(info.EasyOcrCode, ct);

            var tempFile = Path.Combine(Path.GetTempPath(), $"st_easyocr_{Guid.NewGuid():N}.png");
            bitmap.Save(tempFile, ImageFormat.Png);

            try
            {
                await _worker!.StandardInput.WriteLineAsync(tempFile);
                await _worker.StandardInput.FlushAsync();

                var resultLine = await _worker.StandardOutput.ReadLineAsync(ct);
                if (resultLine == null) throw new InvalidOperationException("EasyOCR worker closed unexpectedly.");

                using var doc = JsonDocument.Parse(resultLine);
                var sb = new StringBuilder();
                foreach (var line in doc.RootElement.GetProperty("lines").EnumerateArray())
                {
                    if (sb.Length > 0) sb.Append('\n');
                    sb.Append(line.GetString());
                }
                return sb.ToString().Trim();
            }
            finally
            {
                try { File.Delete(tempFile); } catch { /* best effort */ }
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            if (_worker is { HasExited: false })
            {
                _worker.StandardInput.WriteLine("EXIT");
                _worker.WaitForExit(2000);
                if (!_worker.HasExited) _worker.Kill();
            }
        }
        catch { /* best effort cleanup */ }
        return ValueTask.CompletedTask;
    }
}
