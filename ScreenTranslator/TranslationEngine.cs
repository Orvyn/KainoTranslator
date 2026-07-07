using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using ScreenTranslator.Capture;
using ScreenTranslator.Models;
using ScreenTranslator.Ocr;
using ScreenTranslator.Overlay;
using ScreenTranslator.Translation;

namespace ScreenTranslator;

/// <summary>
/// Owns the live capture -> OCR -> translate -> overlay loop. Designed so a single "tick" is
/// cheap when nothing has changed on screen (the common case for subtitles/dialogue that sit
/// still for a second or more), so perceived delay comes almost entirely from the OCR +
/// translation network call, not from unnecessary re-processing.
/// </summary>
public sealed class TranslationEngine : IDisposable
{
    public event Action<string>? TextUpdated;
    public event Action<string>? ErrorOccurred;
    /// <summary>Fires whenever Start()/Stop() actually changes the running state - lets any open
    /// UI (e.g. the Settings window) stay truthfully in sync instead of guessing locally.</summary>
    public event Action<bool>? RunningStateChanged;
    public bool IsRunning { get; private set; }

    private AppSettings _settings;
    private IOcrEngine _ocrEngine;
    private ITranslator _translator;
    private CancellationTokenSource? _cts;
    private long _lastFrameHash;

    // Debounce state: a single noisy OCR read (a stray misrecognized character, extra space,
    // etc.) used to cause the displayed translation to flicker/change even though the on-screen
    // text hadn't really changed. Requiring the same (normalized) text to be read twice in a row
    // before acting on it filters that out at the cost of one extra polling interval of delay.
    private string? _pendingCandidate;
    private string _lastCommittedText = "";

    public TranslationEngine(AppSettings settings)
    {
        _settings = settings;
        _ocrEngine = OcrEngineFactory.Create(settings);
        _translator = TranslatorFactory.Create(settings);
    }

    /// <summary>Call after Settings are changed so the engine picks up new OCR/translator/API config.</summary>
    public async Task ApplySettingsAsync(AppSettings settings)
    {
        var wasRunning = IsRunning;
        if (wasRunning) Stop();

        await _ocrEngine.DisposeAsync();
        _settings = settings;
        _ocrEngine = OcrEngineFactory.Create(settings);
        _translator = TranslatorFactory.Create(settings);

        if (wasRunning) Start();
    }

    public void Start()
    {
        if (IsRunning) return;
        if (!_settings.Region.HasRegion)
        {
            ErrorOccurred?.Invoke("No capture area selected yet. Use the 'Select capture area' hotkey first.");
            return;
        }

        IsRunning = true;
        _lastFrameHash = 0;
        _lastCommittedText = "";
        _pendingCandidate = null;
        _cts = new CancellationTokenSource();
        _ = RunLoopAsync(_cts.Token);
        RunningStateChanged?.Invoke(true);
    }

    public void Stop()
    {
        if (!IsRunning) return;
        IsRunning = false;
        _cts?.Cancel();
        TextUpdated?.Invoke("");
        RunningStateChanged?.Invoke(false);
    }

    public void Toggle()
    {
        if (IsRunning) Stop(); else Start();
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var tickStart = Environment.TickCount64;
            try
            {
                await ProcessOneFrameAsync(ct);
            }
            catch (OperationCanceledException) { /* expected on stop */ }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(ex.Message);
            }

            var elapsed = Environment.TickCount64 - tickStart;
            var remaining = _settings.PollingIntervalMs - elapsed;
            if (remaining > 0)
            {
                try { await Task.Delay((int)remaining, ct); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private async Task ProcessOneFrameAsync(CancellationToken ct)
    {
        var region = new Rectangle(_settings.Region.X, _settings.Region.Y, _settings.Region.Width, _settings.Region.Height);
        using var bitmap = ScreenCapture.CaptureRegion(region);

        if (_settings.SkipOcrWhenFrameUnchanged)
        {
            var hash = ScreenCapture.QuickHash(bitmap);
            if (hash == _lastFrameHash) return; // nothing changed - skip OCR + translation entirely
            _lastFrameHash = hash;
        }

        var recognized = await _ocrEngine.RecognizeAsync(bitmap, _settings.SourceLanguageKey, ct);
        var normalized = SubtitleTextProcessor.NormalizeForComparison(recognized);

        if (normalized.Length == 0)
        {
            _pendingCandidate = null;
            if (_lastCommittedText.Length > 0)
            {
                _lastCommittedText = "";
                TextUpdated?.Invoke("");
            }
            return;
        }

        if (normalized == _lastCommittedText) return; // already showing this - nothing to do

        // Require the same reading twice in a row before committing to a (re)translation, so a
        // single noisy OCR frame doesn't make the displayed text flicker/change unnecessarily.
        if (normalized != _pendingCandidate)
        {
            _pendingCandidate = normalized;
            return;
        }

        _lastCommittedText = normalized;
        _pendingCandidate = null;

        var translated = await TranslateRecognizedTextAsync(recognized, ct);
        if (!string.IsNullOrWhiteSpace(translated)) TextUpdated?.Invoke(translated);
    }

    /// <summary>
    /// Captures + OCRs + translates a single, one-off region without touching the main capture
    /// area or the continuous loop's state - used by the "translate once" hotkey.
    /// </summary>
    public async Task<string> TranslateOnceAsync(Rectangle region, CancellationToken ct)
    {
        using var bitmap = ScreenCapture.CaptureRegion(region);
        var recognized = await _ocrEngine.RecognizeAsync(bitmap, _settings.SourceLanguageKey, ct);
        if (string.IsNullOrWhiteSpace(recognized)) return "";
        return await TranslateRecognizedTextAsync(recognized, ct);
    }

    /// <summary>Splits off a speaker/character name and translates it separately from the
    /// dialogue body (both get translated - just as two independent pieces rather than one
    /// merged sentence, so the name doesn't get folded into the sentence's grammar).</summary>
    private async Task<string> TranslateRecognizedTextAsync(string recognized, CancellationToken ct)
    {
        var (speaker, body) = SubtitleTextProcessor.SplitSpeakerAndBody(recognized);
        if (string.IsNullOrWhiteSpace(body)) return "";

        var translatedBody = await _translator.TranslateAsync(body, _settings.SourceLanguageKey, _settings.TargetLanguageKey, ct);

        string? translatedSpeaker = null;
        if (!string.IsNullOrWhiteSpace(speaker))
            translatedSpeaker = await _translator.TranslateAsync(speaker!, _settings.SourceLanguageKey, _settings.TargetLanguageKey, ct);

        return SubtitleTextProcessor.Combine(translatedSpeaker, translatedBody);
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
        _ = _ocrEngine.DisposeAsync();
    }
}
