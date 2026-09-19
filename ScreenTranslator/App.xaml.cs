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

namespace ScreenTranslator;

public partial class App : Application
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetCursorPos(out System.Drawing.Point lpPoint);

    private const string AppDisplayName = "KainoTranslator";
    public const string AppVersion = "1.5";

    private AppSettings _settings = null!;
    private TranslationEngine _engine = null!;
    private HotkeyManager _hotkeys = null!;
    private IDisposable? _hoverTriggerHook; // BareKeyHook or BareMouseButtonHook, whichever HoverTranslate.TriggerKey resolves to
    private LeftClickWatcher? _leftClickWatcher;
    private System.Windows.Forms.ToolStripMenuItem? _hoverTrayMenuItem;
    private bool _suppressHoverTrayToggle;
    private string _lastHoverTriggerKey = HoverTranslateSettings.DefaultTrigger; // remembered so unchecking then rechecking the tray item restores it instead of forcing a re-pick
    private OverlayWindow _overlay = null!;
    private System.Windows.Forms.NotifyIcon _trayIcon = null!;
    private SettingsWindow? _settingsWindow;
    private DispatcherTimer? _autoHideTimer;
    private CancellationTokenSource? _oneTimeCts;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _settings = AppSettings.Load();
        Loc.Current = _settings.UiLanguage == "en" ? AppLanguage.English : AppLanguage.Russian;

        _overlay = new OverlayWindow();
        _overlay.ApplyStyle(_settings.Overlay);
        _overlay.Hide();

        _engine = new TranslationEngine(_settings);
        _engine.TextUpdated += OnTextUpdated;
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

        var w = _settings.HoverTranslate.BoxWidth;
        var h = _settings.HoverTranslate.BoxHeight;
        var virtualScreen = System.Windows.Forms.SystemInformation.VirtualScreen;
        var region = new Rectangle(cursor.X - w / 2, cursor.Y - h / 2, w, h);
        // Clamp fully inside the virtual screen so a box near a monitor edge doesn't try to
        // capture negative/off-screen coordinates.
        region.X = Math.Max(virtualScreen.Left, Math.Min(region.X, virtualScreen.Right - w));
        region.Y = Math.Max(virtualScreen.Top, Math.Min(region.Y, virtualScreen.Bottom - h));

        _oneTimeCts?.Cancel();
        _oneTimeCts = new CancellationTokenSource();
        var ct = _oneTimeCts.Token;

        try
        {
            var translated = await _engine.TranslateOnceAsync(region, ct);
            if (ct.IsCancellationRequested) return;
            if (string.IsNullOrWhiteSpace(translated)) return; // stay silent on hover misses - a balloon for every empty hover would be noisy

            ShowOneTimeOverlay(region, translated);
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
    ///  - A left-click anywhere dismisses it immediately (see LeftClickWatcher) instead of only
    ///    the auto-hide timer, so re-translating the next line of dialogue right after a click
    ///    doesn't fight with a stale translation still on screen.
    /// </summary>
    private void ShowOneTimeOverlay(Rectangle region, string translated)
    {
        _overlay.SetText(translated);
        _overlay.Show();

        var screenBounds = System.Windows.Forms.Screen.FromRectangle(region).Bounds;
        var regionCenterY = region.Top + region.Height / 2;
        var screenCenterY = screenBounds.Top + screenBounds.Height / 2;
        var position = regionCenterY > screenCenterY ? OverlayPosition.AboveCaptureArea : OverlayPosition.BelowCaptureArea;
        _overlay.PositionRelativeTo(region, position);

        RestartAutoHideTimer(forceSeconds: 8);

        _leftClickWatcher ??= CreateLeftClickWatcher();
        _leftClickWatcher.Start();
    }

    private LeftClickWatcher CreateLeftClickWatcher()
    {
        var watcher = new LeftClickWatcher();
        watcher.Clicked += () => Dispatcher.Invoke(DismissOneTimeOverlay);
        return watcher;
    }

    private void DismissOneTimeOverlay()
    {
        _autoHideTimer?.Stop();
        _overlay.Hide();
        _leftClickWatcher?.Stop();
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
            _overlay.ApplyStyle(_settings.Overlay);
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
        };
        _settingsWindow.Closed += (_, _) => ShowBalloon(AppDisplayName, Loc.S("Balloon.StillRunning"));
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void OnTextUpdated(string translated)
    {
        Dispatcher.Invoke(() =>
        {
            _leftClickWatcher?.Stop(); // this overlay update is from continuous mode, not a one-off/hover result - click-to-dismiss only applies to those

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

    /// <summary>(Re)starts the auto-hide countdown if enabled in settings; a fresh line of text always resets the timer.</summary>
    private void RestartAutoHideTimer(int? forceSeconds = null)
    {
        _autoHideTimer?.Stop();
        var seconds = forceSeconds ?? (_settings.Overlay.AutoHideEnabled ? _settings.Overlay.AutoHideSeconds : (int?)null);
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
        _leftClickWatcher?.Stop();
    }

    private void OnEngineError(string message) => Dispatcher.Invoke(() => ShowBalloon(AppDisplayName, message));

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
        _engine?.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        base.OnExit(e);
    }
}
