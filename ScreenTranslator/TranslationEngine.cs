using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
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
    /// <summary>
    /// Replace-text display mode (see <see cref="ReplaceModeActive"/>): a frame with the translated
    /// blocks to draw over the source text, or null to remove what's drawn. The receiver owns the
    /// frame and disposes it.
    /// </summary>
    public event Action<ReplacementFrame?>? ReplacementUpdated;
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

    // Replace-text mode: the newest OCR read that found text, with the screenshot and line
    // positions it came from - what a committed translation gets drawn over. Only the loop touches it.
    private FrameSnapshot? _latestFrame;
    private bool _replacementShown;
    private long _emptySinceTicks; // when the source text was first seen missing (0 = it isn't)

    private readonly Dictionary<string, (string? Speaker, string Body)> _blockCache = new();
    private readonly object _cacheLock = new();

    private const double StableSimilarity = 0.92; // treat near-identical OCR reads as "the same"
    private const int MinDisplayMs = 1200; // keep a short-lived translation on screen at least this long

    // Replace-text mode: how long the source text has to stay missing before we believe it is really
    // gone. A single empty OCR read is usually a miss on an animated background, and taking the
    // translation down for it made the overlay blink.
    private const int EmptyHideMs = 1500;
    // Replace-text mode: how long a translation may take before the previous one is taken down.
    // A fast translator swaps the old text for the new in one go; a slow one (an AI model) would
    // otherwise leave the old translation sitting over the new line for seconds.
    private const int HideOldAfterMs = 250;
    private const double CacheSimilarity = 0.88; // a block read slightly differently reuses its translation
    private const int MaxBlocksPerFrame = 6;
    // Replace-text mode: a newly confirmed text this similar to the one on screen is treated as the
    // same line re-read slightly differently, and swapped in without taking the old one down first.
    private const double SameLineSimilarity = 0.6;

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
        lock (_cacheLock) _blockCache.Clear();
        _cts = new CancellationTokenSource();
        _ = RunLoopAsync(_cts.Token);
        RunningStateChanged?.Invoke(true);
    }

    public void Stop()
    {
        if (!IsRunning) return;
        IsRunning = false;
        _cts?.Cancel();
        _replacementShown = false;
        TextUpdated?.Invoke("");
        ReplacementUpdated?.Invoke(null);
        RunningStateChanged?.Invoke(false);
    }

    public void Toggle()
    {
        if (IsRunning) Stop(); else Start();
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        var consecutiveErrors = 0;
        while (!ct.IsCancellationRequested)
        {
            var tickStart = Environment.TickCount64;
            var failed = false;
            try
            {
                await ProcessOneFrameAsync(ct);
                consecutiveErrors = 0;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { /* expected on stop */ }
            catch (Exception ex)
            {
                // Includes a translator's own timeout, which HttpClient reports as a cancellation -
                // that used to be swallowed here as if the user had stopped, dropping the line silently.
                failed = true;
                consecutiveErrors++;
                ErrorOccurred?.Invoke(ex.Message);
            }

            var elapsed = Environment.TickCount64 - tickStart;
            var remaining = _settings.PollingIntervalMs - elapsed;

            // After a failure (a translator rate-limiting us, no network...) wait longer each time
            // instead of hammering the same service every poll - which only makes a rate limit last.
            if (failed)
                remaining = Math.Max(remaining, Math.Min(30000, 2000L << Math.Min(consecutiveErrors - 1, 4)));
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

        string recognized;
        if (ReplaceModeActive)
        {
            // Same text as RecognizeAsync would give, but keeping where each line is, so the
            // translation can be drawn over it.
            var lines = await _ocrEngine.RecognizeLinesAsync(bitmap, _settings.SourceLanguageKey, ct) ?? Array.Empty<OcrTextLine>();
            recognized = string.Join("\n", lines.Select(l => l.Text)).Trim();
            SetLatestFrame(lines.Count > 0 ? new FrameSnapshot((Bitmap)bitmap.Clone(), region.Location, lines) : null);
        }
        else
        {
            recognized = await _ocrEngine.RecognizeAsync(bitmap, _settings.SourceLanguageKey, ct);
        }
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
                    await CommitAsync(toCommit, ct, stillOnScreen: false);
                return;
            }

            // In replace mode the translation sits on top of the source text, so it has to go once
            // the source does - a blurred patch hanging in mid-air looks broken - but only after
            // several empty reads in a row, not the first one.
            if (_lastCommittedNormalized.Length > 0)
            {
                bool gone;
                if (ReplaceModeActive)
                {
                    if (_emptySinceTicks == 0) _emptySinceTicks = Environment.TickCount64;
                    gone = Environment.TickCount64 - _emptySinceTicks >= EmptyHideMs;
                }
                else
                {
                    gone = ElapsedSinceShownMs() >= MinDisplayMs;
                }
                if (gone)
                {
                    _emptySinceTicks = 0;
                    _lastCommittedNormalized = "";
                    _replacementShown = false;
                    TextUpdated?.Invoke("");
                    ReplacementUpdated?.Invoke(null);
                }
            }
            return;
        }
        _emptySinceTicks = 0;

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

    private async Task CommitAsync(string raw, CancellationToken ct, bool stillOnScreen = true)
    {
        var normalized = SubtitleTextProcessor.NormalizeForComparison(raw);
        if (SubtitleTextProcessor.Similarity(normalized, _lastCommittedNormalized) >= StableSimilarity) return;

        if (ReplaceModeActive)
        {
            // A translation drawn over text that's already gone would just be a patch of
            // background with words on it.
            if (stillOnScreen) await CommitReplacementAsync(normalized, ct);
            return;
        }

        _lastCommittedNormalized = normalized;

        string translated;
        try
        {
            translated = await TranslateRecognizedTextAsync(raw, ct);
        }
        catch
        {
            // The translator failed (timeout, rate limit...): forget that this text was handled, so it
            // is tried again - the loop waits longer after each failure, so that can't turn into
            // hammering the service. Before, such a line was never translated at all.
            _lastCommittedNormalized = "";
            throw;
        }
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

        if (ReplaceModeActive)
        {
            var lines = await _ocrEngine.RecognizeLinesAsync(bitmap, _settings.SourceLanguageKey, ct);
            if (lines is null || lines.Count == 0) return;

            var frame = await BuildReplacementAsync((Bitmap)bitmap.Clone(), region.Location, TextBlockLocator.GroupIntoBlocks(lines), bypassCache: true, ct);
            if (frame is null) return;

            _lastCommittedNormalized = SubtitleTextProcessor.NormalizeForComparison(string.Join("\n", lines.Select(l => l.Text)));
            _lastShownAtTicks = Environment.TickCount64;
            _replacementShown = true;
            ReplacementUpdated?.Invoke(frame);
            return;
        }

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

    /// <summary>True when the current OCR engine reports where each line is, i.e.
    /// <see cref="TranslateBlockAtAsync"/> can find the text block itself.</summary>
    public bool SupportsBlockDetection => _ocrEngine.ProvidesLineBounds;

    /// <summary>
    /// Captures <paramref name="band"/>, finds the block of text <paramref name="point"/> is on and
    /// translates just that block - so the result doesn't depend on how wide or tall the text
    /// happens to be. Returns where the block is on screen (for placing the overlay), or null
    /// when the OCR engine can't report line positions and the whole band was translated instead.
    /// An empty translation means no text was near the point.
    /// </summary>
    public async Task<(string Translated, Rectangle? SourceBounds)> TranslateBlockAtAsync(
        Rectangle band, System.Drawing.Point point, CancellationToken ct)
    {
        using var bitmap = ScreenCapture.CaptureRegion(band);
        var lines = await _ocrEngine.RecognizeLinesAsync(bitmap, _settings.SourceLanguageKey, ct);

        if (lines is null)
        {
            var recognized = await _ocrEngine.RecognizeAsync(bitmap, _settings.SourceLanguageKey, ct);
            if (string.IsNullOrWhiteSpace(recognized)) return ("", null);
            return (await TranslateRecognizedTextAsync(recognized, ct), null);
        }

        var block = TextBlockLocator.FindBlockAt(lines, new System.Drawing.Point(point.X - band.X, point.Y - band.Y));
        if (block.Count == 0) return ("", null);

        var text = string.Join("\n", block.Select(l => l.Text));
        var bounds = block.Select(l => l.Bounds).Aggregate(Rectangle.Union);
        bounds.Offset(band.Location);
        return (await TranslateRecognizedTextAsync(text, ct), bounds);
    }

    // ---------------------------------------------------------------------------------------
    // Replace-text display mode: the source text is blurred out and the translation is drawn in
    // its place. Everything below is the engine's side of that; the drawing is in Overlay/.
    // ---------------------------------------------------------------------------------------

    /// <summary>True when translations should be drawn over the source text instead of shown in the
    /// panel: chosen in Settings, and possible - the OCR engine has to report where each line is
    /// (only Windows OCR does) and Windows has to be able to hide the overlay from screen capture.</summary>
    public bool ReplaceModeActive =>
        _settings.Overlay.DisplayMode == OverlayDisplayMode.ReplaceText
        && _ocrEngine.ProvidesLineBounds
        && ReplaceOverlayWindow.IsSupported;

    /// <summary>Replace-text version of <see cref="TranslateOnceAsync"/>: every block of text in the
    /// region, translated. Null when no text was found.</summary>
    public async Task<ReplacementFrame?> TranslateOnceReplaceAsync(Rectangle region, CancellationToken ct)
    {
        using var bitmap = ScreenCapture.CaptureRegion(region);
        var lines = await _ocrEngine.RecognizeLinesAsync(bitmap, _settings.SourceLanguageKey, ct);
        if (lines is null || lines.Count == 0) return null;
        return await BuildReplacementAsync((Bitmap)bitmap.Clone(), region.Location, TextBlockLocator.GroupIntoBlocks(lines), bypassCache: true, ct);
    }

    /// <summary>Replace-text version of <see cref="TranslateBlockAtAsync"/>: just the block of text
    /// under <paramref name="point"/>. Null when there was no text near it.</summary>
    public async Task<ReplacementFrame?> TranslateBlockAtReplaceAsync(Rectangle band, System.Drawing.Point point, CancellationToken ct)
    {
        using var bitmap = ScreenCapture.CaptureRegion(band);
        var lines = await _ocrEngine.RecognizeLinesAsync(bitmap, _settings.SourceLanguageKey, ct);
        if (lines is null || lines.Count == 0) return null;

        var block = TextBlockLocator.FindBlockAt(lines, new System.Drawing.Point(point.X - band.X, point.Y - band.Y));
        if (block.Count == 0) return null;

        return await BuildReplacementAsync((Bitmap)bitmap.Clone(), band.Location, new List<List<OcrTextLine>> { block }, bypassCache: true, ct);
    }

    /// <summary>Commits the text the loop just confirmed, in replace-text mode: translates each block
    /// of the newest screenshot and hands the result to whoever draws it.</summary>
    private async Task CommitReplacementAsync(string normalized, CancellationToken ct)
    {
        var snapshot = _latestFrame;
        if (snapshot is null || SubtitleTextProcessor.Similarity(snapshot.Normalized, normalized) < 0.85)
        {
            // The newest read isn't the text being committed - read the screen again (it is still there).
            using var bitmap = ScreenCapture.CaptureRegion(new Rectangle(_settings.Region.X, _settings.Region.Y, _settings.Region.Width, _settings.Region.Height));
            var lines = await _ocrEngine.RecognizeLinesAsync(bitmap, _settings.SourceLanguageKey, ct);
            if (lines is null || lines.Count == 0) return;
            var fresh = new FrameSnapshot((Bitmap)bitmap.Clone(), new System.Drawing.Point(_settings.Region.X, _settings.Region.Y), lines);
            if (SubtitleTextProcessor.Similarity(fresh.Normalized, normalized) < 0.85) { fresh.Dispose(); return; }
            SetLatestFrame(fresh);
            snapshot = fresh;
        }

        // A clearly different line, as opposed to the same line read slightly differently.
        var newLine = _replacementShown && SubtitleTextProcessor.Similarity(normalized, _lastCommittedNormalized) < SameLineSimilarity;

        // Mark the text as handled before translating, so the loop doesn't start on it again while
        // this is still going on.
        _lastCommittedNormalized = normalized;

        try
        {
            var blocks = TextBlockLocator.GroupIntoBlocks(snapshot.Lines);

            // Translate first. If that's quick the old translation is simply swapped for the new one;
            // if it takes a while (an AI model), the old one comes down so it doesn't sit over the
            // new line for seconds.
            var translating = TranslateBlocksAsync(blocks, ct);
            if (newLine)
            {
                var first = await Task.WhenAny(translating, Task.Delay(HideOldAfterMs, ct));
                if (first != translating)
                {
                    _replacementShown = false;
                    ReplacementUpdated?.Invoke(null);
                }
            }
            await translating;

            // The screen moves on while a translation is being made, so draw from a fresh look at it
            // rather than from the screenshot taken before - with the translations just made (or
            // ones for the same text). Nothing matching means the text is gone or has changed again.
            var region = new Rectangle(_settings.Region.X, _settings.Region.Y, _settings.Region.Width, _settings.Region.Height);
            using var bitmap = ScreenCapture.CaptureRegion(region);
            var lines = await _ocrEngine.RecognizeLinesAsync(bitmap, _settings.SourceLanguageKey, ct);
            var frame = lines is { Count: > 0 }
                ? await BuildReplacementAsync((Bitmap)bitmap.Clone(), region.Location, TextBlockLocator.GroupIntoBlocks(lines), bypassCache: false, ct, cacheOnly: true)
                : null;

            if (frame is null)
            {
                // Don't leave an outdated translation up; the loop picks up whatever is on screen now.
                _lastCommittedNormalized = "";
                if (_replacementShown)
                {
                    _replacementShown = false;
                    ReplacementUpdated?.Invoke(null);
                }
                return;
            }

            _lastShownAtTicks = Environment.TickCount64;
            _replacementShown = true;
            ReplacementUpdated?.Invoke(frame);
        }
        catch
        {
            // Translator failure: try again later (the loop backs off after each failure).
            _lastCommittedNormalized = "";
            throw;
        }
    }

    /// <summary>The biggest few blocks - the dialogue - not every scrap of text on a busy screen.</summary>
    private static IEnumerable<List<OcrTextLine>> SelectBlocks(IReadOnlyList<List<OcrTextLine>> blocks) =>
        blocks.OrderByDescending(b => b.Sum(l => l.Text.Length)).Take(MaxBlocksPerFrame);

    /// <summary>Translates the blocks without drawing anything; the results go into the block cache.</summary>
    private async Task TranslateBlocksAsync(IReadOnlyList<List<OcrTextLine>> blocks, CancellationToken ct)
    {
        foreach (var block in SelectBlocks(blocks))
        {
            ct.ThrowIfCancellationRequested();
            var text = string.Join("\n", block.Select(l => l.Text));
            if (!SubtitleTextProcessor.LooksPlausible(text)) continue;
            await TranslateBlockCachedAsync(text, bypassCache: false, ct);
        }
    }

    /// <summary>Translates each block and wraps the results with the screenshot they were found in.
    /// Takes ownership of <paramref name="image"/>: it ends up in the returned frame, or is disposed
    /// when there is nothing to show.</summary>
    private async Task<ReplacementFrame?> BuildReplacementAsync(
        Bitmap image, System.Drawing.Point origin, IReadOnlyList<List<OcrTextLine>> blocks, bool bypassCache, CancellationToken ct,
        bool cacheOnly = false)
    {
        try
        {
            var result = new List<ReplacementBlock>();

            // Every block is its own translation request - so only the biggest few are used.
            foreach (var block in SelectBlocks(blocks))
            {
                ct.ThrowIfCancellationRequested();

                var text = string.Join("\n", block.Select(l => l.Text));
                if (!SubtitleTextProcessor.LooksPlausible(text)) continue;

                // cacheOnly: draw only what has already been translated, never start a new request.
                (string? Speaker, string Body) parts;
                if (cacheOnly)
                {
                    if (!TryGetCached(SubtitleTextProcessor.NormalizeForComparison(text), out parts)) continue;
                }
                else
                {
                    parts = await TranslateBlockCachedAsync(text, bypassCache, ct);
                }
                var (speaker, body) = parts;
                if (string.IsNullOrWhiteSpace(body)) continue;

                result.Add(new ReplacementBlock(
                    block.Select(l => l.Bounds).Aggregate(Rectangle.Union),
                    string.IsNullOrWhiteSpace(speaker) ? null : speaker,
                    body,
                    block.Average(l => (double)l.Bounds.Height),
                    block.Count,
                    DetectAlignment(block)));
            }

            if (result.Count == 0)
            {
                image.Dispose();
                return null;
            }
            return new ReplacementFrame(image, origin, result);
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }

    /// <summary>Finds a stored translation for this text - or for text that is nearly the same, since OCR
    /// reads the same line slightly differently from one poll to the next.</summary>
    private bool TryGetCached(string normalizedKey, out (string? Speaker, string Body) value)
    {
        lock (_cacheLock)
        {
            if (_blockCache.TryGetValue(normalizedKey, out value)) return true;
            foreach (var pair in _blockCache)
            {
                if (SubtitleTextProcessor.Similarity(normalizedKey, pair.Key) < CacheSimilarity) continue;
                value = pair.Value;
                return true;
            }
        }
        value = default;
        return false;
    }

    /// <summary>A block that's already been translated (HUD labels, menus that stay put while the
    /// dialogue changes) isn't sent to the translator again.</summary>
    private async Task<(string? Speaker, string Body)> TranslateBlockCachedAsync(string text, bool bypassCache, CancellationToken ct)
    {
        var key = SubtitleTextProcessor.NormalizeForComparison(text);
        if (!bypassCache && TryGetCached(key, out var hit)) return hit;

        var translated = await TranslateRecognizedPartsAsync(text, ct);
        if (!string.IsNullOrWhiteSpace(translated.Body))
        {
            lock (_cacheLock)
            {
                if (_blockCache.Count >= 64) _blockCache.Clear();
                _blockCache[key] = translated;
            }
        }
        return translated;
    }

    /// <summary>Centered when the lines of a block share a middle more than a left edge (typical for
    /// subtitles), left-aligned otherwise. A single line is centered.</summary>
    private static BlockAlignment DetectAlignment(IReadOnlyList<OcrTextLine> block)
    {
        if (block.Count < 2) return BlockAlignment.Center;
        var leftSpread = block.Max(l => l.Bounds.Left) - block.Min(l => l.Bounds.Left);
        var centers = block.Select(l => l.Bounds.Left + l.Bounds.Width / 2.0).ToList();
        var centerSpread = centers.Max() - centers.Min();
        return centerSpread + 2 < leftSpread ? BlockAlignment.Center : BlockAlignment.Left;
    }

    private void SetLatestFrame(FrameSnapshot? frame)
    {
        var old = _latestFrame;
        _latestFrame = frame;
        old?.Dispose();
    }

    /// <summary>One OCR read in replace-text mode: the screenshot, where it was taken and the lines found.</summary>
    private sealed class FrameSnapshot : IDisposable
    {
        public FrameSnapshot(Bitmap image, System.Drawing.Point origin, IReadOnlyList<OcrTextLine> lines)
        {
            Image = image;
            Origin = origin;
            Lines = lines;
            Normalized = SubtitleTextProcessor.NormalizeForComparison(string.Join("\n", lines.Select(l => l.Text)));
        }

        public Bitmap Image { get; }
        public System.Drawing.Point Origin { get; }
        public IReadOnlyList<OcrTextLine> Lines { get; }
        public string Normalized { get; }

        public void Dispose() => Image.Dispose();
    }

    /// <summary>Splits off a speaker/character name and translates it separately from the
    /// dialogue body (both get translated - just as two independent pieces rather than one
    /// merged sentence, so the name doesn't get folded into the sentence's grammar).</summary>
    private async Task<string> TranslateRecognizedTextAsync(string recognized, CancellationToken ct)
    {
        var (translatedSpeaker, translatedBody) = await TranslateRecognizedPartsAsync(recognized, ct);
        if (string.IsNullOrWhiteSpace(translatedBody)) return "";
        return SubtitleTextProcessor.Combine(translatedSpeaker, translatedBody);
    }

    /// <summary>Same as <see cref="TranslateRecognizedTextAsync"/> but keeps the translated speaker name
    /// and the translated dialogue apart, for display modes that style the name separately.</summary>
    private async Task<(string? Speaker, string Body)> TranslateRecognizedPartsAsync(string recognized, CancellationToken ct)
    {
        var (speaker, body) = SubtitleTextProcessor.SplitSpeakerAndBody(recognized);
        if (string.IsNullOrWhiteSpace(body)) return (null, "");

        var speakerEntry = GlossaryProcessor.FindSpeakerEntry(speaker, _settings.ActiveGlossaryEntries);

        // An AI translator gets the glossary, the game context and the previous lines as part of
        // its prompt, so it can inflect names and keep the dialogue coherent. Plain translation
        // APIs can't do that, so for them terms are swapped for placeholders and restored after.
        var ai = _translator as IContextualTranslator;
        string translatedBody;
        if (ai != null)
        {
            translatedBody = await ai.TranslateAsync(body, _settings.SourceLanguageKey, _settings.TargetLanguageKey, BuildAiContext(body, false), ct);
            RememberDialogueLine(body);
        }
        else
        {
            var protectedBody = GlossaryProcessor.ProtectTerms(body, _settings.ActiveGlossaryEntries, out var restoreList);
            translatedBody = await _translator.TranslateAsync(protectedBody, _settings.SourceLanguageKey, _settings.TargetLanguageKey, ct);
            translatedBody = GlossaryProcessor.RestoreTerms(translatedBody, restoreList);
        }

        string? translatedSpeaker = null;
        if (!string.IsNullOrWhiteSpace(speaker))
        {
            translatedSpeaker = speakerEntry != null && !string.IsNullOrWhiteSpace(speakerEntry.TargetTerm)
                ? speakerEntry.TargetTerm
                : ai != null
                    ? await ai.TranslateAsync(speaker!, _settings.SourceLanguageKey, _settings.TargetLanguageKey, BuildAiContext(speaker!, true), ct)
                    : await _translator.TranslateAsync(speaker!, _settings.SourceLanguageKey, _settings.TargetLanguageKey, ct);
        }

        return (translatedSpeaker, translatedBody);
    }

    private const int DialogueHistorySize = 3;
    private readonly List<string> _dialogueHistory = new();
    private readonly object _historyLock = new();

    private TranslationContext BuildAiContext(string currentText, bool isCharacterName)
    {
        var ai = _settings.Ai;
        IReadOnlyList<string> previous = Array.Empty<string>();
        if (ai.UseDialogueHistory && !isCharacterName)
        {
            lock (_historyLock)
                previous = _dialogueHistory
                    .Where(l => !string.Equals(l, currentText, StringComparison.Ordinal)) // a re-translate of the same line shouldn't list itself as "previous"
                    .ToList();
        }
        IReadOnlyList<GlossaryEntry> glossary = ai.UseGlossary ? _settings.ActiveGlossaryEntries : Array.Empty<GlossaryEntry>();
        return new TranslationContext(ai.GameContext, glossary, previous, isCharacterName);
    }

    private void RememberDialogueLine(string line)
    {
        lock (_historyLock)
        {
            if (_dialogueHistory.Count > 0 && _dialogueHistory[^1] == line) return;
            _dialogueHistory.Add(line);
            while (_dialogueHistory.Count > DialogueHistorySize) _dialogueHistory.RemoveAt(0);
        }
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
        _latestFrame?.Dispose();
        _ = _ocrEngine.DisposeAsync();
    }
}
