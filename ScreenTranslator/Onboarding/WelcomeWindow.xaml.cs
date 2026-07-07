using System;
using System.Windows;
using ScreenTranslator.Models;

namespace ScreenTranslator.Onboarding;

public partial class WelcomeWindow : Window
{
    public event Action? OpenSettingsRequested;
    public bool DontShowAgain { get; private set; }

    public WelcomeWindow(HotkeySettings hotkeys)
    {
        InitializeComponent();
        HotkeyRegion.Text = hotkeys.SelectRegion.Combo;
        HotkeyToggle.Text = hotkeys.ToggleTranslation.Combo;
        HotkeyOnce.Text = hotkeys.OneTimeTranslate.Combo;
        HotkeyOpenSettings.Text = hotkeys.OpenSettings.Combo;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        DontShowAgain = DontShowAgainCheck.IsChecked == true;
        Close();
    }

    private void OnOpenSettingsClick(object sender, RoutedEventArgs e)
    {
        DontShowAgain = DontShowAgainCheck.IsChecked == true;
        OpenSettingsRequested?.Invoke();
        Close();
    }
}
