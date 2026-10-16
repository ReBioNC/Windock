using static Windock.Core.Layout.DockMetrics;

namespace Windock.Core.Layout;

/// Pure layout for a bottom dock: magnification, plate, indicators, active zone (DESIGN.md §2–3).
/// Coordinates are relative to the dock window, origin top-left.
public static class DockLayoutEngine
{
    public static LayoutResult Compute(LayoutInput input)
    {
        var items = input.Items;
        var ratio = input.MagnifiedSize / input.IconSize;
        var baseIcon = FitIconSize(input);
        var reach = ReachFactor * baseIcon;

        // 1. Unmagnified slot centres drive the magnification distance (no feedback loop).
        var baseWidths = items.Select(i => SlotWidth(i, baseIcon)).ToArray();
        var baseCenters = new double[items.Count];
        var x = (input.ScreenWidth - (2 * PlatePaddingH + baseWidths.Sum())) / 2 + PlatePaddingH;
        for (var i = 0; i < items.Count; i++)
        {
            baseCenters[i] = x + baseWidths[i] / 2;
            x += baseWidths[i];
        }

        // 2. Per-item icon size with magnification.
        var sizes = new double[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            var scale = 1.0;
            if (items[i].Kind == LayoutItemKind.Icon && input.MagnificationEnabled && input.CursorX is { } cursor)
            {
                var t = Math.Min(Math.Abs(cursor - baseCenters[i]) / reach, 1);
                var cos = Math.Cos(t * Math.PI / 2);
                scale = 1 + (ratio - 1) * cos * cos;
                scale = 1 + (scale - 1) * input.MagnifyProgress;
            }
            sizes[i] = baseIcon * scale;
        }

        // 3. Plate from the actual (magnified) slot widths, centred on screen.
        var widths = items.Select((item, i) => item.Kind == LayoutItemKind.Icon
            ? (sizes[i] + IconSpacing) * item.SlotProgress
            : SeparatorSlotWidth * item.SlotProgress).ToArray();
        var plateHeight = PlateHeight(baseIcon);
        var plateWidth = 2 * PlatePaddingH + widths.Sum();
        var plate = new RectD((input.ScreenWidth - plateWidth) / 2, input.WindowHeight - EdgeGap - plateHeight, plateWidth, plateHeight);

        // 4. Icons anchored to their bottom line, centred in their slot.
        var iconBottom = plate.Bottom - PlatePaddingV - IndicatorSpace;
        var layouts = new List<ItemLayout>(items.Count);
        x = plate.X + PlatePaddingH;
        for (var i = 0; i < items.Count; i++)
        {
            var center = x + widths[i] / 2;
            RectD bounds;
            if (items[i].Kind == LayoutItemKind.Icon)
            {
                bounds = new RectD(center - sizes[i] / 2, iconBottom - sizes[i], sizes[i], sizes[i]);
            }
            else
            {
                var h = SeparatorHeightFactor * baseIcon;
                bounds = new RectD(center - 0.5, iconBottom - baseIcon / 2 - h / 2, 1, h);
            }
            layouts.Add(new ItemLayout(items[i].Key, items[i].Kind, bounds, items[i].SlotProgress,
                bounds.CenterX, bounds.Bottom + IndicatorOffset + IndicatorDiameter / 2));
            x += widths[i];
        }

        // 5. Active zone: covers magnified icons while the pointer is involved, so gaps between them don't flicker.
        var magnifying = input.CursorX is not null || input.MagnifyProgress > 0;
        var top = magnifying && layouts.Count > 0 ? Math.Min(plate.Y, layouts.Min(l => l.Bounds.Y)) : plate.Y;
        var activeZone = new RectD(plate.X, top, plate.Width, input.WindowHeight - top);

        return new LayoutResult(plate, activeZone, layouts, baseIcon);
    }

    private static double SlotWidth(LayoutItem item, double icon) =>
        (item.Kind == LayoutItemKind.Icon ? icon + IconSpacing : SeparatorSlotWidth) * item.SlotProgress;

    /// Shrinks icons uniformly when the unmagnified dock would not fit the screen.
    private static double FitIconSize(LayoutInput input)
    {
        var available = input.ScreenWidth - 2 * ScreenMargin;
        var needed = 2 * PlatePaddingH + input.Items.Sum(i => SlotWidth(i, input.IconSize));
        if (needed <= available) return input.IconSize;

        var iconWeight = input.Items.Where(i => i.Kind == LayoutItemKind.Icon).Sum(i => i.SlotProgress);
        if (iconWeight <= 0) return input.IconSize;
        var separators = input.Items.Where(i => i.Kind == LayoutItemKind.Separator).Sum(i => SeparatorSlotWidth * i.SlotProgress);
        var fitted = (available - 2 * PlatePaddingH - separators - iconWeight * IconSpacing) / iconWeight;
        return Math.Max(1, fitted);
    }
}
