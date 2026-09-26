using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using ScreenTranslator.Ocr;
using ScreenTranslator.Translation;

namespace ScreenTranslator.Models;

public sealed class CaptureRegionSettings
{
    // Stored in *virtual-screen physical pixels* (DPI-independent), so it stays correct
    // no matter which monitor/scale factor the region was picked on.
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; } = 800;
    public int Height { get; set; } = 160;
    public bool HasRegion { get; set; } = false;
}

public enum OverlayPosition
{
    OverCaptureArea,
    AboveCaptureArea,
    BelowCaptureArea,
    FixedBottomCenterOfScreen,
    FixedTopCenterOfScreen
}

public enum TextOutlineMode
{
    None,
    Outline,
    Shadow
}

public sealed class OverlaySettings
{
    public double FontSize { get; set; } = 26;
    public string FontFamily { get; set; } = "Segoe UI";
    public string TextColor { get; set; } = "#FFFFFF";        // plain RGB, always fully opaque
    public string OutlineColor { get; set; } = "#000000";     // used for both Outline and Shadow modes
    public TextOutlineMode OutlineMode { get; set; } = TextOutlineMode.None;

    public string BackgroundColorRgb { get; set; } = "#000000";
    // 0 = fully solid background, 100 = fully see-through. (Not "opacity" - deliberately the
    // opposite direction, to match how people actually use the word "прозрачность".)
    public int BackgroundTransparencyPercent { get; set; } = 20;

    public OverlayPosition Position { get; set; } = OverlayPosition.FixedTopCenterOfScreen;
    public int MaxWidth { get; set; } = 1200;
    public bool ClickThrough { get; set; } = true;

    public bool AutoHideEnabled { get; set; } = false;
    public int AutoHideSeconds { get; set; } = 5;
}

public sealed class HotkeyBinding
{
    // Stored as e.g. "Ctrl+Shift+A" - parsed by HotkeyManager.
    public string Combo { get; set; } = "";
    public HotkeyBinding() { }
    public HotkeyBinding(string combo) => Combo = combo;
}

public enum HoverTranslateMode
{
    // Translates the area around the cursor immediately when the trigger fires.
    Immediate,
    // The trigger shows a visible clickable cursor first (like region selection) and translates
    // wherever the next click lands - for games that hide/replace the mouse cursor, where there's
    // otherwise no way to see where a translate would even happen.
    ConfirmClick
}

public sealed class HoverTranslateSettings
{
    // Empty means instant translate is off - there's no separate enabled flag; clearing this
    // (Backspace in Settings, or unchecking the tray item) is what turns it off.
    // One field covers all three kinds of trigger: a keyboard key stored as its
    // System.Windows.Input.Key name (e.g. "LeftShift"), a mouse button stored as
    // "Mouse:<button>" (e.g. "Mouse:Right", "Mouse:XButton1" - see HoverMouseButton), or a
    // modifier+key combo (e.g. "Ctrl+Shift+F"). Empty by default: Shift/Ctrl/Alt/mouse buttons
    // all double as in-game actions in many games, so this shouldn't just turn itself on with
    // a default nobody chose.
    public string TriggerKey { get; set; } = "";
    public HoverTranslateMode Mode { get; set; } = HoverTranslateMode.Immediate;
    // Minimum height of the band captured around the cursor - on larger screens it grows to a
    // fifth (a third with Windows OCR) of the monitor height. Its width is always the full
    // monitor width; the text block under the cursor is then found inside it - see
    // App.HoverTranslate. (An old BoxWidth field in settings.json is just ignored.)
    public int BoxHeight { get; set; } = 140;
}

public sealed class HotkeySettings
{
    public HotkeyBinding SelectRegion { get; set; } = new("Ctrl+Shift+A");
    public HotkeyBinding ToggleTranslation { get; set; } = new("Ctrl+Shift+S");
    public HotkeyBinding OpenSettings { get; set; } = new("Ctrl+Shift+O");
    // One-off: pick any area once, translate it once, without touching the main capture area
    // or the continuous translation loop.
    public HotkeyBinding OneTimeTranslate { get; set; } = new("Ctrl+Shift+D");
    // Re-runs OCR+translation on the current capture area right now, ignoring the "already
    // showing this text" cache - for when the translator returned something garbled and the
    // source text is still on screen.
    public HotkeyBinding Retranslate { get; set; } = new("Ctrl+Shift+R");
    // Legacy: instant translate briefly had a separate combo field next to its single-key
    // trigger; both now live in HoverTranslate.TriggerKey. Kept only so settings.json files saved
    // in that window still load - folded into TriggerKey by MigrateHoverCombo() on Load(), then
    // nulled so it's no longer written back. Ignore this in new code.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public HotkeyBinding? HoverTranslateCombo { get; set; }
}

public sealed class ApiKeySettings
{
    public string DeepLApiKey { get; set; } = "";
    public bool DeepLUseProEndpoint { get; set; } = false;

    public string GoogleApiKey { get; set; } = ""; // Cloud Translation v2 API key (leave blank to use the free unofficial endpoint)

    public string YandexApiKey { get; set; } = "";
    public string YandexFolderId { get; set; } = "";

    public string PapagoClientId { get; set; } = "";
    public string PapagoClientSecret { get; set; } = "";
}

public sealed class AppSettings
{
    public OcrEngineKind OcrEngine { get; set; } = OcrEngineKind.WindowsOcr; // recommended default
    public string SourceLanguageKey { get; set; } = "en";

    public TranslatorKind Translator { get; set; } = TranslatorKind.Google;
    public string TargetLanguageKey { get; set; } = "ru";

    public int PollingIntervalMs { get; set; } = 300;
    public bool SkipOcrWhenFrameUnchanged { get; set; } = true; // big perf/latency win for static subtitles
    public bool AutoStartTranslationAfterRegionSelect { get; set; } = true;

    public CaptureRegionSettings Region { get; set; } = new();
    public OverlaySettings Overlay { get; set; } = new();
    public HotkeySettings Hotkeys { get; set; } = new();
    public HoverTranslateSettings HoverTranslate { get; set; } = new();
    public ApiKeySettings ApiKeys { get; set; } = new();

    public List<string> ProxyList { get; set; } = new(); // "http://user:pass@host:port" or "host:port"
    public bool RotateProxyPerRequest { get; set; } = true;

    public string TesseractDataPath { get; set; } = @".\tessdata";
    public string EasyOcrPythonExePath { get; set; } = "python";
    public string EasyOcrWorkerScriptPath { get; set; } = @".\Python\easyocr_worker.py";
    public bool EasyOcrUseGpu { get; set; } = false;

    public bool RunAtWindowsStartup { get; set; } = false;
    public bool ShowWelcomeOnStartup { get; set; } = true;
    public string UiLanguage { get; set; } = "ru"; // "ru" or "en"

    public List<GlossaryProfile> GlossaryProfiles { get; set; } = new();
    public string ActiveGlossaryProfileId { get; set; } = "";
    // Legacy pre-profiles field, kept only so settings.json files saved before profiles existed
    // still load - migrated into a profile by MigrateLegacyGlossary() on Load() and left empty
    // afterwards. Ignore this in new code; use GlossaryProfiles/ActiveGlossaryProfileId instead.
    public List<GlossaryEntry> Glossary { get; set; } = new();

    /// <summary>The glossary entries currently in effect for translation - the active profile's,
    /// or the first profile's if the stored active id doesn't match anything (e.g. that profile
    /// was deleted elsewhere).</summary>
    [JsonIgnore]
    public List<GlossaryEntry> ActiveGlossaryEntries =>
        (GlossaryProfiles.FirstOrDefault(p => p.Id == ActiveGlossaryProfileId) ?? GlossaryProfiles.FirstOrDefault())
        ?.Entries ?? new List<GlossaryEntry>();

    [JsonIgnore]
    public static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "KainoTranslator", "settings.json");

    public static AppSettings Load()
    {
        AppSettings settings;
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            }
            else
            {
                settings = new AppSettings();
            }
        }
        catch
        {
            // Fall back to defaults if the file is corrupt/from an incompatible version.
            settings = new AppSettings();
        }

        settings.MigrateLegacyGlossary();
        settings.EnsureGlossaryProfile();
        settings.MigrateHoverCombo();
        return settings;
    }

    /// <summary>One-time upgrade path: settings.json files saved before glossary profiles existed
    /// have their terms in the flat Glossary list instead. Folds that into a single profile the
    /// first time such a file loads; harmless no-op on every load after that.</summary>
    private void MigrateLegacyGlossary()
    {
        if (GlossaryProfiles.Count > 0 || Glossary.Count == 0) return;

        var profile = new GlossaryProfile { Name = "Глоссарий", Entries = Glossary };
        GlossaryProfiles.Add(profile);
        ActiveGlossaryProfileId = profile.Id;
        Glossary = new();
    }

    /// <summary>One-time upgrade path: a combo that used to live in its own field moves into the
    /// single trigger field - unless a key/mouse trigger is already set, which wins (one trigger
    /// only now).</summary>
    private void MigrateHoverCombo()
    {
        var legacy = Hotkeys.HoverTranslateCombo?.Combo;
        if (string.IsNullOrWhiteSpace(HoverTranslate.TriggerKey) && !string.IsNullOrWhiteSpace(legacy))
            HoverTranslate.TriggerKey = legacy;
        Hotkeys.HoverTranslateCombo = null;
    }

    /// <summary>The Settings UI and the translation pipeline both assume at least one profile
    /// always exists - guards against a freshly-created AppSettings (first run, or a corrupt
    /// settings.json that fell back to defaults) having none.</summary>
    private void EnsureGlossaryProfile()
    {
        if (GlossaryProfiles.Count == 0)
            GlossaryProfiles.Add(new GlossaryProfile { Name = "Профиль 1" });
        if (!GlossaryProfiles.Any(p => p.Id == ActiveGlossaryProfileId))
            ActiveGlossaryProfileId = GlossaryProfiles[0].Id;
    }

    /// <summary>Deep-copies these settings (used by the Settings window so cancelling doesn't mutate live settings).</summary>
    public AppSettings Clone()
    {
        var json = JsonSerializer.Serialize(this, JsonOptions);
        var clone = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions)!;
        clone.EnsureGlossaryProfile(); // defensive - normally already satisfied by the time Load() handed this instance out
        return clone;
    }

    public void Save()
    {
        var dir = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(this, JsonOptions);
        File.WriteAllText(SettingsPath, json);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
}
