using System;
using System.Drawing;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Application = System.Windows.Application;
using ScreenTranslator.Capture;
using ScreenTranslator.Hotkeys;
using ScreenTranslator.Localization;
using ScreenTranslator.Models;
using ScreenTranslator.Onboarding;
using ScreenTranslator.Overlay;
using ScreenTranslator.SettingsUi;
using ScreenTranslator.Themes;

namespace ScreenTranslator;

public partial class App : Application
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetCursorPos(out System.Drawing.Point lpPoint);

    private const string AppDisplayName = "KainoTranslator";
    public const string AppVersion = "2.0";

    private AppSettings _settings = null!;
    private TranslationEngine _engine = null!;
    private HotkeyManager _hotkeys = null!;
    private IDisposable? _hoverTriggerHook; // BareKeyHook or BareMouseButtonHook, whichever HoverTranslate.TriggerKey resolves to
    private LeftClickWatcher? _leftClickWatcher;
    private DismissKeyWatcher? _dismissKeyWatcher;
    private System.Windows.Forms.ToolStripMenuItem? _hoverTrayMenuItem;
    private bool _suppressHoverTrayToggle;
    // Remembered so unchecking then rechecking the tray item restores it instead of forcing a
    // re-pick. Defaults to Ctrl+F: a modifier+key combo, so it goes through RegisterHotKey like
    // every other hotkey and isn't affected by the anti-cheat input-hook blocking that a bare
    // key/mouse-button trigger can run into in some games (see Settings.HoverSectionHint).
    private string _lastHoverTriggerKey = "Ctrl+F";
    private OverlayWindow _overlay = null!;
    private static Mutex? s_singleInstance; // kept for the life of the process, so it isn't collected
    private ReplaceOverlayWindow _replaceOverlay = null!; // the "replace text" display mode - see Overlay/ReplaceOverlayWindow
    private System.Windows.Forms.NotifyIcon _trayIcon = null!;
    private SettingsWindow? _settingsWindow;
    private DispatcherTimer? _autoHideTimer;
    private CancellationTokenSource? _oneTimeCts;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _settings = AppSettings.Load();
        Loc.Current = _settings.UiLanguage == "en" ? AppLanguage.English : AppLanguage.Russian;

        // Two copies at once (say, the new build started while the old one still sits in the tray)
        // would both read the screen, both translate it and both draw an overlay over the other's -
        // which looks like flicker and doubles the requests to the translator.
        s_singleInstance = new Mutex(true, @"Local\KainoTranslator.SingleInstance", out var firstInstance);
        if (!firstInstance)
        {
            System.Windows.MessageBox.Show(Loc.S("App.AlreadyRunning"), AppDisplayName);
            Shutdown();
            return;
        }

        ThemeManager.Apply(ParseThemeMode(_settings.UiTheme));

        _overlay = new OverlayWindow();
        _overlay.ApplyStyle(_settings.Overlay);
        _overlay.Hide();
        _replaceOverlay = new ReplaceOverlayWindow();

        _engine = new TranslationEngine(_settings);
        _engine.TextUpdated += OnTextUpdated;
        _engine.ReplacementUpdated += OnReplacementUpdated;
        _engine.ErrorOccurred += OnEngineError;
        _engine.RunningStateChanged += isRunning => Dispatcher.Invoke(() => _settingsWindow?.SetRunningState(isRunning));

        _hotkeys = new HotkeyManager();
        RegisterHotkeys();
        ApplyHoverTranslateSetting();

        SetupTrayIcon();
        ShowBalloon(AppDisplayName, Loc.S("Balloon.Started"));

        if (_settings.ShowWelcomeOnStartup)
            ShowWelcomeWindow();
    }

    private void ShowWelcomeWindow()
    {
        var welcome = new WelcomeWindow(_settings.Hotkeys);
        welcome.OpenSettingsRequested += () => Dispatcher.Invoke(OpenSettings);
        welcome.Closed += (_, _) =>
        {
            if (welcome.DontShowAgain)
            {
                _settings.ShowWelcomeOnStartup = false;
                _settings.Save();
            }
        };
        welcome.Show();
    }

    private void RegisterHotkeys()
    {
        _hotkeys.UnregisterAll();
        TryRegisterHotkey(_settings.Hotkeys.SelectRegion.Combo, OnSelectRegionHotkey, "Balloon.HotkeyConflict.SelectRegion");
        TryRegisterHotkey(_settings.Hotkeys.ToggleTranslation.Combo, OnToggleHotkey, "Balloon.HotkeyConflict.Toggle");
        TryRegisterHotkey(_settings.Hotkeys.OpenSettings.Combo, OnOpenSettingsHotkey, "Balloon.HotkeyConflict.Settings");
        TryRegisterHotkey(_settings.Hotkeys.OneTimeTranslate.Combo, OnOneTimeTranslateHotkey, "Balloon.HotkeyConflict.OneTime");
        TryRegisterHotkey(_settings.Hotkeys.Retranslate.Combo, OnRetranslateHotkey, "Balloon.HotkeyConflict.Retranslate");
        if (IsHoverComboTrigger(_settings.HoverTranslate.TriggerKey))
            TryRegisterHotkey(_settings.HoverTranslate.TriggerKey, OnHoverTriggerComboHotkey, "Balloon.HotkeyConflict.HoverCombo");
    }

    /// <summary>Registers one hotkey, unless the combo is empty - cleared via Backspace in
    /// Settings means "no hotkey for this action", not a conflict to warn about.</summary>
    private void TryRegisterHotkey(string combo, Action handler, string conflictLocKey)
    {
        if (string.IsNullOrWhiteSpace(combo)) return;
        if (!_hotkeys.Register(combo, handler))
            ShowBalloon(Loc.S("Balloon.HotkeyConflictTitle"), string.Format(Loc.S(conflictLocKey), combo));
    }

    private void OnSelectRegionHotkey() => Dispatcher.Invoke(SelectRegion);

    private void SelectRegion()
    {
        var wasRunning = _engine.IsRunning;
        _engine.Stop();

        var selector = new RegionSelectorWindow();
        selector.ShowDialog();

        if (selector.SelectedRegion is { } region)
        {
            _settings.Region.X = region.X;
            _settings.Region.Y = region.Y;
            _settings.Region.Width = region.Width;
            _settings.Region.Height = region.Height;
            _settings.Region.HasRegion = true;
            _settings.Save();
            ShowBalloon(Loc.S("Balloon.RegionSetTitle"), string.Format(Loc.S("Balloon.RegionSetText"), region.Width, region.Height, region.X, region.Y));

            if (_settings.AutoStartTranslationAfterRegionSelect) wasRunning = true;
        }

        if (wasRunning) _engine.Start();
    }

    private void OnToggleHotkey() => Dispatcher.Invoke(ToggleTranslation);

    /// <summary>Shared by the hotkey, the tray menu, and the header button - warns instead of silently failing if no area is selected yet.</summary>
    private void ToggleTranslation()
    {
        if (!_engine.IsRunning && !_settings.Region.HasRegion)
        {
            System.Windows.MessageBox.Show(
                Loc.S("Warning.NoRegionText"),
                AppDisplayName, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _engine.Toggle();
    }

    private void OnOpenSettingsHotkey() => Dispatcher.Invoke(OpenSettings);

    private void OnOneTimeTranslateHotkey() => Dispatcher.Invoke(OneTimeTranslate);

    /// <summary>
    /// Lets the user pick any area on screen, translates it once, and shows the result in the
    /// same overlay window temporarily - without touching the main capture area or the
    /// continuous translation loop (which keeps running in the background if it was on).
    /// </summary>
    private async void OneTimeTranslate()
    {
        var selector = new RegionSelectorWindow();
        selector.ShowDialog();
        if (selector.SelectedRegion is not { } region) return;

        _oneTimeCts?.Cancel();
        _oneTimeCts = new CancellationTokenSource();
        var ct = _oneTimeCts.Token;

        try
        {
            if (_engine.ReplaceModeActive)
            {
                var replacement = await _engine.TranslateOnceReplaceAsync(region, ct);
                if (ct.IsCancellationRequested) { replacement?.Dispose(); return; }

                if (replacement is null)
                {
                    ShowBalloon(AppDisplayName, Loc.S("Balloon.OneTimeNoText"));
                    return;
                }

                ShowOneTimeReplacement(replacement);
                return;
            }

            var translated = await _engine.TranslateOnceAsync(region, ct);
            if (ct.IsCancellationRequested) return;

            if (string.IsNullOrWhiteSpace(translated))
            {
                ShowBalloon(AppDisplayName, Loc.S("Balloon.OneTimeNoText"));
                return;
            }

            ShowOneTimeOverlay(region, translated);
        }
        catch (Exception ex)
        {
            ShowBalloon(AppDisplayName, string.Format(Loc.S("Balloon.OneTimeError"), ex.Message));
        }
    }

    private void OnRetranslateHotkey() => Dispatcher.Invoke(RetranslateCurrent);
    private void OnHoverTriggerComboHotkey() => Dispatcher.Invoke(TriggerInstantTranslate);

    /// <summary>
    /// Forces a fresh OCR+translate of the main capture area, ignoring the "already showing this
    /// text" cache - for when the source text is still on screen but the translation itself came
    /// out garbled and needs a second attempt. Updates the same overlay the continuous loop uses
    /// (not the one-off overlay), since this is meant to correct what's already showing there.
    /// </summary>
    private async void RetranslateCurrent()
    {
        if (!_settings.Region.HasRegion)
        {
            ShowBalloon(AppDisplayName, Loc.S("Warning.NoRegionText"));
            return;
        }

        _oneTimeCts?.Cancel();
        _oneTimeCts = new CancellationTokenSource();
        var ct = _oneTimeCts.Token;

        try
        {
            await _engine.RetranslateCurrentAsync(ct);
        }
        catch (Exception ex)
        {
            ShowBalloon(AppDisplayName, string.Format(Loc.S("Balloon.RetranslateError"), ex.Message));
        }
    }

    /// <summary>(Re)creates or tears down the instant-translate trigger hook to match the current
    /// setting - call after startup and whenever Settings are saved, same as RegisterHotkeys().
    /// There's no separate on/off flag: an empty TriggerKey (cleared via Backspace in Settings)
    /// means instant translate is off, same as any other cleared hotkey. A modifier+key combo
    /// trigger isn't handled here - it goes through RegisterHotkeys() like the other combos.</summary>
    private void ApplyHoverTranslateSetting()
    {
        _hoverTriggerHook?.Dispose();
        _hoverTriggerHook = null;

        var trigger = _settings.HoverTranslate.TriggerKey;
        if (string.IsNullOrWhiteSpace(trigger)) return;
        if (IsHoverComboTrigger(trigger)) return;

        if (TryParseHoverMouseButton(trigger, out var button))
        {
            var mouseHook = new BareMouseButtonHook(button);
            mouseHook.Tapped += () => Dispatcher.Invoke(TriggerInstantTranslate);
            mouseHook.Start();
            _hoverTriggerHook = mouseHook;
            return;
        }

        if (!Enum.TryParse<System.Windows.Input.Key>(trigger, out var key))
        {
            ShowBalloon(AppDisplayName, Loc.S("Balloon.HoverKeyInvalid"));
            return;
        }

        var keyHook = new BareKeyHook(key);
        keyHook.Tapped += () => Dispatcher.Invoke(TriggerInstantTranslate);
        keyHook.Start();
        _hoverTriggerHook = keyHook;
    }

    private static bool IsHoverComboTrigger(string trigger) => trigger.Contains('+');

    private static ThemeMode ParseThemeMode(string value) => value switch
    {
        "Light" => ThemeMode.Light,
        "Dark" => ThemeMode.Dark,
        _ => ThemeMode.System,
    };

    private static bool TryParseHoverMouseButton(string trigger, out HoverMouseButton button)
    {
        button = default;
        if (!trigger.StartsWith("Mouse:", StringComparison.Ordinal)) return false;
        return Enum.TryParse(trigger["Mouse:".Length..], out button);
    }

    /// <summary>
    /// Entry point for every instant-translate trigger (bare key, bare mouse button, or combo
    /// hotkey) - branches on the configured mode instead of translating directly,
    /// since "confirm click" needs to show a point-picker first.
    /// </summary>
    private void TriggerInstantTranslate()
    {
        if (_settings.HoverTranslate.Mode == HoverTranslateMode.ConfirmClick)
        {
            var picker = new ClickPointSelectorWindow();
            picker.ShowDialog();
            if (picker.SelectedPoint is not { } dip) return;

            // The picker already converted to physical pixels for us (same convention as
            // RegionSelectorWindow), so this is a direct System.Drawing.Point, not a DIP->device
            // conversion here.
            HoverTranslate(new System.Drawing.Point((int)Math.Round(dip.X), (int)Math.Round(dip.Y)));
        }
        else
        {
            HoverTranslate(null);
        }
    }

    /// <summary>
    /// Gaminik-style one-shot translate: captures a fixed-size box centered on the given point (or
    /// the current mouse position, if none was given) and translates it, same as
    /// OneTimeTranslate() otherwise - doesn't touch the main capture area or the continuous loop.
    /// </summary>
    private async void HoverTranslate(System.Drawing.Point? at)
    {
        System.Drawing.Point cursor;
        if (at is { } given) cursor = given;
        else { GetCursorPos(out var p); cursor = p; }

        var screenBounds = System.Windows.Forms.Screen.FromPoint(cursor).Bounds;

        // Capture a band across the whole width of the monitor, centered vertically on the
        // cursor, and let the engine pick out the block of text under the cursor (see
        // TranslateBlockAtAsync) - so neither the width nor the height of the dialogue matters.
        // The band is generous (a third of the monitor height) when the OCR engine can locate
        // lines itself; engines that can't just get the whole band as text, so for those it's
        // kept tighter (a fifth) to limit stray text. BoxHeight is only the minimum.
        var divisor = _engine.SupportsBlockDetection ? 3 : 5;
        var h = Math.Min(screenBounds.Height, Math.Max(_settings.HoverTranslate.BoxHeight, screenBounds.Height / divisor));
        var region = new Rectangle(screenBounds.Left, cursor.Y - h / 2, screenBounds.Width, h);
        // Clamped inside the monitor so a cursor near the top/bottom edge doesn't capture
        // off-screen coordinates.
        region.Y = Math.Max(screenBounds.Top, Math.Min(region.Y, screenBounds.Bottom - h));

        // Fallback overlay anchor for when the text block's own position isn't known: a compact
        // box around the cursor rather than the full-width band, so the overlay shows up next to
        // the text instead of always at screen center.
        const int overlayAnchorWidth = 500;
        var anchorX = Math.Max(screenBounds.Left, Math.Min(cursor.X - overlayAnchorWidth / 2, screenBounds.Right - overlayAnchorWidth));
        var overlayAnchor = new Rectangle(anchorX, region.Y, overlayAnchorWidth, h);

        _oneTimeCts?.Cancel();
        _oneTimeCts = new CancellationTokenSource();
        var ct = _oneTimeCts.Token;

        try
        {
            if (_engine.ReplaceModeActive)
            {
                var replacement = await _engine.TranslateBlockAtReplaceAsync(region, cursor, ct);
                if (ct.IsCancellationRequested) { replacement?.Dispose(); return; }
                if (replacement is null) return; // stay silent on hover misses, same as below

                ShowOneTimeReplacement(replacement);
                return;
            }

            var (translated, sourceBounds) = await _engine.TranslateBlockAtAsync(region, cursor, ct);
            if (ct.IsCancellationRequested) return;
            if (string.IsNullOrWhiteSpace(translated)) return; // stay silent on hover misses - a balloon for every empty hover would be noisy

            ShowOneTimeOverlay(sourceBounds ?? overlayAnchor, translated);
        }
        catch (Exception ex)
        {
            ShowBalloon(AppDisplayName, string.Format(Loc.S("Balloon.OneTimeError"), ex.Message));
        }
    }

    /// <summary>
    /// Shows a translation from either OneTimeTranslate() or HoverTranslate(). Two things set
    /// this apart from the continuous-mode overlay in OnTextUpdated():
    ///  - Position is picked dynamically (above vs. below the source text) based on which half of
    ///    the screen it's on, so the translation doesn't land directly on top of the original -
    ///    overlapping it would mean the *next* capture in the same spot picks up a mix of the old
    ///    translation and the new source text.
    ///  - A left-click or Space press anywhere dismisses it immediately (see LeftClickWatcher /
    ///    DismissKeyWatcher), in addition to the auto-hide timer, so re-translating the next line
    ///    of dialogue right after either doesn't fight with a stale translation still on screen.
    ///    Optional - Overlay.DismissOnClickOrSpace turns it off for people who click/hit Space
    ///    often enough during play that this would fire unwanted. Auto-hide itself follows the
    ///    same "Поведение окна" setting as continuous mode (Overlay.AutoHideEnabled/Seconds); if
    ///    both settings are off, the overlay only goes away when replaced by the next translate.
    /// </summary>
    private void ShowOneTimeOverlay(Rectangle region, string translated)
    {
        _replaceOverlay.Hide();
        _overlay.SetText(translated);
        _overlay.Show();

        var screenBounds = System.Windows.Forms.Screen.FromRectangle(region).Bounds;
        var regionCenterY = region.Top + region.Height / 2;
        var screenCenterY = screenBounds.Top + screenBounds.Height / 2;
        var position = regionCenterY > screenCenterY ? OverlayPosition.AboveCaptureArea : OverlayPosition.BelowCaptureArea;
        _overlay.PositionRelativeTo(region, position);

        RestartAutoHideTimer();
        StartDismissWatchers();
    }

    /// <summary>Replace-text counterpart of <see cref="ShowOneTimeOverlay"/>: the translation is drawn
    /// over the source text itself, so there's no above/below placement to pick. Closes the same way
    /// (auto-hide, left click, Space).</summary>
    private void ShowOneTimeReplacement(ReplacementFrame frame)
    {
        _overlay.Hide();
        try { _replaceOverlay.Present(frame, _settings.Overlay); }
        finally { frame.Dispose(); }

        RestartAutoHideTimer();
        StartDismissWatchers();
    }

    private void StartDismissWatchers()
    {
        if (!_settings.Overlay.DismissOnClickOrSpace) return;
        _leftClickWatcher ??= CreateLeftClickWatcher();
        _leftClickWatcher.Start();
        _dismissKeyWatcher ??= CreateDismissKeyWatcher();
        _dismissKeyWatcher.Start();
    }

    private LeftClickWatcher CreateLeftClickWatcher()
    {
        var watcher = new LeftClickWatcher();
        watcher.Clicked += () => Dispatcher.Invoke(DismissOneTimeOverlay);
        return watcher;
    }

    private DismissKeyWatcher CreateDismissKeyWatcher()
    {
        var watcher = new DismissKeyWatcher();
        watcher.Pressed += () => Dispatcher.Invoke(DismissOneTimeOverlay);
        return watcher;
    }

    private void DismissOneTimeOverlay()
    {
        _autoHideTimer?.Stop();
        _overlay.Hide();
        _replaceOverlay.Hide();
        _leftClickWatcher?.Stop();
        _dismissKeyWatcher?.Stop();
    }

    private void OpenSettings()
    {
        if (_settingsWindow is { IsVisible: true })
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(_settings, _engine.IsRunning);
        _settingsWindow.RequestSelectRegion += () => Dispatcher.Invoke(SelectRegion);
        _settingsWindow.RequestToggleTranslation += () => Dispatcher.Invoke(ToggleTranslation);
        _settingsWindow.SettingsSaved += async updated =>
        {
            _settings = updated;
            Loc.Current = _settings.UiLanguage == "en" ? AppLanguage.English : AppLanguage.Russian;
            ThemeManager.Apply(ParseThemeMode(_settings.UiTheme));
            _overlay.ApplyStyle(_settings.Overlay);
            if (!_settings.Overlay.DismissOnClickOrSpace)
            {
                // Take effect immediately rather than waiting for the current one-off overlay (if
                // any) to be dismissed some other way first.
                _leftClickWatcher?.Stop();
                _dismissKeyWatcher?.Stop();
            }
            RegisterHotkeys();
            ApplyHoverTranslateSetting();
            if (!string.IsNullOrWhiteSpace(_settings.HoverTranslate.TriggerKey))
                _lastHoverTriggerKey = _settings.HoverTranslate.TriggerKey;
            if (_hoverTrayMenuItem is not null)
            {
                _suppressHoverTrayToggle = true;
                _hoverTrayMenuItem.Checked = !string.IsNullOrWhiteSpace(_settings.HoverTranslate.TriggerKey);
                _suppressHoverTrayToggle = false;
            }
            await _engine.ApplySettingsAsync(_settings);

            if (_settings.Overlay.DisplayMode == OverlayDisplayMode.ReplaceText && !_engine.ReplaceModeActive)
                ShowBalloon(AppDisplayName, Loc.S("Balloon.ReplaceUnavailable"));
            else if (_settings.Overlay.DisplayMode == OverlayDisplayMode.Panel)
                _replaceOverlay.Hide();
        };
        _settingsWindow.Closed += (_, _) => ShowBalloon(AppDisplayName, Loc.S("Balloon.StillRunning"));
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void OnTextUpdated(string translated)
    {
        Dispatcher.Invoke(() =>
        {
            _leftClickWatcher?.Stop(); // this overlay update is from continuous mode, not a one-off/hover result - click/Space-to-dismiss only applies to those
            _dismissKeyWatcher?.Stop();

            if (string.IsNullOrWhiteSpace(translated))
            {
                _overlay.Hide();
                _autoHideTimer?.Stop();
                return;
            }

            _overlay.SetText(translated);
            var region = new Rectangle(_settings.Region.X, _settings.Region.Y, _settings.Region.Width, _settings.Region.Height);
            _overlay.Show();
            _overlay.PositionRelativeTo(region, _settings.Overlay.Position);
            RestartAutoHideTimer();
        });
    }

    /// <summary>Continuous mode in the replace-text display: draws the translated blocks over the source
    /// text, or takes them down when the frame is null.</summary>
    private void OnReplacementUpdated(ReplacementFrame? frame)
    {
        Dispatcher.Invoke(() =>
        {
            _leftClickWatcher?.Stop(); // same as OnTextUpdated: click/Space-to-dismiss is for one-off results only
            _dismissKeyWatcher?.Stop();

            if (frame is null)
            {
                _replaceOverlay.Hide();
                _autoHideTimer?.Stop();
                return;
            }

            try
            {
                _overlay.Hide();
                _replaceOverlay.Present(frame, _settings.Overlay);
            }
            finally
            {
                frame.Dispose();
            }

            // No auto-hide here: the overlay sits on the source text and the engine takes it down
            // itself when that text goes away. An auto-hide timeout would just make it vanish while
            // the original is still on screen (and nothing would ever bring it back).
            _autoHideTimer?.Stop();
        });
    }

    /// <summary>(Re)starts the auto-hide countdown if enabled in settings; a fresh line of text always resets the timer.</summary>
    private void RestartAutoHideTimer()
    {
        _autoHideTimer?.Stop();
        var seconds = _settings.Overlay.AutoHideEnabled ? _settings.Overlay.AutoHideSeconds : (int?)null;
        if (seconds is null or <= 0) return;

        _autoHideTimer ??= new DispatcherTimer();
        _autoHideTimer.Interval = TimeSpan.FromSeconds(seconds.Value);
        _autoHideTimer.Tick -= OnAutoHideTick;
        _autoHideTimer.Tick += OnAutoHideTick;
        _autoHideTimer.Start();
    }

    private void OnAutoHideTick(object? sender, EventArgs e)
    {
        _autoHideTimer?.Stop();
        _overlay.Hide();
        _replaceOverlay.Hide();
        _leftClickWatcher?.Stop();
        _dismissKeyWatcher?.Stop();
    }

    private string? _lastErrorMessage;
    private DateTime _lastErrorAt;

    // The same error tends to repeat on every poll (a translator rate-limiting us, say). Windows
    // queues balloon notifications and keeps showing them one after another - even after the app
    // is closed - so an identical message is only shown once a minute.
    private void OnEngineError(string message) => Dispatcher.Invoke(() =>
    {
        var now = DateTime.UtcNow;
        if (message == _lastErrorMessage && now - _lastErrorAt < TimeSpan.FromSeconds(60)) return;
        _lastErrorMessage = message;
        _lastErrorAt = now;
        ShowBalloon(AppDisplayName, message);
    });

    private static System.Drawing.Icon GetAppIcon()
    {
        try
        {
            var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            if (exePath != null)
            {
                var extracted = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                if (extracted != null) return extracted;
            }
        }
        catch { /* fall back below */ }
        return System.Drawing.SystemIcons.Application;
    }

    private void SetupTrayIcon()
    {
        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = GetAppIcon(),
            Visible = true,
            Text = AppDisplayName
        };

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add(Loc.S("Tray.SelectRegion"), null, (_, _) => SelectRegion());
        menu.Items.Add(Loc.S("Tray.ToggleTranslation"), null, (_, _) => ToggleTranslation());
        menu.Items.Add(Loc.S("Tray.TranslateOnce"), null, (_, _) => OneTimeTranslate());
        var hoverItem = new System.Windows.Forms.ToolStripMenuItem(Loc.S("Tray.HoverTranslate"))
        {
            CheckOnClick = true,
            Checked = !string.IsNullOrWhiteSpace(_settings.HoverTranslate.TriggerKey)
        };
        hoverItem.CheckedChanged += (_, _) =>
        {
            if (_suppressHoverTrayToggle) return;
            var triggerBefore = _settings.HoverTranslate.TriggerKey;
            if (hoverItem.Checked)
            {
                _settings.HoverTranslate.TriggerKey = string.IsNullOrWhiteSpace(_settings.HoverTranslate.TriggerKey)
                    ? _lastHoverTriggerKey
                    : _settings.HoverTranslate.TriggerKey;
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(_settings.HoverTranslate.TriggerKey))
                    _lastHoverTriggerKey = _settings.HoverTranslate.TriggerKey;
                _settings.HoverTranslate.TriggerKey = "";
            }
            _settings.Save();
            // A combo trigger lives with the other RegisterHotKey hotkeys, so (un)registering it
            // means re-running that path too.
            if (IsHoverComboTrigger(triggerBefore) || IsHoverComboTrigger(_settings.HoverTranslate.TriggerKey))
                RegisterHotkeys();
            ApplyHoverTranslateSetting();
        };
        _hoverTrayMenuItem = hoverItem;
        menu.Items.Add(hoverItem);
        menu.Items.Add(Loc.S("Tray.Settings"), null, (_, _) => OpenSettings());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add(Loc.S("Tray.Help"), null, (_, _) => ShowWelcomeWindow());
        menu.Items.Add(Loc.S("Tray.About"), null, (_, _) => ShowAboutDialog());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add(Loc.S("Tray.Exit"), null, (_, _) => Shutdown());
        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += (_, _) => OpenSettings();
        _trayIcon.BalloonTipClicked += (_, _) => OpenSettings();
    }

    private void ShowAboutDialog()
    {
        var about = new Onboarding.AboutWindow();
        about.ShowDialog();
    }

    private void ShowBalloon(string title, string text)
    {
        _trayIcon.BalloonTipTitle = title;
        _trayIcon.BalloonTipText = text;
        _trayIcon.ShowBalloonTip(4000);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkeys?.Dispose();
        _hoverTriggerHook?.Dispose();
        _leftClickWatcher?.Dispose();
        _dismissKeyWatcher?.Dispose();
        ThemeManager.Unsubscribe();
        _engine?.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        base.OnExit(e);
    }
}
