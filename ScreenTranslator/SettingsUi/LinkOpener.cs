using System;
using System.Diagnostics;
using System.Windows.Navigation;

namespace ScreenTranslator.SettingsUi;

public static class LinkOpener
{
    public static void OpenInBrowser(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch { /* best effort - not worth interrupting the user over */ }
        e.Handled = true;
    }
}
