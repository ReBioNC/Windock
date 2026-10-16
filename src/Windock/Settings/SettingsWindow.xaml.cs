using System.Windows;
using System.Windows.Controls;
using Windock.Settings.Pages;

namespace Windock.Settings;

/// Pengaturan: every change applies immediately through SettingsStore.Update; there is no Save button (DESIGN §7).
internal partial class SettingsWindow : Window
{
    private readonly SettingsContext _context;
    private readonly Dictionary<int, UserControl> _pages = new();

    public SettingsWindow(SettingsContext context)
    {
        _context = context;
        InitializeComponent();
        Nav.SelectedIndex = 0;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Interop.WindowEffects.UseDarkTitleBar(new System.Windows.Interop.WindowInteropHelper(this).Handle);
    }

    private void OnNavigate(object sender, SelectionChangedEventArgs e)
    {
        var index = Nav.SelectedIndex;
        if (!_pages.TryGetValue(index, out var page))
        {
            page = index switch
            {
                0 => new AppearancePage(_context),
                1 => new BehaviorPage(_context),
                2 => new PinsPage(_context),
                3 => new SafetyPage(_context),
                _ => new AboutPage(_context),
            };
            _pages[index] = page;
        }
        Page.Content = page;
    }
}
