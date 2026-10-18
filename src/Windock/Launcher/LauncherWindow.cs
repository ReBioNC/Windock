using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Windock.Core.Launcher;
using Windock.Interop;

namespace Windock.Launcher;

/// Search box near the top of the screen: type, pick with arrows/mouse, Enter opens, Esc closes.
/// Built when opened (with a fresh index) and thrown away when closed.
internal sealed class LauncherWindow : Window
{
    private readonly IconProvider _icons;
    private readonly int _maxResults;
    private readonly Action<LauncherEntry> _open;
    private readonly TextBox _query = new();
    private readonly ListBox _list = new();
    private readonly TextBlock _hint = new();
    private readonly ObservableCollection<ResultRow> _rows = new();
    private IReadOnlyList<LauncherEntry> _index = Array.Empty<LauncherEntry>();
    private bool _indexReady;
    private bool _closing;

    private readonly Windock.Core.Logging.ILog _log;

    public LauncherWindow(IconProvider icons, int maxResults, bool includeFiles, Action<LauncherEntry> open, Windock.Core.Logging.ILog log)
    {
        _log = log;
        _icons = icons;
        _maxResults = maxResults;
        _open = open;
        Title = "Windock Launcher";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Width = 640;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
        Top = SystemParameters.PrimaryScreenHeight * 0.2;

        var accent = Application.Current.TryFindResource("Windock.Accent") as Brush ?? Brushes.SteelBlue;
        _query.FontSize = 20;
        _query.Foreground = Brushes.White;
        _query.CaretBrush = Brushes.White;
        _query.Background = Brushes.Transparent;
        _query.BorderThickness = new Thickness(0);
        _query.VerticalContentAlignment = VerticalAlignment.Center;
        _query.TextChanged += (_, _) => Refresh();
        _query.PreviewKeyDown += OnKey;

        var searchRow = new DockPanel { Margin = new Thickness(16, 12, 16, 12) };
        var glyph = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 18, Foreground = Brushes.White, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        DockPanel.SetDock(glyph, System.Windows.Controls.Dock.Left);
        searchRow.Children.Add(glyph);
        searchRow.Children.Add(_query);

        _list.ItemsSource = _rows;
        _list.Background = Brushes.Transparent;
        _list.BorderThickness = new Thickness(0);
        _list.Foreground = Brushes.White;
        _list.Margin = new Thickness(6, 0, 6, 6);
        _list.ItemTemplate = RowTemplate();
        _list.ItemContainerStyle = RowStyle(accent);
        _list.MouseUp += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Left && (e.OriginalSource as FrameworkElement)?.DataContext is ResultRow) OpenSelected();
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);

        _hint.Foreground = Brushes.White;
        _hint.Opacity = 0.55;
        _hint.Margin = new Thickness(20, 0, 16, 12);

        var panel = new StackPanel();
        panel.Children.Add(searchRow);
        panel.Children.Add(_list);
        panel.Children.Add(_hint);
        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x24, 0x24, 0x24)), CornerRadius = new CornerRadius(12),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(1), Child = panel,
        };

        Deactivated += (_, _) => CloseOnce();
        Closing += (_, _) => _closing = true;
        Loaded += (_, _) =>
        {
            Activate();
            _query.Focus();
            Keyboard.Focus(_query);
        };
        _hint.Text = "Memuat daftar aplikasi…";
        LoadIndex(includeFiles);
    }

    public void CloseOnce()
    {
        if (_closing) return;
        _closing = true;
        Close();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Native.AddExStyle(new WindowInteropHelper(this).Handle, Native.WS_EX_TOOLWINDOW);
    }

    private async void LoadIndex(bool includeFiles)
    {
        try
        {
            _index = await LauncherIndex.BuildAsync(includeFiles, message => _log.Info(message));
        }
        catch (Exception ex)
        {
            _log.Error("launcher index failed", ex);
            _index = Array.Empty<LauncherEntry>();
        }
        _indexReady = true;
        Refresh();
    }

    private void Refresh()
    {
        _rows.Clear();
        var query = _query.Text;
        if (!_indexReady)
        {
            _hint.Text = "Memuat daftar aplikasi…";
            return;
        }
        foreach (var entry in LauncherSearch.Search(_index, query, _maxResults))
        {
            var row = new ResultRow(entry);
            _rows.Add(row);
            _icons.Request("launcher:" + entry.Target, entry.Target, IntPtr.Zero, 32, (_, image) => row.Icon = image);
        }
        if (_rows.Count > 0) _list.SelectedIndex = 0;
        _hint.Text = string.IsNullOrWhiteSpace(query) ? $"Ketik untuk mencari di {_index.Count} aplikasi dan file."
            : _rows.Count == 0 ? "Tidak ada yang cocok." : "Enter untuk membuka · ↑↓ pilih · Esc tutup";
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;
                CloseOnce();
                break;
            case Key.Enter:
                e.Handled = true;
                OpenSelected();
                break;
            case Key.Down when _rows.Count > 0:
                e.Handled = true;
                _list.SelectedIndex = (_list.SelectedIndex + 1) % _rows.Count;
                _list.ScrollIntoView(_list.SelectedItem);
                break;
            case Key.Up when _rows.Count > 0:
                e.Handled = true;
                _list.SelectedIndex = (_list.SelectedIndex - 1 + _rows.Count) % _rows.Count;
                _list.ScrollIntoView(_list.SelectedItem);
                break;
        }
    }

    private void OpenSelected()
    {
        if (_list.SelectedItem is not ResultRow row) return;
        CloseOnce();
        _open(row.Entry);
    }

    private static DataTemplate RowTemplate()
    {
        var root = new FrameworkElementFactory(typeof(StackPanel));
        root.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        root.SetValue(MarginProperty, new Thickness(6, 4, 6, 4));
        var image = new FrameworkElementFactory(typeof(Image));
        image.SetValue(WidthProperty, 28.0);
        image.SetValue(HeightProperty, 28.0);
        image.SetValue(MarginProperty, new Thickness(0, 0, 12, 0));
        image.SetBinding(Image.SourceProperty, new System.Windows.Data.Binding(nameof(ResultRow.Icon)));
        var texts = new FrameworkElementFactory(typeof(StackPanel));
        texts.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        var name = new FrameworkElementFactory(typeof(TextBlock));
        name.SetValue(TextBlock.FontSizeProperty, 15.0);
        name.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(ResultRow.Name)));
        var sub = new FrameworkElementFactory(typeof(TextBlock));
        sub.SetValue(TextBlock.FontSizeProperty, 11.0);
        sub.SetValue(OpacityProperty, 0.6);
        sub.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(ResultRow.Detail)));
        texts.AppendChild(name);
        texts.AppendChild(sub);
        root.AppendChild(image);
        root.AppendChild(texts);
        return new DataTemplate { VisualTree = root };
    }

    private static Style RowStyle(Brush accent)
    {
        var style = new Style(typeof(ListBoxItem));
        var template = new ControlTemplate(typeof(ListBoxItem));
        var border = new FrameworkElementFactory(typeof(Border), "Bd");
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        border.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
        template.VisualTree = border;
        var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Border.BackgroundProperty, accent, "Bd"));
        template.Triggers.Add(selected);
        style.Setters.Add(new Setter(TemplateProperty, template));
        style.Setters.Add(new Setter(ForegroundProperty, Brushes.White));
        style.Setters.Add(new Setter(CursorProperty, Cursors.Hand));
        return style;
    }

    private sealed class ResultRow(LauncherEntry entry) : INotifyPropertyChanged
    {
        private ImageSource? _icon;

        public LauncherEntry Entry { get; } = entry;
        public string Name => Entry.Name;
        public string Detail => Entry.Kind == LauncherEntryKind.App ? "Aplikasi" : Entry.Target;

        public ImageSource? Icon
        {
            get => _icon;
            set { _icon = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
