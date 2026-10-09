using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using System.Windows.Threading;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using ScreenTranslator.Localization;
using ScreenTranslator.Models;
using ScreenTranslator.Ocr;
using ScreenTranslator.Translation;
using TextBox = System.Windows.Controls.TextBox;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;

namespace ScreenTranslator.SettingsUi;

public partial class SettingsWindow : Window
{
    public event Action<AppSettings>? SettingsSaved;
    public event Action? RequestSelectRegion;
    public event Action? RequestToggleTranslation;

    private readonly ObservableCollection<GlossaryEntryViewModel> _glossaryRows = new();

    private readonly AppSettings _working;
    private bool _isTranslationRunning;
    private bool _isLoading; // suppresses color-preview handlers firing while populating fields
    private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string RunKeyName = "KainoTranslator";

    // Shared across "Обновить список" clicks so repeated fetches don't exhaust sockets; this
    // window is the only thing that does model-list fetches, so there's nothing else competing
    // for it that would call for per-call disposal instead.
    private static readonly HttpClient s_modelFetchHttpClient = new();

    public SettingsWindow(AppSettings current, bool isTranslationRunning = false)
    {
        InitializeComponent();
        _isTranslationRunning = isTranslationRunning;
        StateChanged += (_, _) => UpdateMaximizeRestoreGlyph();
        UpdateMaximizeRestoreGlyph();

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
            new EnumOption<TranslatorKind>(TranslatorKind.OpenAiCompatible, "OpenAI-совместимый"),
        };

        OpenAiCompatibleProviderCombo.ItemsSource = new[]
        {
            new EnumOption<string>(OpenAiCompatiblePresets.Gemini, "Gemini"),
            new EnumOption<string>(OpenAiCompatiblePresets.DeepSeek, "DeepSeek"),
            new EnumOption<string>(OpenAiCompatiblePresets.OpenAi, "ChatGPT (OpenAI)"),
            new EnumOption<string>(OpenAiCompatiblePresets.Custom, Loc.S("Settings.OpenAiCompatibleCustomProvider")),
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

        ThemeCombo.ItemsSource = new[]
        {
            new EnumOption<string>("System", Loc.S("Settings.ThemeSystem")),
            new EnumOption<string>("Light", Loc.S("Settings.ThemeLight")),
            new EnumOption<string>("Dark", Loc.S("Settings.ThemeDark")),
        };

        GlossaryItemsControl.ItemsSource = _glossaryRows;

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
        GlossaryTab.Header = Loc.S("Settings.Tab.Glossary");
        OtherTab.Header = Loc.S("Settings.Tab.Other");

        GlossaryHeaderText.Text = Loc.S("Settings.GlossaryHeader");
        GlossaryHintText.Text = Loc.S("Settings.GlossaryHint");
        GlossarySourceColumnLabel.Text = Loc.S("Settings.GlossarySourceColumn");
        GlossaryTargetColumnLabel.Text = Loc.S("Settings.GlossaryTargetColumn");
        AddGlossaryEntryButton.Content = Loc.S("Settings.GlossaryAddButton");
        GlossaryProfileNameBox.ToolTip = Loc.S("Settings.GlossaryProfileNameHint");
        AddGlossaryProfileButton.Content = Loc.S("Settings.GlossaryProfileAddButton");
        DeleteGlossaryProfileButton.Content = Loc.S("Settings.GlossaryProfileDeleteButton");
        ExportGlossaryButton.Content = Loc.S("Settings.GlossaryExportButton");
        ImportGlossaryButton.Content = Loc.S("Settings.GlossaryImportButton");

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

        OpenAiCompatibleHeaderText.Text = Loc.S("Settings.OpenAiCompatibleHeader");
        OpenAiCompatibleHintText.Text = Loc.S("Settings.OpenAiCompatibleHint");
        OpenAiCompatibleProviderLabel.Text = Loc.S("Settings.OpenAiCompatibleProviderLabel");
        OpenAiCompatibleKeyLabel.Text = Loc.S("Settings.ApiKeyLabel");
        OpenAiCompatibleUrlLabel.Text = Loc.S("Settings.OpenAiCompatibleUrlLabel");
        OpenAiCompatibleModelLabel.Text = Loc.S("Settings.OpenAiCompatibleModelLabel");
        OpenAiCompatibleRefreshModelsText.Text = Loc.S("Settings.OpenAiCompatibleRefreshModels");
        AiContextLabel.Text = Loc.S("Settings.AiContextLabel");
        AiContextHintText.Text = Loc.S("Settings.AiContextHint");
        AiUseGlossaryCheck.Content = Loc.S("Settings.AiUseGlossary");
        AiUseHistoryCheck.Content = Loc.S("Settings.AiUseHistory");

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
        HotkeyRetranslateLabel.Text = Loc.S("Settings.HotkeyRetranslate");
        HotkeyOpenSettingsLabel.Text = Loc.S("Settings.HotkeyOpenSettings");

        HoverSectionHeaderText.Text = Loc.S("Settings.HoverSectionHeader");
        HoverSectionHintText.Text = Loc.S("Settings.HoverSectionHint");
        HoverModeOffRadio.Content = Loc.S("Settings.HoverModeOff");
        HoverModeImmediateRadio.Content = Loc.S("Settings.HoverModeImmediate");
        HoverModeConfirmRadio.Content = Loc.S("Settings.HoverModeConfirm");

        DisplayModeHeaderText.Text = Loc.S("Settings.DisplayModeHeader");
        DisplayModePanelRadio.Content = Loc.S("Settings.DisplayModePanel");
        DisplayModeReplaceRadio.Content = Loc.S("Settings.DisplayModeReplace");
        DisplayModeHintText.Text = Loc.S("Settings.DisplayModeReplaceHint");

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
        DismissOnClickOrSpaceText.Text = Loc.S("Settings.DismissOnClickOrSpaceCheck");
        AutoHidePrefixText.Text = Loc.S("Settings.AutoHidePrefix");

        LanguageHeaderText.Text = Loc.S("Settings.LanguageHeader");
        LanguageHintText.Text = Loc.S("Settings.LanguageHint");
        ThemeLabel.Text = Loc.S("Settings.ThemeLabel");
        ThemeHintText.Text = Loc.S("Settings.ThemeHint");

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
        RetranslateHotkeyBox.Text = _working.Hotkeys.Retranslate.Combo;
        OpenSettingsHotkeyBox.Text = _working.Hotkeys.OpenSettings.Combo;
        HoverTriggerKeyBox.Text = _working.HoverTranslate.TriggerKey;
        if (string.IsNullOrEmpty(_working.HoverTranslate.TriggerKey))
        {
            HoverModeOffRadio.IsChecked = true;
        }
        else if (_working.HoverTranslate.Mode == HoverTranslateMode.ConfirmClick)
        {
            HoverModeConfirmRadio.IsChecked = true;
        }
        else
        {
            HoverModeImmediateRadio.IsChecked = true;
        }

        DeepLKeyBox.Text = _working.ApiKeys.DeepLApiKey;
        DeepLProCheck.IsChecked = _working.ApiKeys.DeepLUseProEndpoint;
        GoogleKeyBox.Text = _working.ApiKeys.GoogleApiKey;
        YandexKeyBox.Text = _working.ApiKeys.YandexApiKey;
        YandexFolderBox.Text = _working.ApiKeys.YandexFolderId;
        PapagoIdBox.Text = _working.ApiKeys.PapagoClientId;
        PapagoSecretBox.Text = _working.ApiKeys.PapagoClientSecret;
        var providerOptions = (IEnumerable<EnumOption<string>>)OpenAiCompatibleProviderCombo.ItemsSource;
        OpenAiCompatibleProviderCombo.SelectedItem = providerOptions.FirstOrDefault(o => o.Value == _working.ApiKeys.OpenAiCompatibleProvider) ?? providerOptions.First();
        OpenAiCompatibleUrlBox.Text = _working.ApiKeys.OpenAiCompatibleBaseUrl;
        OpenAiCompatibleModelCombo.Text = _working.ApiKeys.OpenAiCompatibleModel;
        OpenAiCompatibleKeyBox.Text = _working.ApiKeys.OpenAiCompatibleApiKey;
        AiContextBox.Text = _working.Ai.GameContext;
        AiUseGlossaryCheck.IsChecked = _working.Ai.UseGlossary;
        AiUseHistoryCheck.IsChecked = _working.Ai.UseDialogueHistory;
        UpdateOpenAiCompatibleProviderUi();

        ProxyListBox.Text = string.Join(Environment.NewLine, _working.ProxyList);
        RotateProxyCheck.IsChecked = _working.RotateProxyPerRequest;

        if (_working.Overlay.DisplayMode == OverlayDisplayMode.ReplaceText) DisplayModeReplaceRadio.IsChecked = true;
        else DisplayModePanelRadio.IsChecked = true;

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
        DismissOnClickOrSpaceCheck.IsChecked = _working.Overlay.DismissOnClickOrSpace;

        TesseractPathBox.Text = _working.TesseractDataPath;
        EasyOcrPythonBox.Text = _working.EasyOcrPythonExePath;
        EasyOcrGpuCheck.IsChecked = _working.EasyOcrUseGpu;
        RunAtStartupCheck.IsChecked = _working.RunAtWindowsStartup;

        var uiLangOptions = (IEnumerable<EnumOption<string>>)UiLanguageCombo.ItemsSource;
        UiLanguageCombo.SelectedItem = uiLangOptions.FirstOrDefault(o => o.Value == _working.UiLanguage) ?? uiLangOptions.First();

        var themeOptions = (IEnumerable<EnumOption<string>>)ThemeCombo.ItemsSource;
        ThemeCombo.SelectedItem = themeOptions.FirstOrDefault(o => o.Value == _working.UiTheme) ?? themeOptions.First();

        LoadGlossaryProfilesUi();

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
        OpenAiCompatibleCard.Visibility = kind == TranslatorKind.OpenAiCompatible ? Visibility.Visible : Visibility.Collapsed;
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
            TranslatorKind.OpenAiCompatible => Loc.S("Translator.OpenAiCompatible"),
            _ => ""
        };
    }

    // ---------- OpenAI-compatible provider ----------

    private void OnOpenAiCompatibleProviderChanged(object sender, SelectionChangedEventArgs e) => UpdateOpenAiCompatibleProviderUi();

    /// <summary>Shows the raw URL field only for "Custom" (known providers' URLs are fixed - see
    /// OpenAiCompatiblePresets - so showing an editable field for them would just invite someone
    /// to "fix" it into something wrong). Also points the "get a key" link at that provider's own
    /// key page, hiding it entirely for Custom since there's no single page to send people to.</summary>
    private void UpdateOpenAiCompatibleProviderUi()
    {
        if (OpenAiCompatibleUrlPanel is null) return; // fires while InitializeComponent is still wiring up controls

        var provider = (OpenAiCompatibleProviderCombo.SelectedItem as EnumOption<string>)?.Value ?? OpenAiCompatiblePresets.Gemini;
        var isCustom = provider == OpenAiCompatiblePresets.Custom;

        OpenAiCompatibleUrlPanel.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;

        var keyPageUrl = OpenAiCompatiblePresets.KeyPageUrl(provider);
        OpenAiCompatibleKeyLinkBlock.Visibility = keyPageUrl is null ? Visibility.Collapsed : Visibility.Visible;
        if (keyPageUrl is not null)
        {
            OpenAiCompatibleKeyLink.NavigateUri = new Uri(keyPageUrl);
            OpenAiCompatibleKeyLinkRun.Text = string.Format(Loc.S("Settings.OpenAiCompatibleGetKeyFor"), provider);
        }

        OpenAiCompatibleModelHintText.Text = Loc.S("Settings.OpenAiCompatibleModelHint");
    }

    /// <summary>Fetches the provider's model list via the standard OpenAI GET /models endpoint
    /// and offers it as a dropdown, instead of the person having to know and type an exact model
    /// id by hand (those change often enough that a hardcoded list here would go stale). Typing
    /// one in manually still works regardless - this is purely a convenience on top.</summary>
    private async void OnRefreshOpenAiCompatibleModelsClick(object sender, RoutedEventArgs e)
    {
        var provider = (OpenAiCompatibleProviderCombo.SelectedItem as EnumOption<string>)?.Value ?? OpenAiCompatiblePresets.Gemini;
        var baseUrl = OpenAiCompatiblePresets.ResolveBaseUrl(provider, OpenAiCompatibleUrlBox.Text.Trim());
        var key = OpenAiCompatibleKeyBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            OpenAiCompatibleModelHintText.Text = Loc.S("Settings.OpenAiCompatibleNoUrl");
            return;
        }

        OpenAiCompatibleRefreshModelsButton.IsEnabled = false;
        var previousText = OpenAiCompatibleModelCombo.Text;
        OpenAiCompatibleModelHintText.Text = Loc.S("Settings.OpenAiCompatibleFetchingModels");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, baseUrl.TrimEnd('/') + "/models");
            if (!string.IsNullOrWhiteSpace(key))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

            using var response = await s_modelFetchHttpClient.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                OpenAiCompatibleModelHintText.Text = string.Format(Loc.S("Settings.OpenAiCompatibleFetchFailed"), (int)response.StatusCode);
                return;
            }

            using var doc = JsonDocument.Parse(body);
            var ids = doc.RootElement.GetProperty("data").EnumerateArray()
                .Select(el => el.TryGetProperty("id", out var idEl) ? idEl.GetString() : null)
                .Where(id => !string.IsNullOrEmpty(id))
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (ids.Count == 0)
            {
                OpenAiCompatibleModelHintText.Text = Loc.S("Settings.OpenAiCompatibleNoModels");
                return;
            }

            OpenAiCompatibleModelCombo.ItemsSource = ids;
            OpenAiCompatibleModelCombo.Text = previousText; // keep whatever was typed/selected - this only adds the dropdown, it doesn't pick for them
            OpenAiCompatibleModelHintText.Text = string.Format(Loc.S("Settings.OpenAiCompatibleModelsLoaded"), ids.Count);
        }
        catch (Exception ex)
        {
            OpenAiCompatibleModelHintText.Text = string.Format(Loc.S("Settings.OpenAiCompatibleFetchError"), ex.Message);
        }
        finally
        {
            OpenAiCompatibleRefreshModelsButton.IsEnabled = true;
        }
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

    // With "replace text" the translation is drawn where the original was, in the original's own
    // colour and size, so the panel's position/size and colour settings don't apply.
    private void OnDisplayModeChanged(object sender, RoutedEventArgs e)
    {
        var replace = DisplayModeReplaceRadio.IsChecked == true;
        PositionCard.IsEnabled = !replace;
        ColorCard.IsEnabled = !replace;
        PositionCard.Opacity = replace ? 0.45 : 1;
        ColorCard.Opacity = replace ? 0.45 : 1;
    }

    private void OnAutoHideCheckChanged(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        AutoHideSecondsBox.IsEnabled = AutoHideCheck.IsChecked == true;
    }

    // ---------- Glossary ----------
    private void OnAddGlossaryEntryClick(object sender, RoutedEventArgs e)
    {
        _glossaryRows.Add(new GlossaryEntryViewModel());
    }

    private void OnRemoveGlossaryEntryClick(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is GlossaryEntryViewModel row)
            _glossaryRows.Remove(row);
    }

    // ---------- Glossary profiles ----------
    private string _editingGlossaryProfileId = "";
    private bool _suppressGlossaryProfileNameEvent;
    private static readonly JsonSerializerOptions GlossaryFileJsonOptions = new() { WriteIndented = true };

    private void LoadGlossaryProfilesUi()
    {
        var active = _working.GlossaryProfiles.FirstOrDefault(p => p.Id == _working.ActiveGlossaryProfileId)
                     ?? _working.GlossaryProfiles[0];
        SwitchEditingProfile(active);
    }

    private void LoadGlossaryRowsFrom(GlossaryProfile profile)
    {
        _glossaryRows.Clear();
        foreach (var entry in profile.Entries)
            _glossaryRows.Add(new GlossaryEntryViewModel { SourceTerm = entry.SourceTerm, TargetTerm = entry.TargetTerm });
    }

    /// <summary>Writes the currently-shown rows back into the given profile's Entries. Called
    /// before switching/adding/deleting/saving so in-progress edits are never silently lost.</summary>
    private void CommitGlossaryRowsToProfile(string profileId)
    {
        var profile = _working.GlossaryProfiles.FirstOrDefault(p => p.Id == profileId);
        if (profile is null) return;

        profile.Entries = _glossaryRows
            .Where(r => !string.IsNullOrWhiteSpace(r.SourceTerm))
            .Select(r => new GlossaryEntry { SourceTerm = r.SourceTerm.Trim(), TargetTerm = r.TargetTerm.Trim() })
            .ToList();
    }

    /// <summary>Switches editing to the given profile. Callers that just added/removed a profile
    /// from _working.GlossaryProfiles call this afterwards to bring the UI in line.</summary>
    private void SwitchEditingProfile(GlossaryProfile profile)
    {
        _editingGlossaryProfileId = profile.Id;
        LoadGlossaryRowsFrom(profile);

        _suppressGlossaryProfileNameEvent = true;
        GlossaryProfileNameBox.Text = profile.Name;
        _suppressGlossaryProfileNameEvent = false;
    }

    private void OnGlossaryProfileNameChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressGlossaryProfileNameEvent) return;
        var profile = _working.GlossaryProfiles.FirstOrDefault(p => p.Id == _editingGlossaryProfileId);
        if (profile is null) return;

        profile.Name = GlossaryProfileNameBox.Text;
    }

    /// <summary>Shows every profile in a dropdown menu off the "▾" button - picking one commits
    /// the current rows and switches to it. A plain menu instead of a combo box next to the name
    /// field on purpose: a combo box showing the same name as the field right beside it just
    /// looked like a duplicate, not like two different controls doing two different things.</summary>
    private void OnGlossaryProfileSwitchClick(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        foreach (var profile in _working.GlossaryProfiles)
        {
            var item = new MenuItem { Header = profile.Name, IsChecked = profile.Id == _editingGlossaryProfileId };
            item.Click += (_, _) =>
            {
                if (profile.Id == _editingGlossaryProfileId) return;
                CommitGlossaryRowsToProfile(_editingGlossaryProfileId);
                SwitchEditingProfile(profile);
            };
            menu.Items.Add(item);
        }
        menu.PlacementTarget = (UIElement)sender;
        menu.IsOpen = true;
    }

    private string NextDefaultGlossaryProfileName()
    {
        var n = _working.GlossaryProfiles.Count + 1;
        while (_working.GlossaryProfiles.Any(p => p.Name == string.Format(Loc.S("Settings.GlossaryProfileDefaultName"), n)))
            n++;
        return string.Format(Loc.S("Settings.GlossaryProfileDefaultName"), n);
    }

    private void OnAddGlossaryProfileClick(object sender, RoutedEventArgs e)
    {
        CommitGlossaryRowsToProfile(_editingGlossaryProfileId);

        var profile = new GlossaryProfile { Name = NextDefaultGlossaryProfileName() };
        _working.GlossaryProfiles.Add(profile);
        SwitchEditingProfile(profile);
    }

    private void OnDeleteGlossaryProfileClick(object sender, RoutedEventArgs e)
    {
        if (_working.GlossaryProfiles.Count <= 1)
        {
            StatusText.Foreground = System.Windows.Media.Brushes.IndianRed;
            StatusText.Text = Loc.S("Settings.GlossaryProfileDeleteLastError");
            ScheduleStatusTextClear();
            return;
        }

        var current = _working.GlossaryProfiles.FirstOrDefault(p => p.Id == _editingGlossaryProfileId);
        if (current is null) return;
        _working.GlossaryProfiles.Remove(current);
        SwitchEditingProfile(_working.GlossaryProfiles[0]);
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "glossary" : cleaned;
    }

    private void OnExportGlossaryClick(object sender, RoutedEventArgs e)
    {
        CommitGlossaryRowsToProfile(_editingGlossaryProfileId);
        var profile = _working.GlossaryProfiles.First(p => p.Id == _editingGlossaryProfileId);

        var dlg = new SaveFileDialog
        {
            Title = Loc.S("Settings.GlossaryExportButton"),
            Filter = "JSON (*.json)|*.json",
            FileName = SanitizeFileName(profile.Name) + ".json"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var json = JsonSerializer.Serialize(profile.Entries, GlossaryFileJsonOptions);
            File.WriteAllText(dlg.FileName, json);
            StatusText.Foreground = System.Windows.Media.Brushes.Gray;
            StatusText.Text = string.Format(Loc.S("Settings.GlossaryExported"), profile.Name);
        }
        catch (Exception ex)
        {
            StatusText.Foreground = System.Windows.Media.Brushes.IndianRed;
            StatusText.Text = string.Format(Loc.S("Settings.GlossaryExportError"), ex.Message);
        }
        ScheduleStatusTextClear();
    }

    private void OnImportGlossaryClick(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Title = Loc.S("Settings.GlossaryImportButton"), Filter = "JSON (*.json)|*.json" };
        if (dlg.ShowDialog() != true) return;

        List<GlossaryEntry>? entries;
        try
        {
            var json = File.ReadAllText(dlg.FileName);
            entries = JsonSerializer.Deserialize<List<GlossaryEntry>>(json, GlossaryFileJsonOptions);
        }
        catch (Exception ex)
        {
            StatusText.Foreground = System.Windows.Media.Brushes.IndianRed;
            StatusText.Text = string.Format(Loc.S("Settings.GlossaryImportError"), ex.Message);
            ScheduleStatusTextClear();
            return;
        }

        if (entries is null || entries.Count == 0)
        {
            StatusText.Foreground = System.Windows.Media.Brushes.IndianRed;
            StatusText.Text = Loc.S("Settings.GlossaryImportEmpty");
            ScheduleStatusTextClear();
            return;
        }

        CommitGlossaryRowsToProfile(_editingGlossaryProfileId);

        var profile = new GlossaryProfile { Name = Path.GetFileNameWithoutExtension(dlg.FileName), Entries = entries };
        _working.GlossaryProfiles.Add(profile);
        SwitchEditingProfile(profile);

        StatusText.Foreground = System.Windows.Media.Brushes.Gray;
        StatusText.Text = string.Format(Loc.S("Settings.GlossaryImported"), profile.Name);
        ScheduleStatusTextClear();
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
    private static bool IsModifierKey(Key key) =>
        key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;

    private static string FormatCombo(ModifierKeys modifiers, Key key)
    {
        var sb = new StringBuilder();
        if (modifiers.HasFlag(ModifierKeys.Control)) sb.Append("Ctrl+");
        if (modifiers.HasFlag(ModifierKeys.Alt)) sb.Append("Alt+");
        if (modifiers.HasFlag(ModifierKeys.Shift)) sb.Append("Shift+");
        if (modifiers.HasFlag(ModifierKeys.Windows)) sb.Append("Win+");
        sb.Append(key);
        return sb.ToString();
    }

    private void OnHotkeyBoxPreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Back)
        {
            ((TextBox)sender).Text = ""; // no hotkey bound for this action
            return;
        }
        if (IsModifierKey(key)) return; // wait for a non-modifier key

        var modifiers = Keyboard.Modifiers;
        if (modifiers == ModifierKeys.None) return; // require at least one modifier for a system-wide hotkey

        ((TextBox)sender).Text = FormatCombo(modifiers, key);
    }

    /// <summary>
    /// Shows the trigger box only for the two "on" modes - "Off" has nothing to configure, and
    /// clearing the trigger now happens by picking "Off" rather than by clearing the box (see
    /// OnHoverTriggerKeyBoxPreviewKeyDown). Switching on for the first time (box still empty)
    /// fills in Ctrl+F: a modifier+key combo, so it goes through RegisterHotKey like the other
    /// hotkeys and isn't affected by the anti-cheat input-hook blocking a bare key/mouse trigger
    /// can run into in some games.
    /// </summary>
    private void OnHoverModeChanged(object sender, RoutedEventArgs e)
    {
        if (HoverTriggerKeyBox is null) return; // fires while InitializeComponent is still wiring up controls

        var isOff = HoverModeOffRadio.IsChecked == true;
        HoverTriggerKeyBox.Visibility = isOff ? Visibility.Collapsed : Visibility.Visible;
        if (!isOff && string.IsNullOrEmpty(HoverTriggerKeyBox.Text))
            HoverTriggerKeyBox.Text = "Ctrl+F";
    }

    /// <summary>
    /// Captures the instant-translate trigger, which can be a single bare key (modifier keys
    /// included - Shift, Ctrl, Alt, CapsLock are exactly the keys meant to be tapped alone), a
    /// modifier+key combo, or (see the mouse handler below) a mouse button. Pressing a modifier
    /// shows it as a bare key right away; adding a non-modifier key while it's still held turns
    /// it into a combo, so "hold Ctrl, press F" ends up as "Ctrl+F" and "tap Shift" as "LeftShift".
    /// Backspace clears it (empty means instant translate is off), same gesture as every other
    /// hotkey box. Tab is deliberately left unhandled so it still moves focus normally instead of
    /// getting captured as "the trigger" when someone's just tabbing through the form.
    /// </summary>
    private void OnHoverTriggerKeyBoxPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Tab) return;

        e.Handled = true;
        if (key is Key.None or Key.DeadCharProcessed) return;

        var isModifier = IsModifierKey(key);
        if (isModifier && e.IsRepeat) return; // auto-repeat of a held modifier must not overwrite a combo being built

        var modifiers = Keyboard.Modifiers;
        ((TextBox)sender).Text = !isModifier && modifiers != ModifierKeys.None
            ? FormatCombo(modifiers, key)
            : key.ToString();
    }

    /// <summary>
    /// Same box as above also accepts a mouse button - Right/Middle/side buttons, everything
    /// except Left (reserved for normal clicking, including focusing this very box). Stored as
    /// "Mouse:&lt;button&gt;" so ApplyHoverTranslateSetting can tell it apart from a keyboard key.
    /// </summary>
    private void OnHoverTriggerKeyBoxPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            ((TextBox)sender).Focus(); // plain click just focuses the box, like any other textbox
            return;
        }

        e.Handled = true; // stop e.g. a right-click from popping the default context menu
        ((TextBox)sender).Focus();
        ((TextBox)sender).Text = "Mouse:" + e.ChangedButton;
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
        RequireHotkey(RetranslateHotkeyBox.Text);
        RequireHotkey(OpenSettingsHotkeyBox.Text);
        var hoverOff = HoverModeOffRadio.IsChecked == true;
        if (!hoverOff) RequireTrigger(HoverTriggerKeyBox.Text);
        _working.Hotkeys.SelectRegion.Combo = SelectRegionHotkeyBox.Text;
        _working.Hotkeys.ToggleTranslation.Combo = ToggleHotkeyBox.Text;
        _working.Hotkeys.OneTimeTranslate.Combo = OneTimeHotkeyBox.Text;
        _working.Hotkeys.Retranslate.Combo = RetranslateHotkeyBox.Text;
        _working.Hotkeys.OpenSettings.Combo = OpenSettingsHotkeyBox.Text;
        // "Off" is its own radio now rather than an empty box, so it wins regardless of what's
        // still sitting in the (hidden) trigger box - that text is kept around only so flipping
        // back to Immediate/Confirm doesn't force a re-pick.
        _working.HoverTranslate.TriggerKey = hoverOff ? "" : HoverTriggerKeyBox.Text;
        _working.HoverTranslate.Mode = HoverModeConfirmRadio.IsChecked == true ? HoverTranslateMode.ConfirmClick : HoverTranslateMode.Immediate;

        _working.ApiKeys.DeepLApiKey = DeepLKeyBox.Text.Trim();
        _working.ApiKeys.DeepLUseProEndpoint = DeepLProCheck.IsChecked == true;
        _working.ApiKeys.GoogleApiKey = GoogleKeyBox.Text.Trim();
        _working.ApiKeys.YandexApiKey = YandexKeyBox.Text.Trim();
        _working.ApiKeys.YandexFolderId = YandexFolderBox.Text.Trim();
        _working.ApiKeys.PapagoClientId = PapagoIdBox.Text.Trim();
        _working.ApiKeys.PapagoClientSecret = PapagoSecretBox.Text.Trim();
        _working.ApiKeys.OpenAiCompatibleProvider = ((EnumOption<string>)OpenAiCompatibleProviderCombo.SelectedItem).Value;
        _working.ApiKeys.OpenAiCompatibleBaseUrl = OpenAiCompatibleUrlBox.Text.Trim();
        _working.ApiKeys.OpenAiCompatibleModel = OpenAiCompatibleModelCombo.Text.Trim();
        _working.ApiKeys.OpenAiCompatibleApiKey = OpenAiCompatibleKeyBox.Text.Trim();
        _working.Ai.GameContext = AiContextBox.Text.Trim();
        _working.Ai.UseGlossary = AiUseGlossaryCheck.IsChecked == true;
        _working.Ai.UseDialogueHistory = AiUseHistoryCheck.IsChecked == true;

        _working.ProxyList = ProxyListBox.Text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => l.Length > 0)
            .ToList();
        _working.RotateProxyPerRequest = RotateProxyCheck.IsChecked == true;

        _working.Overlay.DisplayMode = DisplayModeReplaceRadio.IsChecked == true ? OverlayDisplayMode.ReplaceText : OverlayDisplayMode.Panel;
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
        _working.Overlay.DismissOnClickOrSpace = DismissOnClickOrSpaceCheck.IsChecked == true;

        _working.TesseractDataPath = TesseractPathBox.Text.Trim();
        _working.EasyOcrPythonExePath = EasyOcrPythonBox.Text.Trim();
        _working.EasyOcrUseGpu = EasyOcrGpuCheck.IsChecked == true;
        _working.RunAtWindowsStartup = RunAtStartupCheck.IsChecked == true;

        _working.UiLanguage = ((EnumOption<string>)UiLanguageCombo.SelectedItem).Value;
        _working.UiTheme = ((EnumOption<string>)ThemeCombo.SelectedItem).Value;

        CommitGlossaryRowsToProfile(_editingGlossaryProfileId);
        _working.ActiveGlossaryProfileId = _editingGlossaryProfileId;
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
        if (string.IsNullOrEmpty(combo)) return; // cleared via Backspace - intentionally unbound
        if (!Hotkeys.HotkeyManager.TryParse(combo, out _, out _))
        {
            var message = Loc.Current == AppLanguage.English
                ? $"\"{combo}\" isn't a valid hotkey (needs a modifier + a key)."
                : $"«{combo}» — не подходит для горячей клавиши (нужен модификатор + клавиша).";
            throw new InvalidOperationException(message);
        }
    }

    /// <summary>Validates the instant-translate trigger: a bare key name, "Mouse:&lt;button&gt;", or a
    /// modifier+key combo.</summary>
    private static void RequireTrigger(string trigger)
    {
        // Only called for the "Immediate"/"Confirm" modes now - "Off" (an empty trigger) never
        // reaches this, see the hoverOff check at the call site.
        if (trigger.StartsWith("Mouse:", StringComparison.Ordinal))
        {
            if (Enum.TryParse<Hotkeys.HoverMouseButton>(trigger["Mouse:".Length..], out _)) return;
        }
        else if (trigger.Contains('+'))
        {
            RequireHotkey(trigger);
            return;
        }
        else if (Enum.TryParse<Key>(trigger, out _))
        {
            return;
        }

        var message = Loc.Current == AppLanguage.English
            ? $"\"{trigger}\" isn't a recognized key."
            : $"«{trigger}» — нераспознанная клавиша.";
        throw new InvalidOperationException(message);
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

    // ---------- Custom window chrome (no native title bar - see WindowStyle="None" in XAML) ----------

    private void OnMinimizeWindowClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeRestoreWindowClick(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    /// <summary>Square outline when not maximized ("maximize"); two overlapping squares when
    /// maximized ("restore"), the usual Windows convention for this button.</summary>
    private void UpdateMaximizeRestoreGlyph()
    {
        MaximizeRestoreGlyph.Data = Geometry.Parse(WindowState == WindowState.Maximized
            ? "M2,0 L10,0 L10,8 M0,2 L8,2 L8,10 L0,10 Z"
            : "M0,0 L10,0 L10,10 L0,10 Z");
    }

    /// <summary>
    /// Without a native title bar, Windows no longer applies its own "don't cover the taskbar"
    /// logic when this window is maximized - it would otherwise draw over the taskbar and past
    /// screen edges. Handling WM_GETMINMAXINFO ourselves restores that behavior by sizing the
    /// maximized window to the monitor's work area instead of its full bounds.
    /// </summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (PresentationSource.FromVisual(this) is HwndSource hwndSource)
            hwndSource.AddHook(WindowProc);
    }

    private static IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_GETMINMAXINFO = 0x0024;
        if (msg == WM_GETMINMAXINFO)
        {
            ApplyWorkAreaToMinMaxInfo(hwnd, lParam);
            handled = true;
        }
        return IntPtr.Zero;
    }

    private static void ApplyWorkAreaToMinMaxInfo(IntPtr hwnd, IntPtr lParam)
    {
        var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);

        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        if (monitor != IntPtr.Zero)
        {
            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            GetMonitorInfo(monitor, ref info);
            var workArea = info.rcWork;
            var monitorArea = info.rcMonitor;

            mmi.ptMaxPosition.X = Math.Abs(workArea.Left - monitorArea.Left);
            mmi.ptMaxPosition.Y = Math.Abs(workArea.Top - monitorArea.Top);
            mmi.ptMaxSize.X = Math.Abs(workArea.Right - workArea.Left);
            mmi.ptMaxSize.Y = Math.Abs(workArea.Bottom - workArea.Top);
        }

        Marshal.StructureToPtr(mmi, lParam, true);
    }

    private const int MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr handle, int flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
