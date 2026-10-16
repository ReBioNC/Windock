using System.IO;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Windock.Core.Abstractions;
using Windock.Core.Apps;
using Windock.Core.Settings;
using Windock.Interop;

namespace Windock.Dock;

/// Right-click menus for dock items and the empty plate (DESIGN.md §5).
internal sealed class DockContextMenu(AppLauncher launcher, SettingsStore settings, IPinResolver resolver, Action openSettings, Action exit)
{
    private const int MaxWindows = 10;

    public ContextMenu Build(DockItem? item)
    {
        var menu = new ContextMenu { Placement = PlacementMode.MousePoint };
        if (item is null || item.Kind == DockItemKind.Separator)
        {
            menu.Items.Add(Item("Pengaturan Dock…", openSettings));
            menu.Items.Add(Item("Keluar", exit));
            return menu;
        }
        if (item.Kind == DockItemKind.RecycleBin)
        {
            menu.Items.Add(Item("Buka", () => launcher.Execute(ClickAction.OpenRecycleBin.Instance, item)));
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
        menu.Items.Add(Item("Buka jendela baru", () => launcher.Launch(target!, item.DisplayName), enabled: target is not null));
        menu.Items.Add(item.IsPinned
            ? Item("Hapus dari Dock", () => Unpin(item))
            : Item("Simpan di Dock", () => Pin(item), enabled: PinPathFor(item) is not null));

        var file = item.Windows.FirstOrDefault()?.ExePath ?? item.LaunchTarget;
        menu.Items.Add(Item("Tampilkan di Explorer", () => launcher.RevealInExplorer(file!),
            enabled: file is not null && File.Exists(file)));

        if (item.IsRunning)
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("Tutup semua jendela", () => launcher.CloseWindows(item.Windows.Select(w => w.Handle).ToList())));
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
