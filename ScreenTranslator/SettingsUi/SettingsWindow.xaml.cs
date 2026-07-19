using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using System.Windows.Threading;
using Microsoft.Win32;
using ScreenTranslator.Localization;
using ScreenTranslator.Models;
using ScreenTranslator.Ocr;
using ScreenTranslator.Translation;
using TextBox = System.Windows.Controls.TextBox;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;

namespace ScreenTranslator.SettingsUi;

public partial class SettingsWindow : Window
{
    public event Action<AppSettings>? SettingsSaved;
    public event Action? RequestSelectRegion;
    public event Action? RequestToggleTranslation;

    private readonly AppSettings _working;
    private bool _isTranslationRunning;
    private bool _isLoading; // suppresses color-preview handlers firing while populating fields
    private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string RunKeyName = "KainoTranslator";

    public SettingsWindow(AppSettings current, bool isTranslationRunning = false)
    {
        InitializeComponent();
        _isTranslationRunning = isTranslationRunning;

        // Work on a clone so cancelling (closing without Save) doesn't mutate live settings.
        _working = current.Clone();

        ApplyLocalization();

        OcrEngineCombo.ItemsSource = new[]
        {
            new EnumOption<OcrEngineKind>(OcrEngineKind.WindowsOcr, "Windows OCR (" + (Loc.Current == AppLanguage.English ? "recommended" : "рекомендуется") + ")"),
            new EnumOption<OcrEngineKind>(OcrEngineKind.Tesseract, "Tesseract (legacy)"),
            new EnumOption<OcrEngineKind>(OcrEngineKind.EasyOcr, "EasyOCR (legacy)"),
        };

        TranslatorCombo.ItemsSource = new[]
        {
            new EnumOption<TranslatorKind>(TranslatorKind.DeepL, "DeepL"),
            new EnumOption<TranslatorKind>(TranslatorKind.Google, "Google Translate"),
            new EnumOption<TranslatorKind>(TranslatorKind.Yandex, "Yandex Translate"),
            new EnumOption<TranslatorKind>(TranslatorKind.Papago, "Papago"),
        };

        OverlayPositionCombo.ItemsSource = new[]
        {
            new EnumOption<OverlayPosition>(OverlayPosition.FixedTopCenterOfScreen, Loc.Current == AppLanguage.English ? "Top-center of screen" : "Вверху экрана по центру"),
            new EnumOption<OverlayPosition>(OverlayPosition.BelowCaptureArea, Loc.Current == AppLanguage.English ? "Below capture area" : "Под областью захвата"),
            new EnumOption<OverlayPosition>(OverlayPosition.AboveCaptureArea, Loc.Current == AppLanguage.English ? "Above capture area" : "Над областью захвата"),
            new EnumOption<OverlayPosition>(OverlayPosition.FixedBottomCenterOfScreen, Loc.Current == AppLanguage.English ? "Bottom-center of screen" : "Внизу экрана по центру"),
        };

        UiLanguageCombo.ItemsSource = new[]
        {
            new EnumOption<string>("ru", "Русский"),
            new EnumOption<string>("en", "English"),
        };

        _isLoading = true;
        LoadFromSettings();
        _isLoading = false;

        UpdateToggleButton();
    }

    private void ApplyLocalization()
    {
        Title = "KainoTranslator";
        SubtitleText.Text = Loc.S("Settings.Subtitle");
        SelectRegionButton.ToolTip = Loc.S("Settings.SelectRegionTooltip");
        CloseButton.Content = Loc.S("Settings.Close");
        SaveButton.Content = Loc.S("Settings.Save");

        TranslationTab.Header = Loc.S("Settings.Tab.Translation");
        HotkeysTab.Header = Loc.S("Settings.Tab.Hotkeys");
        AppearanceTab.Header = Loc.S("Settings.Tab.Appearance");
        OtherTab.Header = Loc.S("Settings.Tab.Other");

        OcrEngineHeaderText.Text = Loc.S("Settings.OcrEngineHeader");
        WindowsOcrLink.Inlines.Clear();
        WindowsOcrLink.Inlines.Add(Loc.S("Settings.WindowsOcrLink"));
        SourceLanguageHeaderText.Text = Loc.S("Settings.SourceLanguageHeader");
        TargetLanguageHeaderText.Text = Loc.S("Settings.TargetLanguageHeader");
        TranslatorHeaderText.Text = Loc.S("Settings.TranslatorHeader");

        DeepLHeaderText.Text = Loc.S("Settings.DeepLHeader");
        DeepLGetKeyRun.Text = Loc.S("Settings.DeepLGetKey");
        DeepLApiKeyLabel.Text = Loc.S("Settings.ApiKeyLabel");
        DeepLProText.Text = Loc.S("Settings.DeepLProCheck");

        GoogleHeaderText.Text = Loc.S("Settings.GoogleHeader");
        GoogleHintText.Text = Loc.S("Settings.GoogleHint");
        GoogleGetKeyLink.Inlines.Clear();
        GoogleGetKeyLink.Inlines.Add(Loc.S("Settings.GoogleGetKey"));
        GoogleApiKeyLabel.Text = Loc.S("Settings.ApiKeyOptionalLabel");

        YandexHeaderText.Text = Loc.S("Settings.YandexHeader");
        YandexGetKeyRun.Text = Loc.S("Settings.YandexGetKey");
        YandexApiKeyLabel.Text = Loc.S("Settings.ApiKeyLabel");
        YandexFolderIdLabel.Text = Loc.S("Settings.FolderIdLabel");

        PapagoHeaderText.Text = Loc.S("Settings.PapagoHeader");
        PapagoGetKeyRun.Text = Loc.S("Settings.PapagoGetKey");
        PapagoClientIdLabel.Text = Loc.S("Settings.ClientIdLabel");
        PapagoClientSecretLabel.Text = Loc.S("Settings.ClientSecretLabel");
        PapagoHintText.Text = Loc.S("Settings.PapagoHint");

        PollingHeaderText.Text = Loc.S("Settings.PollingHeader");
        PollingPrefixText.Text = Loc.S("Settings.PollingPrefix");
        SecondsText1.Text = Loc.S("Settings.Seconds");
        SecondsText2.Text = Loc.S("Settings.Seconds");
        SkipUnchangedText.Text = Loc.S("Settings.SkipUnchangedCheck");

        BehaviorHeaderText.Text = Loc.S("Settings.BehaviorHeader");
        AutoStartText.Text = Loc.S("Settings.AutoStartCheck");

        HotkeysHeaderText.Text = Loc.S("Settings.HotkeysHeader");
        HotkeysHintText.Text = Loc.S("Settings.HotkeysHint");
        HotkeySelectRegionLabel.Text = Loc.S("Settings.HotkeySelectRegion");
        HotkeyToggleLabel.Text = Loc.S("Settings.HotkeyToggle");
        HotkeyOnceLabel.Text = Loc.S("Settings.HotkeyOnce");
        HotkeyOpenSettingsLabel.Text = Loc.S("Settings.HotkeyOpenSettings");

        PositionSizeHeaderText.Text = Loc.S("Settings.PositionSizeHeader");
        PositionLabel.Text = Loc.S("Settings.PositionLabel");
        FontSizeLabel.Text = Loc.S("Settings.FontSizeLabel");
        MaxWidthLabel.Text = Loc.S("Settings.MaxWidthLabel");

        ColorHeaderText.Text = Loc.S("Settings.ColorHeader");
        TextColorLabel.Text = Loc.S("Settings.TextColorLabel");
        BgColorLabel.Text = Loc.S("Settings.BgColorLabel");
        TextColorPreview.ToolTip = Loc.S("Settings.PickColorTooltip");
        BgColorPreview.ToolTip = Loc.S("Settings.PickColorTooltip");
        BgOpacityLabelRun.Text = Loc.S("Settings.BgOpacityLabel");
        BgOpacityHintText.Text = Loc.S("Settings.BgOpacityHint");

        ReadabilityHeaderText.Text = Loc.S("Settings.ReadabilityHeader");
        ReadabilityHintText.Text = Loc.S("Settings.ReadabilityHint");
        OutlineNoneRadio.Content = Loc.S("Settings.OutlineNone");
        OutlineOutlineRadio.Content = Loc.S("Settings.OutlineOutline");
        OutlineShadowRadio.Content = Loc.S("Settings.OutlineShadow");

        WindowBehaviorHeaderText.Text = Loc.S("Settings.WindowBehaviorHeader");
        ClickThroughText.Text = Loc.S("Settings.ClickThroughCheck");
        AutoHideText.Text = Loc.S("Settings.AutoHideCheck");
        AutoHidePrefixText.Text = Loc.S("Settings.AutoHidePrefix");

        LanguageHeaderText.Text = Loc.S("Settings.LanguageHeader");
        LanguageHintText.Text = Loc.S("Settings.LanguageHint");

        TesseractHeaderText.Text = Loc.S("Settings.TesseractHeader");
        TesseractGetFilesRun.Text = Loc.S("Settings.TesseractGetFiles");
        TesseractFolderLabel.Text = Loc.S("Settings.TesseractFolderLabel");

        EasyOcrHeaderText.Text = Loc.S("Settings.EasyOcrHeader");
        EasyOcrNeedPythonRun.Text = Loc.S("Settings.EasyOcrNeedPython");
        EasyOcrThenRunRun.Text = Loc.S("Settings.EasyOcrThenRun");
        EasyOcrPathLabel.Text = Loc.S("Settings.EasyOcrPathLabel");
        EasyOcrGpuText.Text = Loc.S("Settings.EasyOcrGpuCheck");

        StartupHeaderText.Text = Loc.S("Settings.StartupHeader");
        RunAtStartupText.Text = Loc.S("Settings.RunAtStartupCheck");

        ProxyHeaderText.Text = Loc.S("Settings.ProxyHeader");
        ProxyHintText.Text = Loc.S("Settings.ProxyHint");
        RotateProxyText.Text = Loc.S("Settings.RotateProxyCheck");
    }

    private void LoadFromSettings()
    {
        OcrEngineCombo.SelectedItem = ((IEnumerable<EnumOption<OcrEngineKind>>)OcrEngineCombo.ItemsSource)
            .First(o => o.Value == _working.OcrEngine);
        PopulateSourceLanguages();
        SourceLanguageCombo.SelectedItem = LanguageCatalog.TryByKey(_working.SourceLanguageKey);

        TranslatorCombo.SelectedItem = ((IEnumerable<EnumOption<TranslatorKind>>)TranslatorCombo.ItemsSource)
            .First(o => o.Value == _working.Translator);
        PopulateTargetLanguages();
        TargetLanguageCombo.SelectedItem = LanguageCatalog.TryByKey(_working.TargetLanguageKey);
        UpdateTranslatorCardVisibility();
        UpdateWindowsOcrLinkVisibility();

        PollingIntervalBox.Text = (_working.PollingIntervalMs / 1000.0).ToString("0.##");
        SkipUnchangedCheck.IsChecked = _working.SkipOcrWhenFrameUnchanged;
        AutoStartAfterRegionSelectCheck.IsChecked = _working.AutoStartTranslationAfterRegionSelect;

        SelectRegionHotkeyBox.Text = _working.Hotkeys.SelectRegion.Combo;
        ToggleHotkeyBox.Text = _working.Hotkeys.ToggleTranslation.Combo;
        OneTimeHotkeyBox.Text = _working.Hotkeys.OneTimeTranslate.Combo;
        OpenSettingsHotkeyBox.Text = _working.Hotkeys.OpenSettings.Combo;

        DeepLKeyBox.Text = _working.ApiKeys.DeepLApiKey;
        DeepLProCheck.IsChecked = _working.ApiKeys.DeepLUseProEndpoint;
        GoogleKeyBox.Text = _working.ApiKeys.GoogleApiKey;
        YandexKeyBox.Text = _working.ApiKeys.YandexApiKey;
        YandexFolderBox.Text = _working.ApiKeys.YandexFolderId;
        PapagoIdBox.Text = _working.ApiKeys.PapagoClientId;
        PapagoSecretBox.Text = _working.ApiKeys.PapagoClientSecret;

        ProxyListBox.Text = string.Join(Environment.NewLine, _working.ProxyList);
        RotateProxyCheck.IsChecked = _working.RotateProxyPerRequest;

        var positionOptions = (IEnumerable<EnumOption<OverlayPosition>>)OverlayPositionCombo.ItemsSource;
        OverlayPositionCombo.SelectedItem = positionOptions.FirstOrDefault(o => o.Value == _working.Overlay.Position)
            ?? positionOptions.First(o => o.Value == OverlayPosition.FixedTopCenterOfScreen);
        FontSizeBox.Text = _working.Overlay.FontSize.ToString();
        MaxWidthBox.Text = _working.Overlay.MaxWidth.ToString();
        ClickThroughCheck.IsChecked = _working.Overlay.ClickThrough;

        TextColorBox.Text = _working.Overlay.TextColor;
        BgColorBox.Text = _working.Overlay.BackgroundColorRgb;
        BgOpacitySlider.Value = _working.Overlay.BackgroundTransparencyPercent;
        UpdateBgOpacityLabel();
        UpdateColorPreview(TextColorPreview, TextColorBox.Text);
        UpdateColorPreview(BgColorPreview, BgColorBox.Text);

        switch (_working.Overlay.OutlineMode)
        {
            case TextOutlineMode.None: OutlineNoneRadio.IsChecked = true; break;
            case TextOutlineMode.Shadow: OutlineShadowRadio.IsChecked = true; break;
            default: OutlineOutlineRadio.IsChecked = true; break;
        }

        AutoHideCheck.IsChecked = _working.Overlay.AutoHideEnabled;
        AutoHideSecondsBox.Text = _working.Overlay.AutoHideSeconds.ToString();
        AutoHideSecondsBox.IsEnabled = _working.Overlay.AutoHideEnabled;

        TesseractPathBox.Text = _working.TesseractDataPath;
        EasyOcrPythonBox.Text = _working.EasyOcrPythonExePath;
        EasyOcrGpuCheck.IsChecked = _working.EasyOcrUseGpu;
        RunAtStartupCheck.IsChecked = _working.RunAtWindowsStartup;

        var uiLangOptions = (IEnumerable<EnumOption<string>>)UiLanguageCombo.ItemsSource;
        UiLanguageCombo.SelectedItem = uiLangOptions.FirstOrDefault(o => o.Value == _working.UiLanguage) ?? uiLangOptions.First();

        UpdateOcrHint();
        UpdateTranslatorHint();
    }

    private void PopulateSourceLanguages()
    {
        var kind = (OcrEngineCombo.SelectedItem as EnumOption<OcrEngineKind>)?.Value ?? _working.OcrEngine;
        SourceLanguageCombo.ItemsSource = LanguageCatalog.ForOcrEngine(kind).OrderBy(l => l.DisplayName).ToList();
        SourceLanguageCombo.DisplayMemberPath = nameof(LanguageInfo.DisplayName);
    }

    private void PopulateTargetLanguages()
    {
        var kind = (TranslatorCombo.SelectedItem as EnumOption<TranslatorKind>)?.Value ?? _working.Translator;
        TargetLanguageCombo.ItemsSource = LanguageCatalog.ForTranslator(kind).OrderBy(l => l.DisplayName).ToList();
        TargetLanguageCombo.DisplayMemberPath = nameof(LanguageInfo.DisplayName);
    }

    private void OnOcrEngineChanged(object sender, SelectionChangedEventArgs e)
    {
        PopulateSourceLanguages();
        UpdateOcrHint();
        UpdateWindowsOcrLinkVisibility();
    }

    private void OnTranslatorChanged(object sender, SelectionChangedEventArgs e)
    {
        PopulateTargetLanguages();
        UpdateTranslatorHint();
        UpdateTranslatorCardVisibility();
    }

    private void UpdateTranslatorCardVisibility()
    {
        var kind = (TranslatorCombo.SelectedItem as EnumOption<TranslatorKind>)?.Value ?? _working.Translator;
        DeepLCard.Visibility = kind == TranslatorKind.DeepL ? Visibility.Visible : Visibility.Collapsed;
        GoogleCard.Visibility = kind == TranslatorKind.Google ? Visibility.Visible : Visibility.Collapsed;
        YandexCard.Visibility = kind == TranslatorKind.Yandex ? Visibility.Visible : Visibility.Collapsed;
        PapagoCard.Visibility = kind == TranslatorKind.Papago ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateWindowsOcrLinkVisibility()
    {
        var kind = (OcrEngineCombo.SelectedItem as EnumOption<OcrEngineKind>)?.Value ?? _working.OcrEngine;
        WindowsOcrLinkBlock.Visibility = kind == OcrEngineKind.WindowsOcr ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateOcrHint()
    {
        var kind = (OcrEngineCombo.SelectedItem as EnumOption<OcrEngineKind>)?.Value;
        OcrEngineHint.Text = kind switch
        {
            OcrEngineKind.WindowsOcr => Loc.S("Ocr.WindowsOcr"),
            OcrEngineKind.Tesseract => Loc.S("Ocr.Tesseract"),
            OcrEngineKind.EasyOcr => Loc.S("Ocr.EasyOcr"),
            _ => ""
        };
    }

    private void UpdateTranslatorHint()
    {
        var kind = (TranslatorCombo.SelectedItem as EnumOption<TranslatorKind>)?.Value;
        TranslatorHint.Text = kind switch
        {
            TranslatorKind.DeepL => Loc.S("Translator.DeepL"),
            TranslatorKind.Yandex => Loc.S("Translator.Yandex"),
            TranslatorKind.Papago => Loc.S("Translator.Papago"),
            TranslatorKind.Google => Loc.S("Translator.Google"),
            _ => ""
        };
    }

    // ---------- Header actions ----------
    private void OnSelectRegionClick(object sender, RoutedEventArgs e) => RequestSelectRegion?.Invoke();

    private void OnToggleClick(object sender, RoutedEventArgs e) => RequestToggleTranslation?.Invoke();

    /// <summary>Called by App whenever the engine's real running state changes (toggled here,
    /// via a hotkey, or blocked by the "no region selected" guard) - keeps this button always
    /// truthful instead of guessing locally.</summary>
    public void SetRunningState(bool isRunning)
    {
        _isTranslationRunning = isRunning;
        UpdateToggleButton();
    }

    private void UpdateToggleButton()
    {
        ToggleButtonIcon.Text = _isTranslationRunning ? "⏸" : "▶";
        ToggleButton.ToolTip = _isTranslationRunning ? Loc.S("Settings.ToggleOffTooltip") : Loc.S("Settings.ToggleOnTooltip");
    }

    // ---------- Overlay color/opacity previews ----------
    private void OnTextColorChanged(object sender, TextChangedEventArgs e) => UpdateColorPreview(TextColorPreview, TextColorBox.Text);
    private void OnBgColorChanged(object sender, TextChangedEventArgs e) => UpdateColorPreview(BgColorPreview, BgColorBox.Text);

    private static void UpdateColorPreview(Border preview, string hex)
    {
        try
        {
            preview.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        }
        catch
        {
            preview.Background = System.Windows.Media.Brushes.Transparent;
        }
    }

    private void OnBgOpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdateBgOpacityLabel();

    private void UpdateBgOpacityLabel()
    {
        if (BgOpacityValueText != null) BgOpacityValueText.Text = $"{(int)BgOpacitySlider.Value}%";
    }

    private void OnPickTextColorClick(object sender, MouseButtonEventArgs e) => PickColorInto(TextColorBox);
    private void OnPickBgColorClick(object sender, MouseButtonEventArgs e) => PickColorInto(BgColorBox);

    private static void PickColorInto(TextBox target)
    {
        var dialog = new System.Windows.Forms.ColorDialog { FullOpen = true, AnyColor = true };
        try
        {
            var current = (Color)ColorConverter.ConvertFromString(target.Text);
            dialog.Color = System.Drawing.Color.FromArgb(current.R, current.G, current.B);
        }
        catch { /* keep the dialog's own default if the current text isn't a valid color yet */ }

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            var c = dialog.Color;
            target.Text = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        }
    }

    private void OnAutoHideCheckChanged(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        AutoHideSecondsBox.IsEnabled = AutoHideCheck.IsChecked == true;
    }

    // ---------- Browse dialogs ----------
    private void OnBrowseTesseractFolderClick(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = Loc.S("Settings.TesseractFolderLabel") };
        if (dlg.ShowDialog() == true) TesseractPathBox.Text = dlg.FolderName;
    }

    private void OnBrowseEasyOcrPythonClick(object sender, RoutedEventArgs e)
    {
        var filter = Loc.Current == AppLanguage.English
            ? "Programs (*.exe)|*.exe|All files (*.*)|*.*"
            : "Программы (*.exe)|*.exe|Все файлы (*.*)|*.*";
        var dlg = new OpenFileDialog { Title = Loc.S("Settings.EasyOcrPathLabel"), Filter = filter };
        if (dlg.ShowDialog() == true) EasyOcrPythonBox.Text = dlg.FileName;
    }

    // ---------- Hyperlinks ----------
    private void LinkOpener_OnRequestNavigate(object sender, RequestNavigateEventArgs e) => LinkOpener.OpenInBrowser(sender, e);

    // ---------- Hotkey capture ----------
    private void OnHotkeyBoxPreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return; // wait for a non-modifier key

        var modifiers = Keyboard.Modifiers;
        if (modifiers == ModifierKeys.None) return; // require at least one modifier for a system-wide hotkey

        var sb = new StringBuilder();
        if (modifiers.HasFlag(ModifierKeys.Control)) sb.Append("Ctrl+");
        if (modifiers.HasFlag(ModifierKeys.Alt)) sb.Append("Alt+");
        if (modifiers.HasFlag(ModifierKeys.Shift)) sb.Append("Shift+");
        if (modifiers.HasFlag(ModifierKeys.Windows)) sb.Append("Win+");
        sb.Append(key);

        ((TextBox)sender).Text = sb.ToString();
    }

    // ---------- Save / Close ----------
    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        try
        {
            ApplyUiToWorking();
        }
        catch (Exception ex)
        {
            StatusText.Text = (Loc.Current == AppLanguage.English ? "Invalid value: " : "Некорректное значение: ") + ex.Message;
            StatusText.Foreground = System.Windows.Media.Brushes.IndianRed;
            return;
        }

        _working.Save();
        ApplyRunAtStartup(_working.RunAtWindowsStartup);
        SettingsSaved?.Invoke(_working);

        StatusText.Foreground = System.Windows.Media.Brushes.Gray;
        StatusText.Text = Loc.S("Settings.Saved");
        ScheduleStatusTextClear();
    }

    private DispatcherTimer? _statusClearTimer;

    private void ScheduleStatusTextClear()
    {
        _statusClearTimer?.Stop();
        _statusClearTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _statusClearTimer.Tick += (_, _) =>
        {
            _statusClearTimer!.Stop();
            StatusText.Text = "";
        };
        _statusClearTimer.Start();
    }

    private void ApplyUiToWorking()
    {
        _working.OcrEngine = ((EnumOption<OcrEngineKind>)OcrEngineCombo.SelectedItem).Value;
        _working.SourceLanguageKey = ((LanguageInfo)SourceLanguageCombo.SelectedItem).Key;

        _working.Translator = ((EnumOption<TranslatorKind>)TranslatorCombo.SelectedItem).Value;
        _working.TargetLanguageKey = ((LanguageInfo)TargetLanguageCombo.SelectedItem).Key;

        var seconds = double.Parse(PollingIntervalBox.Text);
        _working.PollingIntervalMs = Math.Clamp((int)Math.Round(seconds * 1000), 100, 5000);
        _working.SkipOcrWhenFrameUnchanged = SkipUnchangedCheck.IsChecked == true;
        _working.AutoStartTranslationAfterRegionSelect = AutoStartAfterRegionSelectCheck.IsChecked == true;

        RequireHotkey(SelectRegionHotkeyBox.Text);
        RequireHotkey(ToggleHotkeyBox.Text);
        RequireHotkey(OneTimeHotkeyBox.Text);
        RequireHotkey(OpenSettingsHotkeyBox.Text);
        _working.Hotkeys.SelectRegion.Combo = SelectRegionHotkeyBox.Text;
        _working.Hotkeys.ToggleTranslation.Combo = ToggleHotkeyBox.Text;
        _working.Hotkeys.OneTimeTranslate.Combo = OneTimeHotkeyBox.Text;
        _working.Hotkeys.OpenSettings.Combo = OpenSettingsHotkeyBox.Text;

        _working.ApiKeys.DeepLApiKey = DeepLKeyBox.Text.Trim();
        _working.ApiKeys.DeepLUseProEndpoint = DeepLProCheck.IsChecked == true;
        _working.ApiKeys.GoogleApiKey = GoogleKeyBox.Text.Trim();
        _working.ApiKeys.YandexApiKey = YandexKeyBox.Text.Trim();
        _working.ApiKeys.YandexFolderId = YandexFolderBox.Text.Trim();
        _working.ApiKeys.PapagoClientId = PapagoIdBox.Text.Trim();
        _working.ApiKeys.PapagoClientSecret = PapagoSecretBox.Text.Trim();

        _working.ProxyList = ProxyListBox.Text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => l.Length > 0)
            .ToList();
        _working.RotateProxyPerRequest = RotateProxyCheck.IsChecked == true;

        _working.Overlay.Position = ((EnumOption<OverlayPosition>)OverlayPositionCombo.SelectedItem).Value;
        _working.Overlay.FontSize = double.Parse(FontSizeBox.Text);
        _working.Overlay.MaxWidth = int.Parse(MaxWidthBox.Text);
        _working.Overlay.ClickThrough = ClickThroughCheck.IsChecked == true;

        _working.Overlay.TextColor = ValidateColor(TextColorBox.Text, Loc.S("Settings.TextColorLabel"));
        _working.Overlay.BackgroundColorRgb = ValidateColor(BgColorBox.Text, Loc.S("Settings.BgColorLabel"));
        _working.Overlay.BackgroundTransparencyPercent = (int)BgOpacitySlider.Value;

        _working.Overlay.OutlineMode = OutlineShadowRadio.IsChecked == true ? TextOutlineMode.Shadow
            : OutlineNoneRadio.IsChecked == true ? TextOutlineMode.None
            : TextOutlineMode.Outline;

        _working.Overlay.AutoHideEnabled = AutoHideCheck.IsChecked == true;
        _working.Overlay.AutoHideSeconds = Math.Clamp(int.Parse(AutoHideSecondsBox.Text), 1, 120);

        _working.TesseractDataPath = TesseractPathBox.Text.Trim();
        _working.EasyOcrPythonExePath = EasyOcrPythonBox.Text.Trim();
        _working.EasyOcrUseGpu = EasyOcrGpuCheck.IsChecked == true;
        _working.RunAtWindowsStartup = RunAtStartupCheck.IsChecked == true;

        _working.UiLanguage = ((EnumOption<string>)UiLanguageCombo.SelectedItem).Value;
    }

    private static string ValidateColor(string hex, string fieldName)
    {
        try
        {
            ColorConverter.ConvertFromString(hex);
            return hex;
        }
        catch
        {
            var message = Loc.Current == AppLanguage.English
                ? $"\"{fieldName}\": \"{hex}\" doesn't look like a color (use #RRGGBB format)."
                : $"«{fieldName}»: «{hex}» не похоже на цвет (используйте формат #RRGGBB).";
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireHotkey(string combo)
    {
        if (!Hotkeys.HotkeyManager.TryParse(combo, out _, out _))
        {
            var message = Loc.Current == AppLanguage.English
                ? $"\"{combo}\" isn't a valid hotkey (needs a modifier + a key)."
                : $"«{combo}» — не подходит для горячей клавиши (нужен модификатор + клавиша).";
            throw new InvalidOperationException(message);
        }
    }

    private static void ApplyRunAtStartup(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key == null) return;
            if (enabled)
            {
                var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                if (exePath != null) key.SetValue(RunKeyName, $"\"{exePath}\"");
            }
            else
            {
                key.DeleteValue(RunKeyName, throwOnMissingValue: false);
            }
        }
        catch { /* best effort - not worth failing Save over */ }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
