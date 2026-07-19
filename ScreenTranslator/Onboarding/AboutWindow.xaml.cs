using System.Windows;
using System.Windows.Navigation;
using ScreenTranslator.Localization;

namespace ScreenTranslator.Onboarding;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        VersionText.Text = $"v{App.AppVersion}";
        SubtitleText.Text = Loc.S("Welcome.Subtitle");
        OcrLabelRun.Text = Loc.S("About.Ocr");
        OcrValueRun.Text = " " + Loc.S("About.OcrValue");
        TranslationLabelRun.Text = Loc.S("About.Translation");
        TranslationValueRun.Text = " " + Loc.S("About.TranslationValue");
        CloseButton.Content = Loc.S("About.Close");
    }

    private void OnRequestNavigate(object sender, RequestNavigateEventArgs e) => SettingsUi.LinkOpener.OpenInBrowser(sender, e);

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
