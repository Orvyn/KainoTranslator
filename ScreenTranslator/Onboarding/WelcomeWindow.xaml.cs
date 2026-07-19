using System;
using System.Windows;
using ScreenTranslator.Localization;
using ScreenTranslator.Models;

namespace ScreenTranslator.Onboarding;

public partial class WelcomeWindow : Window
{
    public event Action? OpenSettingsRequested;
    public bool DontShowAgain { get; private set; }

    public WelcomeWindow(HotkeySettings hotkeys)
    {
        InitializeComponent();

        SubtitleText.Text = Loc.S("Welcome.Subtitle");
        TrayHeaderText.Text = Loc.S("Welcome.TrayHeader");
        TrayBodyText.Text = Loc.S("Welcome.TrayBody");
        HotkeysHeaderText.Text = Loc.S("Welcome.HotkeysHeader");
        HotkeySelectRegionLabel.Text = Loc.S("Welcome.HotkeySelectRegion");
        HotkeyToggleLabel.Text = Loc.S("Welcome.HotkeyToggle");
        HotkeyOnceLabel.Text = Loc.S("Welcome.HotkeyOnce");
        HotkeyOpenSettingsLabel.Text = Loc.S("Welcome.HotkeyOpenSettings");
        HotkeysFooterText.Text = Loc.S("Welcome.HotkeysFooter");
        GameTipHeaderText.Text = Loc.S("Welcome.GameTipHeader");
        GameTipBodyText.Text = Loc.S("Welcome.GameTipBody");
        DefaultsHeaderText.Text = Loc.S("Welcome.DefaultsHeader");
        DefaultsBody1Run.Text = Loc.S("Welcome.DefaultsBody1");
        DefaultsBody1BoldRun.Text = Loc.S("Welcome.DefaultsBody1Bold");
        DefaultsBody2Run.Text = Loc.S("Welcome.DefaultsBody2");
        DefaultsBody2BoldRun.Text = Loc.S("Welcome.DefaultsBody2Bold");
        DefaultsBody3Run.Text = Loc.S("Welcome.DefaultsBody3");
        DontShowAgainText.Text = Loc.S("Welcome.DontShowAgain");
        CloseButton.Content = Loc.S("Welcome.Close");
        OpenSettingsButton.Content = Loc.S("Welcome.OpenSettings");

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
