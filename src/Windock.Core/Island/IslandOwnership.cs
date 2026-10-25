using Windock.Core.Settings;

namespace Windock.Core.Island;

/// Clock, date and battery appear in one place only: the Dynamic Island (when it is on and owns the item)
/// or the dock's status area.
public static class IslandOwnership
{
    public static bool IslandShowsClock(DockSettings s) => s.Island.Enabled && s.Island.ShowClock;
    public static bool IslandShowsDate(DockSettings s) => s.Island.Enabled && s.Island.ShowDate;
    public static bool IslandShowsBattery(DockSettings s) => s.Island.Enabled && s.Island.ShowBattery;

    public static bool DockShowsClock(DockSettings s) => s.Status.ShowClock && !IslandShowsClock(s);
    public static bool DockShowsDate(DockSettings s) => s.Status.ShowDate && !IslandShowsDate(s);
    public static bool DockShowsBattery(DockSettings s) => s.Status.ShowBattery && !IslandShowsBattery(s);
}
