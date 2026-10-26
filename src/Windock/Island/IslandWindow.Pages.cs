using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Windock.Core.Island;
using MediaState = Windock.Core.Island.MediaState;

namespace Windock.Island;

/// What the island draws: the compact pill rows and the pages of the open card.
internal sealed partial class IslandWindow
{
    private const string Bolt = "";
    private const double ProgressWidth = 420, ProgressWidthWithLyrics = 170;
    private const string LyricsGlyph = "\uE8F2";
    private static readonly Brush LyricDim = Frozen(Color.FromRgb(0x5A, 0x5A, 0x5A));
    private static readonly Brush Lavender = Frozen(Color.FromRgb(0xAF, 0xA9, 0xEC));
    private static readonly Brush FocusButton = Frozen(Color.FromRgb(0x3C, 0x34, 0x89));
    private const double RingSize = 72, RingStroke = 6;
    private const int MaxDeviceRows = 6;
    private static readonly Brush Secondary = Frozen(Color.FromRgb(0xA8, 0xA8, 0xA8));
    private static readonly Brush Track = Frozen(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF));
    private static readonly Brush Accent = Frozen(Color.FromRgb(0x5D, 0xCA, 0xA5));
    private static readonly Brush Sky = Frozen(Color.FromRgb(0x85, 0xB7, 0xEB));
    private static readonly Brush Tile = Frozen(Color.FromRgb(0x1C, 0x1C, 0x1C));
    private static readonly Brush TabIdle = Frozen(Color.FromRgb(0x8A, 0x8A, 0x8A));
    private static readonly Brush TabSelected = Frozen(Color.FromRgb(0x2A, 0x2A, 0x2A));
    private static readonly FontFamily Icons = new("Segoe MDL2 Assets");
    private static readonly FontFamily Symbols = new("Segoe UI Symbol");

    private Storyboard? _bars;
    private Border? _progressFill;
    private TextBlock? _progressText, _durationText;
    private double _progressWidth = ProgressWidth;
    private IReadOnlyList<LyricLine>? _lyricLines;
    private readonly TextBlock[] _lyricBlocks = new TextBlock[4];
    private int _lyricIndex = int.MinValue;
    private TextBlock? _focusCompactText, _focusPageText;
    private bool _addingTask;
    private static readonly Brush Field = Frozen(Color.FromRgb(0x16, 0x16, 0x16));
    private static readonly Brush Done = Frozen(Color.FromRgb(0x6A, 0x6A, 0x6A));
    private const int MaxTaskRows = 3;
    private Path? _focusArc;

    // ---- compact ----

    private FrameworkElement BuildCompact(IslandView v)
    {
        ResetProgress();
        return v.Mode switch
        {
            IslandMode.Event when v.Event is { } e => EventRow(e),
            _ when v.Focus is { Running: true } focus => FocusRow(focus, v.Mood),
            IslandMode.Media when v.Media is { } m => MediaRow(m, v.Cover, v.Mood),
            _ => IdleRow(v),
        };
    }

    private static FrameworkElement IdleRow(IslandView v)
    {
        var row = Row();
        if (v.Clock is { } clock) row.Children.Add(Text(clock, 13, FontWeights.SemiBold));
        if (v.Date is { } date) row.Children.Add(Spaced(Text(date, 13, brush: Secondary), row));
        if (v.BatteryGlyph is { } glyph)
        {
            var battery = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (v.BatteryPercent is { } percent) battery.Children.Add(new Border { Child = Text(percent, 12, brush: Secondary), Margin = new Thickness(0, 0, 4, 0) });
            battery.Children.Add(Glyph(glyph, 15));
            row.Children.Add(Spaced(battery, row));
        }
        if (v.Mood is { } mood) row.Children.Add(Spaced(new WadoMascot(mood, 24), row, 12));
        return row;
    }

    private FrameworkElement MediaRow(MediaState m, ImageSource? cover, WadoMood? mood)
    {
        var row = new DockPanel { Margin = new Thickness(7, 0, 12, 0), Height = CompactHeight, LastChildFill = false };
        var art = Cover(cover, 22, 6);
        art.MouseLeftButtonUp += (_, e) => { e.Handled = true; CoverClicked?.Invoke(); };
        DockPanel.SetDock(art, System.Windows.Controls.Dock.Left);
        row.Children.Add(art);
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(m.Playing ? BarsVisual() : Glyph(IslandGlyphs.Pause, 12, Secondary));
        if (mood is { } wado) right.Children.Add(new Border { Child = new WadoMascot(wado, 24), Margin = new Thickness(10, 0, 0, 0) });
        DockPanel.SetDock(right, System.Windows.Controls.Dock.Right);
        row.Children.Add(right);
        return row;
    }

    private static FrameworkElement EventRow(IslandEvent e)
    {
        var row = Row();
        row.Children.Add(Glyph(e.Glyph, 16));
        if (e.Level is { } level)
        {
            var track = new Grid { Width = 120, Height = 4, VerticalAlignment = VerticalAlignment.Center };
            track.Children.Add(new Border { Background = Track, CornerRadius = new CornerRadius(2) });
            track.Children.Add(new Border
            {
                Background = Brushes.White, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left,
                Width = Math.Clamp(level, 0, 1) * 120,
            });
            row.Children.Add(Spaced(track, row, 12));
            row.Children.Add(Spaced(new TextBlock
            {
                Text = e.Detail ?? $"{Math.Round(level * 100)}", Width = 34, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
            }, row, 6));
            return row;
        }
        var text = Text(e.Text, 13, FontWeights.SemiBold);
        text.MaxWidth = 200;
        row.Children.Add(Spaced(text, row, 10));
        if (e.Detail is { } detail)
        {
            var d = Text(detail, 13, brush: Secondary);
            d.MaxWidth = 150;
            row.Children.Add(Spaced(d, row, 10));
        }
        return row;
    }

    /// A running focus timer takes the pill: stopwatch and countdown (lavender while focusing, green on a break).
    private FrameworkElement FocusRow(FocusView focus, WadoMood? mood)
    {
        var row = Row();
        row.Children.Add(Glyph("\uE916", 15, focus.Phase == FocusPhase.Focus ? Lavender : Accent));
        _focusCompactText = Text("", 13, FontWeights.SemiBold);
        row.Children.Add(Spaced(_focusCompactText, row, 10));
        if (mood is { } wado) row.Children.Add(Spaced(new WadoMascot(wado, 24), row, 12));
        return row;
    }

    // ---- pages ----

    private FrameworkElement FocusPage(FocusView focus)
    {
        var panel = new DockPanel { Margin = new Thickness(14, 4, 14, 6) };
        var ring = new Grid { Width = RingSize, Height = RingSize, Margin = new Thickness(0, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center };
        ring.Children.Add(new Ellipse { Stroke = TabSelected, StrokeThickness = RingStroke });
        _focusArc = new Path
        {
            Stroke = focus.Phase == FocusPhase.Focus ? Lavender : Accent, StrokeThickness = RingStroke,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
        };
        ring.Children.Add(_focusArc);
        ring.Children.Add(new TextBlock
        {
            Text = "\uE916", FontFamily = Icons, FontSize = 20, Foreground = focus.Phase == FocusPhase.Focus ? Lavender : Accent,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        });
        DockPanel.SetDock(ring, System.Windows.Controls.Dock.Left);
        panel.Children.Add(ring);

        var column = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var title = focus.Phase switch
        {
            FocusPhase.ShortBreak => "Short break",
            FocusPhase.LongBreak => "Long break",
            _ => $"Focus session {focus.Session} of {focus.Sessions}",
        };
        column.Children.Add(Text(title, 12, brush: Secondary));
        _focusPageText = Text("", 30, FontWeights.Light);
        column.Children.Add(_focusPageText);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        buttons.Children.Add(PillButton(focus.Running ? "\uE769  Pause" : "\uE768  Start", FocusButton, () => FocusStartPause?.Invoke()));
        buttons.Children.Add(PillButton("\uE72C  Reset", Tile, () => FocusReset?.Invoke()));
        buttons.Children.Add(PillButton(focus.Phase == FocusPhase.Focus ? $"\uE7F6  {focus.NextBreakMinutes} min break" : "\uE893  Skip break", Tile, () => FocusBreak?.Invoke()));
        column.Children.Add(buttons);
        panel.Children.Add(column);
        return panel;
    }

    // ---- tasks and notes ----

    private FrameworkElement TasksPage(TasksView tasks)
    {
        var panel = new StackPanel { Margin = new Thickness(14, 4, 14, 6) };
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var add = PillButton("\uE710  Task", Tile, () =>
        {
            _addingTask = true;
            Rebuild();
        });
        add.Margin = new Thickness(0);
        DockPanel.SetDock(add, System.Windows.Controls.Dock.Right);
        header.Children.Add(add);
        var title = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.White };
        title.Inlines.Add(new System.Windows.Documents.Run("Today") { FontSize = 15, FontWeight = FontWeights.SemiBold });
        var open = tasks.Items.Count(t => !t.Done);
        title.Inlines.Add(new System.Windows.Documents.Run(open == 0 ? "   No tasks" : $"   {open} task{(open == 1 ? "" : "s")}") { FontSize = 12, Foreground = Secondary });
        header.Children.Add(title);
        panel.Children.Add(header);

        if (_addingTask)
        {
            var (field, box) = InputField("Call mom 14:30   (a time at the end sets a reminder)", false);
            box.KeyDown += (_, e) =>
            {
                if (e.Key != Key.Enter) return;
                e.Handled = true;
                var text = box.Text.Trim();
                _addingTask = false;
                EndTyping();
                if (text.Length > 0) TaskAdded?.Invoke(text);
                else Rebuild();
            };
            panel.Children.Add(field);
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () => BeginTyping(box));
        }
        if (tasks.Items.Count == 0 && !_addingTask)
        {
            var hint = Text("Nothing planned. Add a task, and put a time at the end to be reminded.", 13, brush: Secondary);
            hint.TextWrapping = TextWrapping.Wrap;
            panel.Children.Add(hint);
            return panel;
        }
        foreach (var task in tasks.Items.Take(_addingTask ? MaxTaskRows - 1 : MaxTaskRows)) panel.Children.Add(TaskRow(task));
        var more = tasks.Items.Count - (_addingTask ? MaxTaskRows - 1 : MaxTaskRows);
        if (more > 0) panel.Children.Add(Text($"+{more} more", 12, brush: Secondary));
        return panel;
    }

    private void CancelTaskInput()
    {
        if (!_addingTask) return;
        _addingTask = false;
        Rebuild();
    }

    private FrameworkElement TaskRow(IslandTask task)
    {
        var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2), Background = Brushes.Transparent };
        var check = new TextBlock
        {
            Text = task.Done ? "\u2611" : "\u2610", FontFamily = Symbols, FontSize = 16, Foreground = task.Done ? Accent : Brushes.White,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), Cursor = Cursors.Hand,
        };
        check.MouseLeftButtonUp += (_, e) => { e.Handled = true; TaskToggled?.Invoke(task.Id); };
        DockPanel.SetDock(check, System.Windows.Controls.Dock.Left);
        row.Children.Add(check);
        var delete = Glyph("\uE711", 11, Secondary);
        delete.Cursor = Cursors.Hand;
        delete.Visibility = Visibility.Hidden;
        delete.Margin = new Thickness(10, 0, 0, 0);
        delete.MouseLeftButtonUp += (_, e) => { e.Handled = true; TaskDeleted?.Invoke(task.Id); };
        row.MouseEnter += (_, _) => delete.Visibility = Visibility.Visible;
        row.MouseLeave += (_, _) => delete.Visibility = Visibility.Hidden;
        DockPanel.SetDock(delete, System.Windows.Controls.Dock.Right);
        row.Children.Add(delete);
        if (task.Due is { } due)
        {
            var time = Text(due.ToString(@"hh\:mm"), 12, brush: Secondary);
            time.Margin = new Thickness(10, 0, 0, 0);
            DockPanel.SetDock(time, System.Windows.Controls.Dock.Right);
            row.Children.Add(time);
        }
        var text = Text(task.Text, 13, brush: task.Done ? Done : Brushes.White);
        if (task.Done) text.TextDecorations = TextDecorations.Strikethrough;
        row.Children.Add(text);
        return row;
    }

    private FrameworkElement NotesPage(NotesView notes)
    {
        var panel = new StackPanel { Margin = new Thickness(14, 4, 14, 6) };
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var tools = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        tools.Children.Add(IconButton("\uE76B", "Previous note", () => NotePrevious?.Invoke(), notes.Index > 0));
        tools.Children.Add(IconButton("\uE76C", "Next note", () => NoteNext?.Invoke(), notes.Index < notes.Count - 1));
        tools.Children.Add(IconButton("\uE74D", "Delete this note", () => NoteDeleted?.Invoke(), notes.Count > 1 || notes.Text.Length > 0));
        var add = PillButton("\uE710  New", Tile, () => NoteAdded?.Invoke());
        add.Margin = new Thickness(6, 0, 0, 0);
        tools.Children.Add(add);
        DockPanel.SetDock(tools, System.Windows.Controls.Dock.Right);
        header.Children.Add(tools);
        var title = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.White };
        title.Inlines.Add(new System.Windows.Documents.Run("Quick note") { FontSize = 15, FontWeight = FontWeights.SemiBold });
        title.Inlines.Add(new System.Windows.Documents.Run($"   {notes.Index + 1} of {notes.Count}") { FontSize = 12, Foreground = Secondary });
        header.Children.Add(title);
        panel.Children.Add(header);

        var (field, box) = InputField("Jot something down\u2026", true);
        box.Text = notes.Text;
        var footer = Text(Count(notes.Text), 11, brush: Secondary);
        footer.Margin = new Thickness(2, 4, 0, 0);
        var index = notes.Index;
        box.TextChanged += (_, _) =>
        {
            footer.Text = "Saved \u00b7 " + Count(box.Text);
            NoteEdited?.Invoke(index, box.Text);
        };
        panel.Children.Add(field);
        panel.Children.Add(footer);
        return panel;
    }

    private static string Count(string text) => text.Length == 1 ? "1 character" : $"{text.Length} characters";

    /// A dark text box with a grey placeholder; clicking it lets the island take the keyboard.
    private (FrameworkElement Field, TextBox Box) InputField(string placeholder, bool multiline)
    {
        var box = new TextBox
        {
            Background = Brushes.Transparent, Foreground = Brushes.White, CaretBrush = Brushes.White, BorderThickness = new Thickness(0),
            FontSize = 13, Padding = new Thickness(0), AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            Height = multiline ? 46 : double.NaN, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            SelectionBrush = Sky,
        };
        var hint = new TextBlock { Text = placeholder, FontSize = 13, Foreground = LyricDim, IsHitTestVisible = false };
        void SyncHint() => hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        box.TextChanged += (_, _) => SyncHint();
        SyncHint();
        box.PreviewMouseLeftButtonDown += (_, _) => BeginTyping(box);
        var grid = new Grid();
        grid.Children.Add(hint);
        grid.Children.Add(box);
        return (new Border { Background = Field, CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 7, 10, 7), Child = grid }, box);
    }

    private static Border IconButton(string glyph, string tooltip, Action click, bool enabled)
    {
        var button = ControlButton(glyph, 12, click);
        button.Width = 30;
        button.Height = 26;
        button.ToolTip = tooltip;
        button.IsEnabled = enabled;
        if (!enabled) ((TextBlock)button.Child).Foreground = LyricDim;
        return button;
    }

    /// A rounded text button; text may start with a Segoe MDL2 glyph.
    private static Border PillButton(string text, Brush background, Action click)
    {
        var label = new TextBlock { FontSize = 12, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center };
        var glyphEnd = text.IndexOf("  ", StringComparison.Ordinal);
        if (glyphEnd > 0)
        {
            label.Inlines.Add(new System.Windows.Documents.Run(text[..glyphEnd]) { FontFamily = Icons, FontSize = 11 });
            label.Inlines.Add(new System.Windows.Documents.Run(text[glyphEnd..]));
        }
        else label.Text = text;
        var button = new Border
        {
            Child = label, Background = background, CornerRadius = new CornerRadius(13), Padding = new Thickness(12, 5, 12, 5),
            Margin = new Thickness(0, 0, 8, 0), Cursor = Cursors.Hand,
        };
        button.MouseEnter += (_, _) => button.Opacity = 0.85;
        button.MouseLeave += (_, _) => button.Opacity = 1;
        button.MouseLeftButtonUp += (_, e) => { e.Handled = true; click(); };
        return button;
    }

    /// The countdown in the pill and on the Focus page, and the ring (called each second while running).
    public void UpdateFocus(TimeSpan remaining, double progress)
    {
        var text = remaining.TotalHours >= 1 ? remaining.ToString(@"h\:mm\:ss") : $"{(int)remaining.TotalMinutes:00}:{remaining.Seconds:00}";
        if (_focusCompactText is not null) _focusCompactText.Text = text;
        if (_focusPageText is not null) _focusPageText.Text = text;
        if (_focusArc is not null) _focusArc.Data = RingArc(progress);
    }

    private static Geometry? RingArc(double progress)
    {
        const double r = (RingSize - RingStroke) / 2, c = RingSize / 2;
        if (progress <= 0.001) return null;
        if (progress >= 0.999) return new EllipseGeometry(new Point(c, c), r, r);
        var angle = progress * 2 * Math.PI;
        var end = new Point(c + r * Math.Sin(angle), c - r * Math.Cos(angle));
        var figure = new PathFigure { StartPoint = new Point(c, c - r), IsClosed = false };
        figure.Segments.Add(new ArcSegment(end, new Size(r, r), 0, progress > 0.5, SweepDirection.Clockwise, true));
        return new PathGeometry(new[] { figure });
    }

    private static FrameworkElement IdleCard(IslandView v)
    {
        var panel = new StackPanel { Margin = new Thickness(14, 4, 14, 6) };
        panel.Children.Add(Text(v.BigClock, 38, FontWeights.Light));
        panel.Children.Add(Text(v.LongDate, 13, brush: Secondary));
        if (v.BatteryLine is { } line)
        {
            var battery = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            if (v.BatteryGlyph is { } glyph) battery.Children.Add(new Border { Child = Glyph(glyph, 15, Secondary), Margin = new Thickness(0, 0, 8, 0) });
            battery.Children.Add(Text(line, 13, brush: Secondary));
            panel.Children.Add(battery);
        }
        return panel;
    }

    private FrameworkElement MusicPage(IslandView v)
    {
        if (v.Media is { } m) return v.Lyrics.Show ? MediaCardWithLyrics(m, v.Cover, v.Lyrics) : MediaCard(m, v.Cover);
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 10, 14, 6) };
        panel.Children.Add(Cover(null, 56, 12));
        var text = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(Text("Nothing playing", 14, FontWeights.SemiBold));
        text.Children.Add(Text("Play something in Spotify, a browser or a media player", 12, brush: Secondary));
        panel.Children.Add(text);
        return panel;
    }

    private FrameworkElement MediaCard(MediaState m, ImageSource? cover)
    {
        var panel = new StackPanel { Margin = new Thickness(14, 6, 14, 4) };
        _progressWidth = ProgressWidth;

        var top = new DockPanel();
        var art = Cover(cover, 56, 12);
        art.MouseLeftButtonUp += (_, e) => { e.Handled = true; CoverClicked?.Invoke(); };
        DockPanel.SetDock(art, System.Windows.Controls.Dock.Left);
        top.Children.Add(art);
        var controls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        controls.Children.Add(LyricsButton(on: false));
        controls.Children.Add(ControlButton(IslandGlyphs.Previous, 16, () => PreviousClicked?.Invoke()));
        controls.Children.Add(ControlButton(m.Playing ? IslandGlyphs.Pause : IslandGlyphs.Play, 20, () => PlayPauseClicked?.Invoke()));
        controls.Children.Add(ControlButton(IslandGlyphs.Next, 16, () => NextClicked?.Invoke()));
        DockPanel.SetDock(controls, System.Windows.Controls.Dock.Right);
        top.Children.Add(controls);
        var titles = new StackPanel { Margin = new Thickness(12, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand, Background = Brushes.Transparent };
        titles.MouseLeftButtonUp += (_, e) => { e.Handled = true; CoverClicked?.Invoke(); };
        titles.Children.Add(Text(m.Title, 14, FontWeights.SemiBold));
        var sub = string.Join(" · ", new[] { m.Artist, m.App }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (sub.Length > 0) titles.Children.Add(Text(sub, 12, brush: Secondary));
        top.Children.Add(titles);
        panel.Children.Add(top);

        panel.Children.Add(ProgressRow(new Thickness(0, 12, 0, 0)));
        return panel;
    }

    /// Player at the left, the lyrics around the current line at the right.
    private FrameworkElement MediaCardWithLyrics(MediaState m, ImageSource? cover, LyricsView lyrics)
    {
        _progressWidth = ProgressWidthWithLyrics;
        var grid = new Grid { Margin = new Thickness(14, 4, 14, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(262) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());

        var player = new StackPanel();
        var top = new StackPanel { Orientation = Orientation.Horizontal };
        var art = Cover(cover, 44, 10);
        art.MouseLeftButtonUp += (_, e) => { e.Handled = true; CoverClicked?.Invoke(); };
        top.Children.Add(art);
        var titles = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Width = 200, Cursor = Cursors.Hand, Background = Brushes.Transparent };
        titles.MouseLeftButtonUp += (_, e) => { e.Handled = true; CoverClicked?.Invoke(); };
        titles.Children.Add(Text(m.Title, 14, FontWeights.SemiBold));
        if (m.Artist.Length > 0) titles.Children.Add(Text(m.Artist, 12, brush: Secondary));
        top.Children.Add(titles);
        player.Children.Add(top);
        player.Children.Add(ProgressRow(new Thickness(0, 8, 0, 0)));
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(-6, 2, 0, 0) };
        controls.Children.Add(LyricsButton(on: true));
        controls.Children.Add(ControlButton(IslandGlyphs.Previous, 15, () => PreviousClicked?.Invoke()));
        controls.Children.Add(ControlButton(m.Playing ? IslandGlyphs.Pause : IslandGlyphs.Play, 19, () => PlayPauseClicked?.Invoke()));
        controls.Children.Add(ControlButton(IslandGlyphs.Next, 15, () => NextClicked?.Invoke()));
        player.Children.Add(controls);
        grid.Children.Add(player);

        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(18, 0, 0, 0) };
        Grid.SetColumn(words, 1);
        _lyricLines = lyrics.Lines;
        _lyricIndex = int.MinValue;
        if (lyrics.Lines is null)
        {
            var message = Text(lyrics.Message ?? "", 13, brush: Secondary);
            message.TextWrapping = TextWrapping.Wrap;
            words.Children.Add(message);
        }
        else
        {
            for (var i = 0; i < _lyricBlocks.Length; i++)
            {
                _lyricBlocks[i] = new TextBlock { FontSize = 14, Foreground = LyricDim, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 2) };
                words.Children.Add(_lyricBlocks[i]);
            }
            ShowLyricLine(-1);
        }
        grid.Children.Add(words);
        return grid;
    }

    private DockPanel ProgressRow(Thickness margin)
    {
        var progress = new DockPanel { Margin = margin };
        _progressText = new TextBlock { FontSize = 11, Foreground = Secondary, Width = 36, VerticalAlignment = VerticalAlignment.Center };
        _durationText = new TextBlock { FontSize = 11, Foreground = Secondary, Width = 36, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(_progressText, System.Windows.Controls.Dock.Left);
        DockPanel.SetDock(_durationText, System.Windows.Controls.Dock.Right);
        progress.Children.Add(_progressText);
        progress.Children.Add(_durationText);
        var track = new Grid { Height = 4, Width = _progressWidth, VerticalAlignment = VerticalAlignment.Center };
        track.Children.Add(new Border { Background = Track, CornerRadius = new CornerRadius(2) });
        _progressFill = new Border { Background = Brushes.White, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
        track.Children.Add(_progressFill);
        progress.Children.Add(track);
        return progress;
    }

    private Border LyricsButton(bool on)
    {
        var button = ControlButton(LyricsGlyph, 15, () => LyricsToggled?.Invoke());
        button.ToolTip = on ? "Hide lyrics" : "Show lyrics";
        if (on) ((TextBlock)button.Child).Foreground = Sky;
        return button;
    }

    /// The line before (dim), the current one (white), and the next two (dim).
    private void ShowLyricLine(int index)
    {
        if (_lyricLines is null || index == _lyricIndex || _lyricBlocks[0] is null) return;
        _lyricIndex = index;
        for (var i = 0; i < _lyricBlocks.Length; i++)
        {
            var line = index - 1 + i;
            var block = _lyricBlocks[i];
            block.Text = line >= 0 && line < _lyricLines.Count ? _lyricLines[line].Text : "";
            var current = i == 1 && index >= 0;
            block.Foreground = current ? Brushes.White : LyricDim;
            block.FontWeight = current ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }

    private static FrameworkElement DevicesPage(IReadOnlyList<DeviceEntry>? devices)
    {
        var panel = new StackPanel { Margin = new Thickness(14, 4, 14, 6) };
        var header = new DockPanel();
        if (devices is { Count: > MaxDeviceRows })
        {
            var more = Text($"+{devices.Count - MaxDeviceRows} more", 12, brush: Secondary);
            DockPanel.SetDock(more, System.Windows.Controls.Dock.Right);
            header.Children.Add(more);
        }
        header.Children.Add(Text("Devices", 12, brush: Secondary));
        panel.Children.Add(header);
        if (devices is null || devices.Count == 0)
        {
            panel.Children.Add(new Border { Margin = new Thickness(0, 10, 0, 0), Child = Text(devices is null ? "Looking for devices…" : "No devices connected", 13, brush: Secondary) });
            return panel;
        }
        var grid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 2, 0, 0) };
        foreach (var d in devices.Take(MaxDeviceRows))
        {
            var row = new DockPanel { Margin = new Thickness(0, 6, 12, 0) };
            var icon = Glyph(DeviceBurst.Glyph(d.Kind), 15);
            icon.Width = 24;
            DockPanel.SetDock(icon, System.Windows.Controls.Dock.Left);
            row.Children.Add(icon);
            var extra = string.Join(" · ", new[] { d.Battery is { } b ? $"{b}%" : null, d.Note }.Where(x => x is not null));
            if (extra.Length > 0)
            {
                var right = Text(extra, 12, brush: Secondary);
                right.Margin = new Thickness(8, 0, 0, 0);
                DockPanel.SetDock(right, System.Windows.Controls.Dock.Right);
                row.Children.Add(right);
            }
            row.Children.Add(Text(d.Name ?? d.Kind.ToString(), 13));
            grid.Children.Add(row);
        }
        panel.Children.Add(grid);
        return panel;
    }

    /// Now at the left (icon, temperature, place, high/low and the rain note), the next hours as tiles at the right.
    private static FrameworkElement WeatherPage(WeatherView w)
    {
        var panel = new DockPanel { Margin = new Thickness(14, 4, 14, 6) };
        if (w.Report is not { } r)
        {
            var box = new StackPanel();
            box.Children.Add(Text("Weather", 12, brush: Secondary));
            var message = Text(w.Message ?? "Loading weather…", 13, brush: Secondary);
            message.TextWrapping = TextWrapping.Wrap;
            box.Children.Add(new Border { Margin = new Thickness(0, 10, 0, 0), Child = message });
            panel.Children.Add(box);
            return panel;
        }
        var (condition, glyph) = WeatherCodes.Describe(r.Code, r.IsDay);
        var tiles = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        tiles.Children.Add(HourTile("Now", glyph, r.Temperature, now: true));
        foreach (var h in r.Hours.Take(4)) tiles.Children.Add(HourTile(h.Time.ToString("HH:mm"), WeatherCodes.Describe(h.Code, h.IsDay).Glyph, h.Temperature, now: false));
        DockPanel.SetDock(tiles, System.Windows.Controls.Dock.Right);
        panel.Children.Add(tiles);

        var icon = new TextBlock { Text = glyph, FontFamily = Symbols, FontSize = 34, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        DockPanel.SetDock(icon, System.Windows.Controls.Dock.Left);
        panel.Children.Add(icon);
        var now = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        now.Children.Add(Text(WeatherCodes.Temperature(r.Temperature), 30, FontWeights.Light));
        now.Children.Add(Text(string.Join(" · ", new[] { w.Place, condition }.Where(x => !string.IsNullOrWhiteSpace(x))), 12, brush: Secondary));
        var range = $"H {WeatherCodes.Temperature(r.High)} · L {WeatherCodes.Temperature(r.Low)}" + (r.RainNote is { } note ? $" · {note}" : "");
        now.Children.Add(Text(range, 12, brush: Secondary));
        if (w.Stale) now.Children.Add(Text($"Offline · updated {r.FetchedAt:HH:mm}", 11, brush: Secondary));
        panel.Children.Add(now);
        return panel;
    }

    private static Border HourTile(string label, string glyph, double temperature, bool now)
    {
        var cell = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        cell.Children.Add(new TextBlock { Text = label, FontSize = 11, FontWeight = now ? FontWeights.SemiBold : FontWeights.Normal, Foreground = now ? Brushes.White : Secondary, HorizontalAlignment = HorizontalAlignment.Center });
        cell.Children.Add(new TextBlock { Text = glyph, FontFamily = Symbols, FontSize = 15, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 3, 0, 3) });
        cell.Children.Add(new TextBlock { Text = WeatherCodes.Temperature(temperature), FontSize = 12, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center });
        return new Border { Width = 50, Padding = new Thickness(0, 6, 0, 6), Margin = new Thickness(3, 0, 0, 0), CornerRadius = new CornerRadius(12), Background = Tile, Child = cell };
    }

    // ---- media progress ----

    /// The media card's progress bar and times (called each second while it is open and playing).
    public void UpdateProgress(TimeSpan position, TimeSpan duration)
    {
        if (_progressFill is null || _progressText is null || _durationText is null) return;
        var fraction = duration > TimeSpan.Zero ? Math.Clamp(position / duration, 0, 1) : 0;
        _progressFill.Width = fraction * _progressWidth;
        _progressText.Text = Time(position);
        _durationText.Text = Time(duration);
        // A quarter second early: the line appears as it is sung, not after.
        if (_lyricLines is not null) ShowLyricLine(Lrc.LineAt(_lyricLines, position + TimeSpan.FromMilliseconds(250)));
    }

    private void ResetProgress()
    {
        _progressFill = null;
        _progressText = _durationText = null;
        _lyricLines = null;
        Array.Clear(_lyricBlocks);
        _focusCompactText = _focusPageText = null;
        _focusArc = null;
    }

    private static Border ControlButton(string glyph, double size, Action click)
    {
        var hover = Frozen(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
        var button = new Border
        {
            Child = Glyph(glyph, size), Width = 40, Height = 36, CornerRadius = new CornerRadius(18), Margin = new Thickness(2, 0, 2, 0),
            Background = Brushes.Transparent, Cursor = Cursors.Hand,
        };
        ((TextBlock)button.Child).HorizontalAlignment = HorizontalAlignment.Center;
        button.MouseEnter += (_, _) => button.Background = hover;
        button.MouseLeave += (_, _) => button.Background = Brushes.Transparent;
        button.MouseLeftButtonUp += (_, e) => { e.Handled = true; click(); };
        return button;
    }

    // ---- music bars ----

    private FrameworkElement BarsVisual()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Height = 14 };
        _bars?.Stop(this);
        _bars = new Storyboard();
        Timeline.SetDesiredFrameRate(_bars, 20);  // a small layered window: 20 fps looks the same and costs a third
        var periods = new[] { 0.45, 0.32, 0.52, 0.38 };
        for (var i = 0; i < periods.Length; i++)
        {
            var scale = new ScaleTransform(1, 0.4);
            var bar = new Rectangle
            {
                Width = 3, Height = 14, RadiusX = 1.5, RadiusY = 1.5, Fill = Accent, Margin = new Thickness(i == 0 ? 0 : 2, 0, 0, 0),
                RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = scale,
            };
            panel.Children.Add(bar);
            var anim = new DoubleAnimation(0.25, 1, TimeSpan.FromSeconds(periods[i])) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
            Storyboard.SetTarget(anim, bar);
            Storyboard.SetTargetProperty(anim, new PropertyPath("RenderTransform.ScaleY"));
            _bars.Children.Add(anim);
        }
        return panel;
    }

    /// Bars move only while the compact media pill is on screen and playing.
    private void UpdateBars()
    {
        var run = _shown && IsVisible && _view is { Mode: IslandMode.Media, Expanded: false, Media.Playing: true };
        if (_bars is null) return;
        if (run) _bars.Begin(this, true);
        else _bars.Stop(this);
        if (!run && _view is not { Mode: IslandMode.Media, Expanded: false }) _bars = null;
    }

    // ---- helpers ----

    private static StackPanel Row() => new()
    {
        Orientation = Orientation.Horizontal, Margin = new Thickness(16, 0, 16, 0), Height = CompactHeight, HorizontalAlignment = HorizontalAlignment.Center,
    };

    private static FrameworkElement Spaced(FrameworkElement element, Panel row, double gap = 14)
    {
        if (row.Children.Count > 0) element.Margin = new Thickness(gap, element.Margin.Top, element.Margin.Right, element.Margin.Bottom);
        return element;
    }

    private static TextBlock Text(string text, double size, FontWeight? weight = null, Brush? brush = null) => new()
    {
        Text = text, FontSize = size, FontWeight = weight ?? FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis, Foreground = brush ?? Brushes.White,
    };

    private static TextBlock Glyph(string glyph, double size, Brush? brush = null) => new()
    {
        Text = glyph, FontFamily = Icons, FontSize = size, VerticalAlignment = VerticalAlignment.Center, Foreground = brush ?? Brushes.White,
    };

    private static Border Cover(ImageSource? image, double size, double radius) => new()
    {
        Width = size, Height = size, CornerRadius = new CornerRadius(radius), VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand,
        Background = image is null ? Frozen(Color.FromRgb(0x1D, 0x9E, 0x75)) : new ImageBrush(image) { Stretch = Stretch.UniformToFill },
        Child = image is null ? new TextBlock
        {
            Text = IslandGlyphs.Music, FontFamily = Icons, FontSize = size * 0.45, Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        } : null,
    };

    private static string Time(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

    private static SolidColorBrush Frozen(Color c)
    {
        var brush = new SolidColorBrush(c);
        brush.Freeze();
        return brush;
    }
}
