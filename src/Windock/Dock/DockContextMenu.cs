using System.IO;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Windock.Core.Abstractions;
using Windock.Core.Apps;
using Windock.Core.Settings;
using Windock.Interop;

namespace Windock.Dock;

/// Right-click menus for dock items and the empty plate (DESIGN.md §5).
internal sealed class DockContextMenu(AppLauncher launcher, SettingsStore settings, IPinResolver resolver, Action openSettings, Action exit,
    Action emptyRecycleBin)
{
    private const int MaxWindows = 10;

    public ContextMenu Build(DockItem? item)
    {
        var menu = new ContextMenu { Placement = PlacementMode.MousePoint };
        if (item is null || item.Kind == DockItemKind.Separator)
        {
            menu.Items.Add(Item("Dock settings…", openSettings));
            menu.Items.Add(Item("Quit", exit));
            return menu;
        }
        if (item.Kind == DockItemKind.RecycleBin)
        {
            menu.Items.Add(Item("Open", () => launcher.Execute(ClickAction.OpenRecycleBin.Instance, item)));
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("Empty Recycle Bin…", emptyRecycleBin, enabled: !RecycleBin.IsEmpty()));
            return menu;
        }

        menu.Items.Add(new MenuItem { Header = item.DisplayName, IsEnabled = false });
        menu.Items.Add(new Separator());
        if (item.Windows.Count > 0)
        {
            foreach (var w in item.Windows.Take(MaxWindows))
            {
                var hwnd = w.Handle;
                menu.Items.Add(Item(Shorten(string.IsNullOrWhiteSpace(w.Title) ? item.DisplayName : w.Title), () => launcher.Activate(hwnd)));
            }
            menu.Items.Add(new Separator());
        }

        var target = item.LaunchTarget is { } t ? AppLauncher.LaunchTargetFor(item, t) : null;
        menu.Items.Add(Item("New window", () => launcher.Launch(target!, item.DisplayName), enabled: target is not null));
        menu.Items.Add(item.IsPinned
            ? Item("Remove from dock", () => Unpin(item))
            : Item("Keep in dock", () => Pin(item), enabled: PinPathFor(item) is not null));

        if (!item.IsPinned && item.AppId is { } hideId)
            menu.Items.Add(Item("Don't show in dock", () => Hide(hideId.Value, item.DisplayName)));

        var file = item.Windows.FirstOrDefault()?.ExePath ?? item.LaunchTarget;
        menu.Items.Add(Item("Show in Explorer", () => launcher.RevealInExplorer(file!),
            enabled: file is not null && File.Exists(file)));

        if (item.IsRunning)
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("Close all windows", () => launcher.CloseWindows(item.Windows.Select(w => w.Handle).ToList())));
        }
        return menu;
    }

    /// What to store in settings when pinning a running app.
    public static string? PinPathFor(DockItem item)
    {
        var id = item.AppId?.Value;
        if (id is not null && id.StartsWith("aumid:", StringComparison.Ordinal)) return AppLauncher.AppsFolderPrefix + id["aumid:".Length..];
        return item.Windows.FirstOrDefault()?.ExePath ?? item.LaunchTarget;
    }

    private void Pin(DockItem item)
    {
        if (PinPathFor(item) is not { } path) return;
        settings.Update(s => s.Pins.Any(p => string.Equals(p.Path, path, StringComparison.OrdinalIgnoreCase))
            ? s
            : s with { Pins = s.Pins.Append(new PinEntry(path)).ToList() });
    }

    private void Hide(string appId, string name) =>
        settings.Update(s => s.HiddenApps.Any(h => string.Equals(h.AppId, appId, StringComparison.OrdinalIgnoreCase))
            ? s
            : s with { HiddenApps = s.HiddenApps.Append(new HiddenApp(appId, name)).ToList() });

    private void Unpin(DockItem item) =>
        settings.Update(s => s with { Pins = s.Pins.Where(p => resolver.Resolve(p).AppId != item.AppId).ToList() });

    private static MenuItem Item(string header, Action onClick, bool enabled = true)
    {
        var mi = new MenuItem { Header = header, IsEnabled = enabled };
        mi.Click += (_, _) => onClick();
        return mi;
    }

    private static string Shorten(string text) => text.Length <= 60 ? text : text[..57] + "…";
}
