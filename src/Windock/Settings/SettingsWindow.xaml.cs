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
        context.Store.Changed += OnSettingsChanged;
        Closed += (_, _) => context.Store.Changed -= OnSettingsChanged;
        ShowMode(context.Store.Current.Behavior.Mode);
    }

    private void OnWindockMode(object sender, RoutedEventArgs e) => SetMode(Core.Settings.DockMode.Windock);

    private void OnWindowsMode(object sender, RoutedEventArgs e) => SetMode(Core.Settings.DockMode.Windows);

    private void SetMode(Core.Settings.DockMode mode) =>
        _context.Store.Update(s => s with { Behavior = s.Behavior with { Mode = mode } });

    private void ShowMode(Core.Settings.DockMode mode)
    {
        var windock = mode == Core.Settings.DockMode.Windock;
        var accent = (System.Windows.Media.Brush)FindResource("Settings.Accent");
        var normal = (System.Windows.Media.Brush)FindResource("Settings.Control");
        WindockModeButton.Background = windock ? accent : normal;
        WindowsModeButton.Background = windock ? normal : accent;
        ModeHint.Text = windock
            ? "Dock aktif dan menggantikan taskbar sesuai pengaturan di bawah."
            : "Tampilan Windows asli: taskbar kembali seperti semula, dock disembunyikan. Pin dan pengaturan tetap tersimpan.";
    }

    /// Confirms that an edit reached the dock; there is no Apply button.
    private void OnSettingsChanged(object? sender, Core.Settings.SettingsChangedEventArgs e)
    {
        ShowMode(e.New.Behavior.Mode);
        Status.Foreground = (System.Windows.Media.Brush)FindResource("Settings.Text");
        Status.Text = $"✓ Diterapkan ke dock dan disimpan ({DateTime.Now:HH:mm:ss})";
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
                1 => new ThemePage(_context),
                2 => new StatusPage(_context),
                3 => new BehaviorPage(_context),
                4 => new PinsPage(_context),
                5 => new LauncherPage(_context),
                6 => new SwitcherPage(_context),
                7 => new SafetyPage(_context),
                _ => new AboutPage(_context),
            };
            _pages[index] = page;
        }
        Page.Content = page;
    }
}
