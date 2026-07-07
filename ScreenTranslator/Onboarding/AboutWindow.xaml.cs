using System.Windows;
using System.Windows.Navigation;

namespace ScreenTranslator.Onboarding;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
    }

    private void OnRequestNavigate(object sender, RequestNavigateEventArgs e) => SettingsUi.LinkOpener.OpenInBrowser(sender, e);

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
