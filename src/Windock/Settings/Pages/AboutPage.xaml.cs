using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Navigation;

namespace Windock.Settings.Pages;

internal partial class AboutPage : UserControl
{
    public AboutPage(SettingsContext context)
    {
        InitializeComponent();
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        Version.Text = $"Windock {version?.ToString(3)}";
        SettingsPath.Text = Path.Combine(context.DataDirectory, "settings.json");
        LogPath.Text = Path.Combine(context.DataDirectory, "logs");
    }

    private void OnLink(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true })?.Dispose();
        e.Handled = true;
    }
}
