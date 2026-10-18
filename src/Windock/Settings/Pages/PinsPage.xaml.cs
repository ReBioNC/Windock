using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using Windock.Core.Settings;

namespace Windock.Settings.Pages;

internal partial class PinsPage : UserControl
{
    private readonly SettingsContext _context;
    private readonly PageSync _sync;
    private readonly ObservableCollection<PinRow> _rows = new();
    private Point _dragStart;
    private PinRow? _dragged;

    public PinsPage(SettingsContext context)
    {
        _context = context;
        InitializeComponent();
        List.ItemsSource = _rows;
        _sync = new PageSync(this, context.Store, Refresh);

        List.SelectionChanged += (_, _) => RemoveButton.IsEnabled = List.SelectedItem is not null;
        HiddenList.SelectionChanged += (_, _) => ShowAgainButton.IsEnabled = HiddenList.SelectedItem is not null;
        ExplorerMenu.Checked += (_, _) => _sync.Edit(s => s with { Behavior = s.Behavior with { ExplorerContextMenu = true } });
        ExplorerMenu.Unchecked += (_, _) => _sync.Edit(s => s with { Behavior = s.Behavior with { ExplorerContextMenu = false } });
        List.PreviewMouseLeftButtonDown += (_, e) =>
        {
            _dragStart = e.GetPosition(List);
            _dragged = (e.OriginalSource as FrameworkElement)?.DataContext as PinRow;
        };
        List.PreviewMouseMove += OnDragMove;
        List.Drop += OnDrop;
    }

    private void Refresh(DockSettings s)
    {
        var selected = (List.SelectedItem as PinRow)?.Path;
        _rows.Clear();
        foreach (var pin in s.Pins)
        {
            var resolution = _context.Pins.Resolve(pin);
            var row = new PinRow(pin.Path, resolution.DisplayName);
            _rows.Add(row);
            _context.Icons.Request("settings:" + pin.Path, resolution.IconPath, IntPtr.Zero, 32, (_, image) => row.Icon = image);
        }
        List.SelectedItem = _rows.FirstOrDefault(r => r.Path == selected);
        RemoveButton.IsEnabled = List.SelectedItem is not null;
        ExplorerMenu.IsChecked = s.Behavior.ExplorerContextMenu;

        HiddenList.ItemsSource = s.HiddenApps;
        HiddenList.Visibility = s.HiddenApps.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        HiddenEmpty.Visibility = s.HiddenApps.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        ShowAgainButton.Visibility = HiddenList.Visibility;
        ShowAgainButton.IsEnabled = HiddenList.SelectedItem is not null;
    }

    private void OnShowAgain(object sender, RoutedEventArgs e)
    {
        if (HiddenList.SelectedItem is not HiddenApp app) return;
        _sync.Edit(s => s with { HiddenApps = s.HiddenApps.Where(h => h != app).ToList() });
    }

    private void OnAdd(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Aplikasi (*.exe;*.lnk)|*.exe;*.lnk", DereferenceLinks = false, Title = "Tambah aplikasi ke Dock" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        var path = dialog.FileName;
        if (_context.Store.Current.Pins.Any(p => string.Equals(p.Path, path, StringComparison.OrdinalIgnoreCase)))
        {
            Message.Text = "Aplikasi ini sudah ada di Dock.";
            return;
        }
        Message.Text = "";
        _sync.Edit(s => s with { Pins = s.Pins.Append(new PinEntry(path)).ToList() });
    }

    private void OnRemove(object sender, RoutedEventArgs e)
    {
        if (List.SelectedItem is not PinRow row) return;
        _sync.Edit(s => s with { Pins = s.Pins.Where(p => p.Path != row.Path).ToList() });
    }

    private void OnResetPins(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show("Ganti daftar pin dengan pin awal?", "Windock", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (answer == MessageBoxResult.OK) _sync.Edit(s => s with { Pins = _context.DefaultPins() });
    }

    private void OnDragMove(object sender, MouseEventArgs e)
    {
        if (_dragged is null || e.LeftButton != MouseButtonState.Pressed) return;
        var delta = e.GetPosition(List) - _dragStart;
        if (Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var row = _dragged;
        _dragged = null;
        DragDrop.DoDragDrop(List, row, DragDropEffects.Move);
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(PinRow)) is not PinRow row) return;
        var target = (e.OriginalSource as FrameworkElement)?.DataContext as PinRow;
        var paths = _rows.Select(r => r.Path).ToList();
        paths.Remove(row.Path);
        var index = target is null ? paths.Count : paths.IndexOf(target.Path);
        if (index < 0) index = paths.Count;
        paths.Insert(index, row.Path);
        List.SelectedItem = null;
        _sync.Edit(s => s with { Pins = paths.Select(p => new PinEntry(p)).ToList() });
        List.SelectedItem = _rows.FirstOrDefault(r => r.Path == row.Path);
    }

    private sealed class PinRow(string path, string name) : INotifyPropertyChanged
    {
        private ImageSource? _icon;

        public string Path { get; } = path;
        public string Name { get; } = name;

        public ImageSource? Icon
        {
            get => _icon;
            set { _icon = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
