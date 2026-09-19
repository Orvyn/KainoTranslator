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

    // Stability tracking. The goal is to tell apart two very different situations from the same
    // stream of OCR reads:
    //  - A "typewriter" subtitle that grows a little more with every poll: we want to wait for
    //    it to *stop* growing before translating, so the translation doesn't flicker through
    //    every partial sentence on the way.
    //  - A cutscene line that appears already complete and may only be on screen for a second
    //    or two: if it vanishes before a second, confirming read ever happens, we still
    //    translate what we saw rather than dropping it - but only that specific "it disappeared"
    //    case. If a reading is instead simply *replaced* by something else before being
    //    confirmed, it's discarded rather than translated, since that's usually a one-off OCR
    //    misread (e.g. a fade transition) - translating it tended to show garbled or stale text.
    private string? _pendingRawText;
    private string? _pendingNormalized;
    private bool _pendingWasGrowing;
    private bool _pendingUsedGrowthPauseGrace;
    private string _lastCommittedNormalized = "";
    private long _lastShownAtTicks;

    private const double StableSimilarity = 0.92; // treat near-identical OCR reads as "the same"
    private const int MinDisplayMs = 1200; // keep a short-lived translation on screen at least this long

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
        _lastCommittedNormalized = "";
        _pendingRawText = null;
        _pendingNormalized = null;
        _pendingWasGrowing = false;
        _pendingUsedGrowthPauseGrace = false;
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
            if (hash == _lastFrameHash)
            {
                // The screen hasn't visibly changed since the last poll. If a candidate was
                // waiting on a confirming re-read, an unchanged image *is* that confirmation -
                // running OCR again would just read the same text, so commit straight away
                // instead of silently skipping (which used to leave it stuck unconfirmed
                // forever whenever the on-screen text had zero background animation to ever
                // change the frame hash again). Exception: if we actually watched this text
                // growing (a typewriter reveal in progress), a brief pause might just be a
                // natural beat in the reveal rather than the end of it - give that specific case
                // one extra poll before locking it in.
                if (_pendingNormalized != null)
                {
                    if (_pendingWasGrowing && !_pendingUsedGrowthPauseGrace)
                    {
                        _pendingUsedGrowthPauseGrace = true;
                        return;
                    }

                    var toCommit = _pendingRawText!;
                    _pendingRawText = null;
                    _pendingNormalized = null;
                    _pendingWasGrowing = false;
                    _pendingUsedGrowthPauseGrace = false;
                    if (SubtitleTextProcessor.LooksPlausible(toCommit))
                        await CommitAsync(toCommit, ct);
                }
                return;
            }
            _lastFrameHash = hash;
        }

        var recognized = await _ocrEngine.RecognizeAsync(bitmap, _settings.SourceLanguageKey, ct);
        var normalized = SubtitleTextProcessor.NormalizeForComparison(recognized);

        // --- Nothing on screen right now ---
        if (normalized.Length == 0)
        {
            if (_pendingNormalized != null)
            {
                // Something was mid-confirmation and just disappeared (a short cutscene flash).
                // Translate what we had rather than silently dropping it - but only if it looks
                // like real text and not a one-off OCR misread (e.g. from a fade transition).
                var toCommit = _pendingRawText!;
                _pendingRawText = null;
                _pendingNormalized = null;
                _pendingWasGrowing = false;
                _pendingUsedGrowthPauseGrace = false;
                if (SubtitleTextProcessor.LooksPlausible(toCommit))
                    await CommitAsync(toCommit, ct);
                return;
            }

            if (_lastCommittedNormalized.Length > 0 && ElapsedSinceShownMs() >= MinDisplayMs)
            {
                _lastCommittedNormalized = "";
                TextUpdated?.Invoke("");
            }
            return;
        }

        // Already showing (near enough) this exact text - nothing to do (and drop any stale
        // pending candidate left over from before the screen briefly matched old text again).
        if (SubtitleTextProcessor.Similarity(normalized, _lastCommittedNormalized) >= StableSimilarity)
        {
            _pendingRawText = null;
            _pendingNormalized = null;
            _pendingWasGrowing = false;
            _pendingUsedGrowthPauseGrace = false;
            return;
        }

        // First time seeing this particular reading since the last commit - hold it for one
        // more poll to see whether it's still being typed out before acting on it.
        if (_pendingNormalized == null)
        {
            _pendingRawText = recognized;
            _pendingNormalized = normalized;
            _pendingWasGrowing = false;
            _pendingUsedGrowthPauseGrace = false;
            return;
        }

        var similarityToPending = SubtitleTextProcessor.Similarity(normalized, _pendingNormalized);
        if (similarityToPending >= StableSimilarity)
        {
            // Stable twice in a row. If we actually watched this grow (typewriter in progress),
            // give it one grace poll in case this is just a natural pause mid-reveal rather than
            // the end of it - otherwise (text appeared already-formed, e.g. a cutscene line)
            // there's no such evidence of more to come, so translate immediately as before.
            if (_pendingWasGrowing && !_pendingUsedGrowthPauseGrace)
            {
                _pendingUsedGrowthPauseGrace = true;
                _pendingRawText = recognized;
                _pendingNormalized = normalized;
                return;
            }

            _pendingRawText = null;
            _pendingNormalized = null;
            _pendingWasGrowing = false;
            _pendingUsedGrowthPauseGrace = false;
            await CommitAsync(recognized, ct);
            return;
        }

        if (SubtitleTextProcessor.IsGrowth(normalized, _pendingNormalized))
        {
            // Still being typed out - keep waiting, remembering the latest (longest) reading.
            _pendingRawText = recognized;
            _pendingNormalized = normalized;
            _pendingWasGrowing = true;
            _pendingUsedGrowthPauseGrace = false;
            return;
        }

        // What we were waiting on got replaced by something unrelated before it was ever
        // confirmed - likely a one-off misread (e.g. mid fade-in/out), or the game has already
        // moved on. A single, unconfirmed OCR read is unreliable enough (garbled characters,
        // missing words) that translating it did more harm than good even as an opt-in - so it's
        // always discarded here, and we just start tracking the new reading fresh instead.
        _pendingRawText = recognized;
        _pendingNormalized = normalized;
        _pendingWasGrowing = false;
        _pendingUsedGrowthPauseGrace = false;
    }

    private long ElapsedSinceShownMs() => Environment.TickCount64 - _lastShownAtTicks;

    private async Task CommitAsync(string raw, CancellationToken ct)
    {
        var normalized = SubtitleTextProcessor.NormalizeForComparison(raw);
        if (SubtitleTextProcessor.Similarity(normalized, _lastCommittedNormalized) >= StableSimilarity) return;
        _lastCommittedNormalized = normalized;

        var translated = await TranslateRecognizedTextAsync(raw, ct);
        if (!string.IsNullOrWhiteSpace(translated))
        {
            _lastShownAtTicks = Environment.TickCount64;
            TextUpdated?.Invoke(translated);
        }
    }

    /// <summary>
    /// Re-captures and re-translates the main capture area right now, deliberately bypassing the
    /// "already showing this text" dedup in CommitAsync - the whole point is to retry when the
    /// translator returned something garbled while the same source text is still on screen.
    /// Updates the same tracking fields the continuous loop uses, so polling resumes normally
    /// afterwards instead of immediately re-triggering on its own next tick.
    /// </summary>
    public async Task RetranslateCurrentAsync(CancellationToken ct)
    {
        var region = new Rectangle(_settings.Region.X, _settings.Region.Y, _settings.Region.Width, _settings.Region.Height);
        using var bitmap = ScreenCapture.CaptureRegion(region);
        var recognized = await _ocrEngine.RecognizeAsync(bitmap, _settings.SourceLanguageKey, ct);
        if (string.IsNullOrWhiteSpace(recognized)) return;

        var translated = await TranslateRecognizedTextAsync(recognized, ct);
        if (string.IsNullOrWhiteSpace(translated)) return;

        _lastCommittedNormalized = SubtitleTextProcessor.NormalizeForComparison(recognized);
        _lastShownAtTicks = Environment.TickCount64;
        TextUpdated?.Invoke(translated);
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

        var speakerEntry = GlossaryProcessor.FindSpeakerEntry(speaker, _settings.ActiveGlossaryEntries);

        var protectedBody = GlossaryProcessor.ProtectTerms(body, _settings.ActiveGlossaryEntries, out var restoreList);
        var translatedBody = await _translator.TranslateAsync(protectedBody, _settings.SourceLanguageKey, _settings.TargetLanguageKey, ct);
        translatedBody = GlossaryProcessor.RestoreTerms(translatedBody, restoreList);

        string? translatedSpeaker = null;
        if (!string.IsNullOrWhiteSpace(speaker))
        {
            translatedSpeaker = speakerEntry != null && !string.IsNullOrWhiteSpace(speakerEntry.TargetTerm)
                ? speakerEntry.TargetTerm
                : await _translator.TranslateAsync(speaker!, _settings.SourceLanguageKey, _settings.TargetLanguageKey, ct);
        }

        return SubtitleTextProcessor.Combine(translatedSpeaker, translatedBody);
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
        _ = _ocrEngine.DisposeAsync();
    }
}
