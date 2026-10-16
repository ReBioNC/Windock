namespace Windock.Core.Layout;

public readonly record struct RectD(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public double CenterX => X + Width / 2;
}

public enum LayoutItemKind { Icon, Separator }

public readonly record struct LayoutItem(string Key, LayoutItemKind Kind, double SlotProgress);

public sealed record LayoutInput(
    IReadOnlyList<LayoutItem> Items,
    double IconSize,
    double MagnifiedSize,
    bool MagnificationEnabled,
    double? CursorX,
    double MagnifyProgress,
    double ScreenWidth,
    double WindowHeight);

public readonly record struct ItemLayout(
    string Key,
    LayoutItemKind Kind,
    RectD Bounds,
    double SlotProgress,
    double IndicatorCenterX,
    double IndicatorCenterY);

public sealed record LayoutResult(RectD Plate, RectD ActiveZone, IReadOnlyList<ItemLayout> Items, double EffectiveIconSize);
