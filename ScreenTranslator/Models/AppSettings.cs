using System;
using System.Collections.Generic;
using System.IO;
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

public sealed class HotkeySettings
{
    public HotkeyBinding SelectRegion { get; set; } = new("Ctrl+Shift+A");
    public HotkeyBinding ToggleTranslation { get; set; } = new("Ctrl+Shift+S");
    public HotkeyBinding OpenSettings { get; set; } = new("Ctrl+Shift+O");
    // One-off: pick any area once, translate it once, without touching the main capture area
    // or the continuous translation loop.
    public HotkeyBinding OneTimeTranslate { get; set; } = new("Ctrl+Shift+D");
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

    [JsonIgnore]
    public static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "KainoTranslator", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (loaded != null) return loaded;
            }
        }
        catch
        {
            // Fall back to defaults if the file is corrupt/from an incompatible version.
        }
        return new AppSettings();
    }

    /// <summary>Deep-copies these settings (used by the Settings window so cancelling doesn't mutate live settings).</summary>
    public AppSettings Clone()
    {
        var json = JsonSerializer.Serialize(this, JsonOptions);
        return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions)!;
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
