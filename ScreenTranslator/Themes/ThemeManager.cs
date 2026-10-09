using System;
using System.Linq;
using System.Windows;
using Microsoft.Win32;
using Application = System.Windows.Application;

namespace ScreenTranslator.Themes;

public enum ThemeMode
{
    System,
    Light,
    Dark,
}

/// <summary>
/// Applies and tracks the app's Palette.Light.xaml / Palette.Dark.xaml resource dictionary
/// (see Controls.xaml, which reads every color through DynamicResource so a live swap here
/// updates every open window immediately - no restart needed).
/// </summary>
public static class ThemeManager
{
    private const string LightSource = "Themes/Palette.Light.xaml";
    private const string DarkSource = "Themes/Palette.Dark.xaml";

    private static ThemeMode _mode = ThemeMode.System;
    private static bool _subscribed;

    /// <summary>Sets the mode and (re)applies it immediately. When the mode is System, also
    /// starts listening for Windows theme changes so the app follows them live; any other mode
    /// stops listening, since the user has overridden the OS setting explicitly.</summary>
    public static void Apply(ThemeMode mode)
    {
        _mode = mode;

        if (mode == ThemeMode.System)
        {
            if (!_subscribed)
            {
                SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
                _subscribed = true;
            }
        }
        else if (_subscribed)
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            _subscribed = false;
        }

        var dark = mode switch
        {
            ThemeMode.Light => false,
            ThemeMode.Dark => true,
            _ => IsSystemDark(),
        };
        ApplyPalette(dark);
    }

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        // Theme changes land under General; filtering avoids re-applying on every unrelated
        // system-settings change (this event fires for a lot of them).
        if (e.Category != UserPreferenceCategory.General) return;
        if (_mode != ThemeMode.System) return;
        Application.Current?.Dispatcher.Invoke(() => ApplyPalette(IsSystemDark()));
    }

    private static void ApplyPalette(bool dark)
    {
        var app = Application.Current;
        if (app is null) return;

        var dictionaries = app.Resources.MergedDictionaries;
        var existing = dictionaries.FirstOrDefault(d =>
            d.Source != null && (d.Source.OriginalString.EndsWith(LightSource, StringComparison.OrdinalIgnoreCase)
                               || d.Source.OriginalString.EndsWith(DarkSource, StringComparison.OrdinalIgnoreCase)));
        if (existing != null) dictionaries.Remove(existing);

        dictionaries.Add(new ResourceDictionary
        {
            Source = new Uri(dark ? DarkSource : LightSource, UriKind.Relative)
        });
    }

    /// <summary>Reads Windows' own "choose your color" setting for apps (Settings > Personalization
    /// > Colors). Defaults to light if the key is missing or unreadable - matches what Windows
    /// itself defaults to on a system where this has never been touched.</summary>
    private static bool IsSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            return value is int i && i == 0;
        }
        catch
        {
            return false; // unreadable - assume light, the safer default for legibility
        }
    }

    /// <summary>Detaches the static SystemEvents subscription (if any) on shutdown - it would
    /// otherwise outlive this app for the lifetime of the process' static state.</summary>
    public static void Unsubscribe()
    {
        if (!_subscribed) return;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _subscribed = false;
    }
}
