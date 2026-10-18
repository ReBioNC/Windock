using Windock.Core.Layout;

namespace Windock.Core.Tests.Layout;

public class DockLayoutEngineTests
{
    private const double FirstIconCenter = 867.5; // 831.5 plate X + 8 padding + 56/2

    /// n apps, then the trash separator and the recycle bin (same shape the dock model produces).
    private static LayoutInput Input(int apps, double? cursor = null, double progress = 1, double screen = 1920,
        bool magnification = true, double firstSlotProgress = 1)
    {
        var items = new List<LayoutItem>();
        for (var i = 0; i < apps; i++) items.Add(new LayoutItem($"app{i}", LayoutItemKind.Icon, i == 0 ? firstSlotProgress : 1));
        items.Add(new LayoutItem("sep:trash", LayoutItemKind.Separator, 1));
        items.Add(new LayoutItem("recyclebin", LayoutItemKind.Icon, 1));
        return new LayoutInput(items, IconSize: 48, MagnifiedSize: 80, MagnificationEnabled: magnification,
            CursorX: cursor, MagnifyProgress: progress, ScreenWidth: screen, WindowHeight: 118);
    }

    private static ItemLayout Item(LayoutResult r, string key) => r.Items.Single(i => i.Key == key);

    [Fact]
    public void Fixed_items_keep_their_width_and_never_magnify()
    {
        var input = Input(3, cursor: 1100);
        var items = input.Items.Append(new LayoutItem("status:clock", LayoutItemKind.Fixed, 1, Width: 60)).ToList();

        var r = DockLayoutEngine.Compute(input with { Items = items });

        var clock = Item(r, "status:clock");
        Assert.Equal(60, clock.Bounds.Width, 3);
        Assert.Equal(48, clock.Bounds.Height, 3);
        Assert.Equal(r.Plate.Right - DockMetrics.PlatePaddingH - (60 + DockMetrics.FixedSpacing) / 2, clock.Bounds.CenterX, 3);
        Assert.Equal(Item(r, "app0").Bounds.Bottom, clock.Bounds.Bottom, 3);
    }

    [Fact]
    public void Fixed_items_widen_the_plate()
    {
        var plain = DockLayoutEngine.Compute(Input(3));
        var input = Input(3);
        var withStatus = DockLayoutEngine.Compute(input with { Items = input.Items.Append(new LayoutItem("s", LayoutItemKind.Fixed, 1, 30)).ToList() });

        Assert.Equal(plain.Plate.Width + 30 + DockMetrics.FixedSpacing, withStatus.Plate.Width, 3);
    }

    [Fact]
    public void Metrics_match_design()
    {
        Assert.Equal(66, DockMetrics.PlateHeight(48));
        Assert.Equal(118, DockMetrics.WindowHeight(48, 80));
        Assert.Equal(70, DockMetrics.ReservedThickness(48));
    }

    [Fact]
    public void Unmagnified_plate_is_centered_with_design_width()
    {
        var r = DockLayoutEngine.Compute(Input(3));

        Assert.Equal(257, r.Plate.Width, 3);
        Assert.Equal(831.5, r.Plate.X, 3);
        Assert.Equal(48, r.Plate.Y, 3);
        Assert.Equal(66, r.Plate.Height, 3);
        Assert.All(r.Items.Where(i => i.Kind == LayoutItemKind.Icon), i =>
        {
            Assert.Equal(48, i.Bounds.Width, 3);
            Assert.Equal(54, i.Bounds.Y, 3);
        });
        Assert.Equal(FirstIconCenter, Item(r, "app0").Bounds.CenterX, 3);
        Assert.Equal(48, r.EffectiveIconSize);
    }

    [Fact]
    public void Icon_under_cursor_reaches_magnified_size()
    {
        var item = Item(DockLayoutEngine.Compute(Input(3, cursor: FirstIconCenter)), "app0");

        Assert.Equal(80, item.Bounds.Width, 3);
        Assert.Equal(80, item.Bounds.Height, 3);
        Assert.Equal(102, item.Bounds.Bottom, 3);
    }

    [Fact]
    public void Icon_at_half_reach_is_halfway()
    {
        var item = Item(DockLayoutEngine.Compute(Input(3, cursor: FirstIconCenter + 60)), "app0");

        Assert.Equal(64, item.Bounds.Width, 3);
    }

    [Fact]
    public void Icon_beyond_reach_is_unchanged()
    {
        var item = Item(DockLayoutEngine.Compute(Input(3, cursor: FirstIconCenter + 120)), "app0");

        Assert.Equal(48, item.Bounds.Width, 3);
    }

    [Fact]
    public void Magnify_progress_scales_effect()
    {
        var item = Item(DockLayoutEngine.Compute(Input(3, cursor: FirstIconCenter, progress: 0.5)), "app0");

        Assert.Equal(64, item.Bounds.Width, 3);
    }

    [Fact]
    public void Disabled_magnification_ignores_cursor()
    {
        var r = DockLayoutEngine.Compute(Input(3, cursor: FirstIconCenter, magnification: false));

        Assert.All(r.Items.Where(i => i.Kind == LayoutItemKind.Icon), i => Assert.Equal(48, i.Bounds.Width, 3));
    }

    [Theory]
    [InlineData(FirstIconCenter)]
    [InlineData(1000)]
    [InlineData(5)]
    public void Plate_stays_centered_while_magnified(double cursor)
    {
        var r = DockLayoutEngine.Compute(Input(3, cursor: cursor));

        Assert.Equal(960, r.Plate.CenterX, 3);
    }

    [Fact]
    public void Indicator_sits_below_icon()
    {
        var item = Item(DockLayoutEngine.Compute(Input(3, cursor: FirstIconCenter)), "app0");

        Assert.Equal(item.Bounds.Bottom + 5, item.IndicatorCenterY, 3);
        Assert.Equal(item.Bounds.CenterX, item.IndicatorCenterX, 3);
    }

    [Fact]
    public void Separator_is_thin_line_centered_in_its_slot()
    {
        var r = DockLayoutEngine.Compute(Input(3));
        var sep = Item(r, "sep:trash");
        var app2 = Item(r, "app2");

        Assert.Equal(1, sep.Bounds.Width, 3);
        Assert.Equal(28.8, sep.Bounds.Height, 3);
        Assert.Equal(app2.Bounds.Right + 4 + 8.5, sep.Bounds.CenterX, 3);
        Assert.Equal(app2.Bounds.Y + 24, sep.Bounds.Y + sep.Bounds.Height / 2, 3);
    }

    [Fact]
    public void Growing_slot_contributes_partial_width()
    {
        var r = DockLayoutEngine.Compute(Input(3, firstSlotProgress: 0.5));

        Assert.Equal(257 - 28, r.Plate.Width, 3);
        Assert.Equal(0.5, Item(r, "app0").SlotProgress);
    }

    [Fact]
    public void Overflowing_dock_shrinks_icons_to_fit_screen()
    {
        var r = DockLayoutEngine.Compute(Input(38, screen: 1280));   // 39 icons (38 apps + recycle) + 1 separator

        Assert.True(r.Plate.Width <= 1280 - 16 + 0.001);
        Assert.True(r.Plate.X >= 8 - 0.001);
        Assert.True(r.EffectiveIconSize < 48);
        Assert.Equal(r.EffectiveIconSize, Item(r, "app0").Bounds.Width, 3);
    }

    [Fact]
    public void Active_zone_covers_magnified_icons_when_pointer_inside()
    {
        var r = DockLayoutEngine.Compute(Input(3, cursor: FirstIconCenter));

        Assert.True(r.ActiveZone.Y <= r.Items.Min(i => i.Bounds.Y));
        Assert.Equal(118, r.ActiveZone.Bottom, 3);
        Assert.Equal(r.Plate.X, r.ActiveZone.X, 3);
        Assert.Equal(r.Plate.Width, r.ActiveZone.Width, 3);
    }

    [Fact]
    public void Active_zone_is_plate_down_to_screen_edge_when_idle()
    {
        var r = DockLayoutEngine.Compute(Input(3, progress: 0));

        Assert.Equal(new RectD(r.Plate.X, r.Plate.Y, r.Plate.Width, 118 - r.Plate.Y), r.ActiveZone);
    }
}
