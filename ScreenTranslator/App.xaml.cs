using System;
using System.Drawing;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Application = System.Windows.Application;
using ScreenTranslator.Capture;
using ScreenTranslator.Hotkeys;
using ScreenTranslator.Models;
using ScreenTranslator.Onboarding;
using ScreenTranslator.Overlay;
using ScreenTranslator.SettingsUi;

namespace ScreenTranslator;

public partial class App : Application
{
    private const string AppDisplayName = "KainoTranslator";

    private AppSettings _settings = null!;
    private TranslationEngine _engine = null!;
    private HotkeyManager _hotkeys = null!;
    private OverlayWindow _overlay = null!;
    private System.Windows.Forms.NotifyIcon _trayIcon = null!;
    private SettingsWindow? _settingsWindow;
    private DispatcherTimer? _autoHideTimer;
    private CancellationTokenSource? _oneTimeCts;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _settings = AppSettings.Load();

        _overlay = new OverlayWindow();
        _overlay.ApplyStyle(_settings.Overlay);
        _overlay.Hide();

        _engine = new TranslationEngine(_settings);
        _engine.TextUpdated += OnTextUpdated;
        _engine.ErrorOccurred += OnEngineError;
        _engine.RunningStateChanged += isRunning => Dispatcher.Invoke(() => _settingsWindow?.SetRunningState(isRunning));

        _hotkeys = new HotkeyManager();
        RegisterHotkeys();

        SetupTrayIcon();
        ShowBalloon(AppDisplayName, "Программа запущена и работает в трее.");

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

        if (!_hotkeys.Register(_settings.Hotkeys.SelectRegion.Combo, OnSelectRegionHotkey))
            ShowBalloon("Конфликт горячих клавиш", $"Не удалось назначить «{_settings.Hotkeys.SelectRegion.Combo}» для выбора области — возможно, она уже занята другой программой.");

        if (!_hotkeys.Register(_settings.Hotkeys.ToggleTranslation.Combo, OnToggleHotkey))
            ShowBalloon("Конфликт горячих клавиш", $"Не удалось назначить «{_settings.Hotkeys.ToggleTranslation.Combo}» для включения/выключения перевода — возможно, она уже занята другой программой.");

        if (!_hotkeys.Register(_settings.Hotkeys.OpenSettings.Combo, OnOpenSettingsHotkey))
            ShowBalloon("Конфликт горячих клавиш", $"Не удалось назначить «{_settings.Hotkeys.OpenSettings.Combo}» для открытия настроек — возможно, она уже занята другой программой.");

        if (!_hotkeys.Register(_settings.Hotkeys.OneTimeTranslate.Combo, OnOneTimeTranslateHotkey))
            ShowBalloon("Конфликт горячих клавиш", $"Не удалось назначить «{_settings.Hotkeys.OneTimeTranslate.Combo}» для разового перевода — возможно, она уже занята другой программой.");
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
            ShowBalloon("Область захвата выбрана", $"{region.Width}×{region.Height} в точке ({region.X},{region.Y}).");
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
                "Сначала выберите область экрана для перевода (горячая клавиша «Выбрать область экрана»), а затем включите перевод.",
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
                ShowBalloon(AppDisplayName, "Не удалось распознать текст в выбранной области.");
                return;
            }

            _overlay.SetText(translated);
            _overlay.Show();
            _overlay.PositionRelativeTo(region, OverlayPosition.OverCaptureArea);
            RestartAutoHideTimer(forceSeconds: 8);
        }
        catch (Exception ex)
        {
            ShowBalloon(AppDisplayName, "Ошибка разового перевода: " + ex.Message);
        }
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
            _overlay.ApplyStyle(_settings.Overlay);
            RegisterHotkeys();
            await _engine.ApplySettingsAsync(_settings);
        };
        _settingsWindow.Closed += (_, _) => ShowBalloon(AppDisplayName, "Программа осталась запущена в трее.");
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void OnTextUpdated(string translated)
    {
        Dispatcher.Invoke(() =>
        {
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
        menu.Items.Add("Выбрать область экрана", null, (_, _) => SelectRegion());
        menu.Items.Add("Включить / выключить перевод", null, (_, _) => ToggleTranslation());
        menu.Items.Add("Перевести область один раз", null, (_, _) => OneTimeTranslate());
        menu.Items.Add("Настройки...", null, (_, _) => OpenSettings());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Справка / с чего начать", null, (_, _) => ShowWelcomeWindow());
        menu.Items.Add("О программе", null, (_, _) => ShowAboutDialog());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => Shutdown());
        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += (_, _) => OpenSettings();
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
        _engine?.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        base.OnExit(e);
    }
}
