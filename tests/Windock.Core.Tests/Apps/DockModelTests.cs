using Windock.Core.Abstractions;
using Windock.Core.Apps;
using Windock.Core.Settings;
using Windock.Core.Tests.Fakes;

namespace Windock.Core.Tests.Apps;

public class DockModelTests
{
    private const string PathA = @"C:\Apps\A.exe", PathB = @"C:\Apps\B.exe", PathC = @"C:\Apps\C.exe";
    private static readonly string A = AppId.FromExePath(PathA).Value, B = AppId.FromExePath(PathB).Value, C = AppId.FromExePath(PathC).Value;

    private readonly FakeWindowSource _windows = new();
    private readonly FakePinResolver _resolver = new();

    private static WindowInfo Win(nint h, string exe, string title = "t") =>
        new(h, AppId.FromExePath(exe), Path.GetFileNameWithoutExtension(exe), title, exe, false);

    private DockModel Model(params string[] pins)
    {
        var m = new DockModel(_windows, _resolver);
        m.SetPins(pins.Select(p => new PinEntry(p)).ToList());
        return m;
    }

    private static string[] Keys(DockModel m) => m.Items.Select(i => i.Key).ToArray();

    private static DockItemsChangedEventArgs? Capture(DockModel m, Action act)
    {
        DockItemsChangedEventArgs? args = null;
        m.ItemsChanged += (_, e) => args = e;
        act();
        return args;
    }

    [Fact]
    public void Orders_pins_separator_running_trash()
    {
        _windows.Seed(Win(1, PathB), Win(2, PathC));

        var m = Model(PathA, PathB);

        Assert.Equal(new[] { A, B, "sep:pinned", C, "sep:trash", "recyclebin" }, Keys(m));
        Assert.True(m.Find(B)!.IsRunning);
        Assert.False(m.Find(A)!.IsRunning);
        Assert.True(m.Find(B)!.IsPinned);
        Assert.False(m.Find(C)!.IsPinned);
        Assert.Equal(DockItemKind.Separator, m.Find("sep:pinned")!.Kind);
        Assert.Equal(DockItemKind.RecycleBin, m.Find("recyclebin")!.Kind);
    }

    [Fact]
    public void No_pinned_separator_without_unpinned_running_apps()
    {
        _windows.Seed(Win(1, PathB));

        var m = Model(PathA, PathB);

        Assert.Equal(new[] { A, B, "sep:trash", "recyclebin" }, Keys(m));
    }

    [Fact]
    public void Unpinned_running_app_uses_window_details()
    {
        _windows.Seed(Win(2, PathC));

        var item = Model().Find(C)!;

        Assert.Equal("C", item.DisplayName);
        Assert.Equal(PathC, item.LaunchTarget);
        Assert.Equal(PathC, item.IconPath);
    }

    [Fact]
    public void Windows_of_same_app_group_into_one_item()
    {
        _windows.Seed(Win(1, PathC), Win(2, PathC));

        var m = Model();

        Assert.Single(m.Items, i => i.Kind == DockItemKind.App);
        Assert.Equal(2, m.Find(C)!.Windows.Count);
    }

    [Fact]
    public void Foreground_change_marks_active_and_reorders_windows_mru()
    {
        _windows.Seed(Win(1, PathB), Win(2, PathB), Win(3, PathC));
        var m = Model(PathB);
        _windows.SetForeground(3);

        var args = Capture(m, () => _windows.SetForeground(2));

        Assert.True(m.Find(B)!.IsActive);
        Assert.False(m.Find(C)!.IsActive);
        Assert.Equal(new nint[] { 2, 1 }, m.Find(B)!.Windows.Select(w => w.Handle));
        Assert.Contains(B, args!.Updated);
        Assert.Contains(C, args.Updated);
    }

    [Fact]
    public void Last_window_closing_removes_unpinned_item()
    {
        _windows.Seed(Win(2, PathC));
        var m = Model();

        var args = Capture(m, () => _windows.Remove(2));

        Assert.Equal(new[] { "sep:pinned", C }, args!.Removed); // separator goes with the last unpinned app
        Assert.Null(m.Find(C));
    }

    [Fact]
    public void Last_window_closing_keeps_pinned_item_but_not_running()
    {
        _windows.Seed(Win(1, PathB));
        var m = Model(PathB);

        var args = Capture(m, () => _windows.Remove(1));

        Assert.Equal(new[] { B }, args!.Updated);
        Assert.Empty(args.Removed);
        Assert.False(m.Find(B)!.IsRunning);
    }

    [Fact]
    public void New_window_of_new_app_is_added()
    {
        var m = Model(PathA);

        var args = Capture(m, () => _windows.Add(Win(5, PathC)));

        Assert.Contains(C, args!.Added);
        Assert.Contains("sep:pinned", args.Added);
    }

    [Fact]
    public void Duplicate_pins_collapse_to_one_item() // Review Focus 5
    {
        var m = Model(PathA, @"c:\apps\a.EXE");

        Assert.Single(m.Items, i => i.Kind == DockItemKind.App);
    }

    [Fact]
    public void Pin_with_missing_target_still_appears() // Review Focus 5
    {
        _resolver.Overrides[@"C:\Gone\Old.exe"] = new PinResolution(AppId.FromExePath(@"C:\Gone\Old.exe"), "Old", @"C:\Gone\Old.exe", @"C:\Gone\Old.exe", TargetExists: false);

        var item = Model(@"C:\Gone\Old.exe").Items[0];

        Assert.True(item.IsPinned);
        Assert.Equal("Old", item.DisplayName);
    }

    [Fact]
    public void Unpinning_running_app_moves_it_to_running_section()
    {
        _windows.Seed(Win(1, PathB));
        var m = Model(PathA, PathB);

        var args = Capture(m, () => m.SetPins(new[] { new PinEntry(PathA) }));

        Assert.Equal(new[] { A, "sep:pinned", B, "sep:trash", "recyclebin" }, Keys(m));
        Assert.Contains(B, args!.Updated);
        Assert.DoesNotContain(B, args.Added);
        Assert.DoesNotContain(B, args.Removed);
    }

    [Fact]
    public void Reordering_pins_reports_moved_items()
    {
        var m = Model(PathA, PathB);

        var args = Capture(m, () => m.SetPins(new[] { new PinEntry(PathB), new PinEntry(PathA) }));

        Assert.Equal(new[] { B, A, "sep:trash", "recyclebin" }, Keys(m));
        Assert.NotEmpty(args!.Updated);
    }

    [Fact]
    public void Title_change_updates_item()
    {
        _windows.Seed(Win(1, PathB, "old"));
        var m = Model(PathB);

        var args = Capture(m, () => _windows.Change(Win(1, PathB, "new")));

        Assert.Equal(new[] { B }, args!.Updated);
        Assert.Equal("new", m.Find(B)!.Windows[0].Title);
    }

    [Fact]
    public void Unchanged_events_raise_nothing()
    {
        _windows.Seed(Win(1, PathB));
        var m = Model(PathB);

        var args = Capture(m, () => m.SetPins(new[] { new PinEntry(PathB) }));

        Assert.Null(args);
    }

    [Fact]
    public void Window_without_readable_path_is_shown_by_process_identity() // Review Focus 4
    {
        _windows.Seed(new WindowInfo(9, AppId.Choose(null, null, "SecureApp", 9), "SecureApp", "Secure", null, false));

        var item = Model().Find("proc:secureapp")!;

        Assert.True(item.IsRunning);
        Assert.Null(item.LaunchTarget);
    }
}
