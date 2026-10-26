using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Windock.Core.Apps;
using Windock.Core.Launcher;
using Windock.Core.Logging;
using Windock.Core.Settings;
using Windock.Interop;

namespace Windock.Launcher;

/// What the launcher asks the rest of Windock to do.
internal sealed record LauncherActions(
    Action<LauncherEntry> Open,
    Action<string> Pin,
    Action<string> Reveal,
    Action<string, LauncherCategory> SetCategory,
    Action OpenSettings);

/// Alt+Space. Grid layout: search bar, tabs (Frequent, All, categories) and an icon grid that typing filters in
/// place. List layout: the search box with a short result list. Built when opened (with a fresh index) and thrown
/// away when closed, icons included.
internal sealed class LauncherWindow : Window
{
    private const double TileWidth = 104, TileHeight = 100, GridRows = 4;
    private const int GridSearchResults = 60;
    private const string IconPrefix = "launcher:";

    private readonly IconProvider _icons;
    private readonly LauncherSettings _settings;
    private readonly UsageStore _usage;
    private readonly LauncherActions _actions;
    private readonly ILog _log;
    private readonly Brush _accent;
    private readonly TextBox _query = new();
    private readonly TextBlock _placeholder = new();
    private readonly ListBox _list = new();
    private readonly WrapPanel _tabsPanel = new();
    private readonly TextBlock _hint = new();
    private readonly ObservableCollection<ResultRow> _rows = new();
    private readonly List<Tab> _tabs = new();
    private IReadOnlyList<LauncherEntry> _entries = Array.Empty<LauncherEntry>();
    private List<LauncherEntry> _apps = new();
    private IReadOnlyList<UsageRecord> _history = Array.Empty<UsageRecord>();
    private IReadOnlyList<LauncherEntry> _frequent = Array.Empty<LauncherEntry>();
    private int _tab;
    private bool _indexReady;
    private bool _closing;

    private bool IsGrid => _settings.Layout == LauncherLayout.Grid;

    public LauncherWindow(IconProvider icons, LauncherSettings settings, UsageStore usage, LauncherActions actions, ILog log)
    {
        _icons = icons;
        _settings = settings;
        _usage = usage;
        _actions = actions;
        _log = log;
        _accent = Application.Current.TryFindResource("Windock.Accent") as Brush ?? Brushes.SteelBlue;
        Title = "Windock Launcher";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Width = IsGrid ? settings.Columns * TileWidth + 44 : 640;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
        Top = SystemParameters.PrimaryScreenHeight * (IsGrid ? 0.14 : 0.2);

        var panel = new StackPanel();
        panel.Children.Add(SearchRow());
        if (IsGrid)
        {
            _tabsPanel.Margin = new Thickness(14, 0, 14, 8);
            panel.Children.Add(_tabsPanel);
        }
        panel.Children.Add(BuildList());
        _hint.Foreground = Brushes.White;
        _hint.Opacity = 0.55;
        _hint.FontSize = 12;
        _hint.Margin = new Thickness(20, 4, 16, 12);
        panel.Children.Add(_hint);
        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x22, 0x22, 0x24)), CornerRadius = new CornerRadius(14),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(1), Child = panel,
        };

        Deactivated += (_, _) => CloseOnce();
        Closing += (_, _) => _closing = true;
        Closed += (_, _) => _icons.Forget(IconPrefix);
        Loaded += (_, _) =>
        {
            Activate();
            _query.Focus();
            Keyboard.Focus(_query);
            // Line the placeholder up with where typed text (and the caret) really starts.
            var start = _query.GetRectFromCharacterIndex(0);
            if (!start.IsEmpty) _placeholder.Margin = new Thickness(start.Left, 0, 0, 0);
        };
        _hint.Text = "Loading apps…";
        LoadIndex();
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

    private FrameworkElement SearchRow()
    {
        _query.FontSize = IsGrid ? 18 : 20;
        _query.Foreground = Brushes.White;
        _query.CaretBrush = Brushes.White;
        _query.Background = Brushes.Transparent;
        _query.BorderThickness = new Thickness(0);
        _query.VerticalContentAlignment = VerticalAlignment.Center;
        _query.TextChanged += (_, _) =>
        {
            _placeholder.Visibility = _query.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            Refresh();
        };
        _query.PreviewKeyDown += OnKey;

        _placeholder.Text = IsGrid ? "Search apps…" : "Search apps and files…";
        _placeholder.FontSize = _query.FontSize;
        _placeholder.Foreground = Brushes.White;
        _placeholder.Opacity = 0.4;
        _placeholder.IsHitTestVisible = false;
        _placeholder.VerticalAlignment = VerticalAlignment.Center;

        var row = new DockPanel { Margin = new Thickness(16, 12, 12, 12) };
        var glyph = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 16, Foreground = Brushes.White, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        DockPanel.SetDock(glyph, System.Windows.Controls.Dock.Left);
        row.Children.Add(glyph);
        if (IsGrid)
        {
            var gear = new Button
            {
                Content = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 14, Foreground = Brushes.White },
                ToolTip = "Launcher settings", Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(8, 4, 8, 4),
                Cursor = Cursors.Hand, Focusable = false, Opacity = 0.7,
            };
            gear.Click += (_, _) =>
            {
                CloseOnce();
                _actions.OpenSettings();
            };
            DockPanel.SetDock(gear, System.Windows.Controls.Dock.Right);
            row.Children.Add(gear);
        }
        var box = new Grid();
        box.Children.Add(_placeholder);
        box.Children.Add(_query);
        row.Children.Add(box);
        return row;
    }

    private ListBox BuildList()
    {
        _list.ItemsSource = _rows;
        _list.Background = Brushes.Transparent;
        _list.BorderThickness = new Thickness(0);
        _list.Foreground = Brushes.White;
        _list.Focusable = false;
        _list.Margin = IsGrid ? new Thickness(14, 0, 14, 0) : new Thickness(6, 0, 6, 6);
        _list.ItemTemplate = IsGrid ? TileTemplate() : RowTemplate();
        _list.ItemContainerStyle = ItemStyle();
        if (IsGrid)
        {
            var grid = new FrameworkElementFactory(typeof(UniformGrid));
            grid.SetValue(UniformGrid.ColumnsProperty, _settings.Columns);
            _list.ItemsPanel = new ItemsPanelTemplate(grid);
            _list.Height = GridRows * TileHeight + 8;
        }
        _list.MouseUp += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Left && (e.OriginalSource as FrameworkElement)?.DataContext is ResultRow) OpenSelected();
        };
        _list.MouseRightButtonUp += (_, e) =>
        {
            if ((e.OriginalSource as FrameworkElement)?.DataContext is not ResultRow row) return;
            _list.SelectedItem = row;
            ShowMenu(row);
            e.Handled = true;
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
        return _list;
    }

    private async void LoadIndex()
    {
        try
        {
            var result = await LauncherIndex.BuildAsync(_settings.IncludeFiles, IsGrid && _settings.UseWindowsHistory, _settings.CategoryOverrides, _log);
            _entries = result.Entries;
            _history = result.WindowsHistory;
        }
        catch (Exception ex)
        {
            _log.Error("launcher index failed", ex);
            _entries = Array.Empty<LauncherEntry>();
        }
        if (_closing) return;
        _apps = _entries.Where(e => e.Kind == LauncherEntryKind.App && !AppCategorizer.IsClutter(e.Name))
            .OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        RankFrequent();
        BuildTabs();
        _tab = _frequent.Count > 0 ? 0 : Math.Min(1, _tabs.Count - 1);
        _indexReady = true;
        ShowTabs();
        Refresh();
    }

    private void RankFrequent()
    {
        if (!IsGrid) return;
        var records = _settings.TrackLaunches ? _usage.Records.Concat(_history) : _history;
        _frequent = UsageRanking.Frequent(_apps, records, _usage.HiddenNames, DateTime.UtcNow, _settings.FrequentCount);
    }

    private sealed record Tab(string Title, LauncherCategory? Category, bool Frequent = false);

    private void BuildTabs()
    {
        _tabs.Clear();
        if (!IsGrid) return;
        _tabs.Add(new Tab("Frequent", null, Frequent: true));
        _tabs.Add(new Tab("All", null));
        if (!_settings.ShowCategories) return;
        foreach (var category in Enum.GetValues<LauncherCategory>())
            if (_apps.Any(a => a.Category == category)) _tabs.Add(new Tab(category.ToString(), category));
    }

    private IReadOnlyList<LauncherEntry> TabItems(Tab tab) =>
        tab.Frequent ? _frequent
        : tab.Category is { } c ? _apps.Where(a => a.Category == c).ToList()
        : _apps;

    private void ShowTabs()
    {
        _tabsPanel.Children.Clear();
        for (var i = 0; i < _tabs.Count; i++)
        {
            var index = i;
            var selected = i == _tab && _query.Text.Length == 0;
            var chip = new Border
            {
                CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 4, 10, 5), Margin = new Thickness(0, 0, 6, 6), Cursor = Cursors.Hand,
                Background = selected ? _accent : new SolidColorBrush(Color.FromArgb(0x1E, 0xFF, 0xFF, 0xFF)),
                Child = new TextBlock { Text = _tabs[i].Title, Foreground = Brushes.White, FontSize = 12.5, Opacity = selected ? 1 : 0.8 },
            };
            chip.MouseLeftButtonUp += (_, _) => SelectTab(index);
            _tabsPanel.Children.Add(chip);
        }
    }

    private void SelectTab(int index)
    {
        if (_tabs.Count == 0) return;
        _tab = (index % _tabs.Count + _tabs.Count) % _tabs.Count;
        if (_query.Text.Length > 0) _query.Text = "";  // TextChanged refreshes
        else Refresh();
        ShowTabs();
    }

    private void Refresh()
    {
        _rows.Clear();
        var query = _query.Text;
        if (!_indexReady)
        {
            _hint.Text = "Loading apps…";
            return;
        }
        IReadOnlyList<LauncherEntry> items;
        if (!string.IsNullOrWhiteSpace(query)) items = LauncherSearch.Search(_entries, query, IsGrid ? GridSearchResults : _settings.MaxResults);
        else items = IsGrid && _tabs.Count > 0 ? TabItems(_tabs[_tab]) : Array.Empty<LauncherEntry>();

        var dip = IsGrid ? 48 : 32;
        var px = IconProvider.PixelSizeFor(dip, VisualTreeHelper.GetDpi(this).DpiScaleX);
        foreach (var entry in items)
        {
            var row = new ResultRow(entry);
            _rows.Add(row);
            _icons.Request(IconPrefix + entry.Target, entry.Target, IntPtr.Zero, px, (_, image) => row.Icon = image);
        }
        if (_rows.Count > 0)
        {
            _list.SelectedIndex = 0;
            _list.ScrollIntoView(_rows[0]);
        }
        if (IsGrid) ShowTabs();
        _hint.Text = HintFor(query, items.Count);
    }

    private string HintFor(string query, int count)
    {
        if (!string.IsNullOrWhiteSpace(query)) return count == 0 ? "No matches." : "Enter to open · arrows to choose · Esc to close";
        if (!IsGrid) return $"Type to search {_entries.Count} apps and files.";
        if (_tabs.Count > 0 && _tabs[_tab].Frequent && count == 0) return "Apps you open will show up here. · Tab for the next tab";
        return "Type to search · Tab for the next tab · right-click an app for more";
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
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
            case Key.Tab when IsGrid:
                e.Handled = true;
                SelectTab(_tab + (shift ? -1 : 1));
                break;
            case Key.Up or Key.Down or Key.Left or Key.Right when _rows.Count > 0 && (IsGrid || e.Key is Key.Up or Key.Down):
                e.Handled = true;
                var move = e.Key switch { Key.Up => GridMove.Up, Key.Down => GridMove.Down, Key.Left => GridMove.Left, _ => GridMove.Right };
                var next = IsGrid
                    ? GridNavigation.Move(_list.SelectedIndex, _rows.Count, _settings.Columns, move)
                    : (_list.SelectedIndex + (move == GridMove.Down ? 1 : -1) + _rows.Count) % _rows.Count;
                _list.SelectedIndex = next;
                _list.ScrollIntoView(_list.SelectedItem);
                break;
            case Key.PageDown when ctrl && IsGrid:
                e.Handled = true;
                SelectTab(_tab + 1);
                break;
            case Key.PageUp when ctrl && IsGrid:
                e.Handled = true;
                SelectTab(_tab - 1);
                break;
        }
    }

    private void OpenSelected()
    {
        if (_list.SelectedItem is not ResultRow row) return;
        CloseOnce();
        _actions.Open(row.Entry);
    }

    private void ShowMenu(ResultRow row)
    {
        var entry = row.Entry;
        var menu = new ContextMenu();
        void Add(string header, Action action)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }

        Add("Open", OpenSelected);
        if (entry.Kind == LauncherEntryKind.App && IsGrid)
        {
            var moveTo = new MenuItem { Header = "Move to category" };
            foreach (var category in Enum.GetValues<LauncherCategory>())
            {
                var item = new MenuItem { Header = category.ToString(), IsChecked = entry.Category == category };
                item.Click += (_, _) => MoveToCategory(entry, category);
                moveTo.Items.Add(item);
            }
            menu.Items.Add(moveTo);
        }
        if (entry.Kind == LauncherEntryKind.App && PinListEditor.CanPin(entry.Target)) Add("Keep in dock", () => _actions.Pin(entry.Target));
        if (File.Exists(entry.Target) || Directory.Exists(entry.Target))
            Add("Show in Explorer", () =>
            {
                CloseOnce();
                _actions.Reveal(entry.Target);
            });
        if (IsGrid && _query.Text.Length == 0 && _tabs.Count > 0 && _tabs[_tab].Frequent)
            Add("Remove from Frequent", () =>
            {
                _usage.Hide(entry.Name);
                RankFrequent();
                Refresh();
            });
        menu.PlacementTarget = _list.ItemContainerGenerator.ContainerFromItem(row) as UIElement ?? _list;
        menu.IsOpen = true;
    }

    private void MoveToCategory(LauncherEntry entry, LauncherCategory category)
    {
        _actions.SetCategory(entry.Name, category);
        var moved = entry with { Category = category };
        _apps = _apps.Select(a => ReferenceEquals(a, entry) || a.Name.Equals(entry.Name, StringComparison.OrdinalIgnoreCase) ? moved : a).ToList();
        _entries = _entries.Select(a => a.Kind == LauncherEntryKind.App && a.Name.Equals(entry.Name, StringComparison.OrdinalIgnoreCase) ? moved : a).ToList();
        _frequent = _frequent.Select(a => a.Name.Equals(entry.Name, StringComparison.OrdinalIgnoreCase) ? moved : a).ToList();
        var current = _tabs.Count > 0 ? _tabs[_tab] : null;
        BuildTabs();
        _tab = Math.Max(0, current is null ? 0 : _tabs.FindIndex(t => t == current));
        Refresh();
    }

    private static DataTemplate TileTemplate()
    {
        var root = new FrameworkElementFactory(typeof(StackPanel));
        root.SetValue(WidthProperty, TileWidth - 8);
        root.SetValue(HeightProperty, TileHeight - 8);
        root.SetValue(MarginProperty, new Thickness(0, 8, 0, 0));
        var image = new FrameworkElementFactory(typeof(Image));
        image.SetValue(WidthProperty, 48.0);
        image.SetValue(HeightProperty, 48.0);
        image.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        image.SetBinding(Image.SourceProperty, new System.Windows.Data.Binding(nameof(ResultRow.Icon)));
        var name = new FrameworkElementFactory(typeof(TextBlock));
        name.SetValue(TextBlock.FontSizeProperty, 12.0);
        name.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Center);
        name.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        name.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        name.SetValue(MaxHeightProperty, 32.0);
        name.SetValue(MarginProperty, new Thickness(4, 6, 4, 0));
        name.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(ResultRow.Name)));
        name.SetBinding(ToolTipProperty, new System.Windows.Data.Binding(nameof(ResultRow.Name)));
        root.AppendChild(image);
        root.AppendChild(name);
        return new DataTemplate { VisualTree = root };
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

    private Style ItemStyle()
    {
        var style = new Style(typeof(ListBoxItem));
        var template = new ControlTemplate(typeof(ListBoxItem));
        var border = new FrameworkElementFactory(typeof(Border), "Bd");
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(IsGrid ? 12 : 8));
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        border.SetValue(MarginProperty, new Thickness(IsGrid ? 3 : 0));
        border.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
        template.VisualTree = border;
        var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)), "Bd"));
        template.Triggers.Add(hover);
        var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Border.BackgroundProperty, _accent, "Bd"));
        template.Triggers.Add(selected);
        style.Setters.Add(new Setter(TemplateProperty, template));
        style.Setters.Add(new Setter(ForegroundProperty, Brushes.White));
        style.Setters.Add(new Setter(CursorProperty, Cursors.Hand));
        style.Setters.Add(new Setter(FocusableProperty, false));
        return style;
    }

    private sealed class ResultRow(LauncherEntry entry) : INotifyPropertyChanged
    {
        private ImageSource? _icon;

        public LauncherEntry Entry { get; } = entry;
        public string Name => Entry.Name;
        public string Detail => Entry.Kind == LauncherEntryKind.App ? "App" : Entry.Target;

        public ImageSource? Icon
        {
            get => _icon;
            set { _icon = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
