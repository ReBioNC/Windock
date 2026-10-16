using Windock.Core.Abstractions;
using Windock.Core.Settings;

namespace Windock.Core.Apps;

/// Merges pinned apps and running windows into the ordered list of dock items (ARCHITECTURE §6.3).
public sealed class DockModel
{
    private static readonly IReadOnlyList<WindowInfo> NoWindows = Array.Empty<WindowInfo>();

    private readonly IWindowSource _windows;
    private readonly IPinResolver _resolver;
    private readonly Dictionary<AppId, List<WindowInfo>> _running = new();
    private readonly List<AppId> _firstSeen = new();
    private IReadOnlyList<PinResolution> _pins = Array.Empty<PinResolution>();

    public DockModel(IWindowSource windows, IPinResolver resolver)
    {
        _windows = windows;
        _resolver = resolver;
        foreach (var w in windows.Snapshot()) Track(w);
        Items = Build();

        windows.WindowAdded += (_, w) => Mutate(() => Track(w));
        windows.WindowRemoved += (_, h) => Mutate(() => Untrack(h));
        windows.WindowChanged += (_, w) => Mutate(() => Replace(w));
        windows.ForegroundChanged += (_, h) => Mutate(() => BringToFront(h));
    }

    public IReadOnlyList<DockItem> Items { get; private set; }

    public event EventHandler<DockItemsChangedEventArgs>? ItemsChanged;

    public DockItem? Find(string key) => Items.FirstOrDefault(i => i.Key == key);

    public void SetPins(IReadOnlyList<PinEntry> pins) => Mutate(() =>
    {
        var seen = new HashSet<AppId>();
        _pins = pins.Select(_resolver.Resolve).Where(r => seen.Add(r.AppId)).ToList();
    });

    private void Track(WindowInfo w)
    {
        Untrack(w.Handle);
        if (!_running.TryGetValue(w.AppId, out var list))
        {
            _running[w.AppId] = list = new List<WindowInfo>();
            _firstSeen.Add(w.AppId);
        }
        list.Add(w);
    }

    private void Untrack(nint handle)
    {
        foreach (var (id, list) in _running)
        {
            if (list.RemoveAll(x => x.Handle == handle) == 0) continue;
            if (list.Count == 0)
            {
                _running.Remove(id);
                _firstSeen.Remove(id);
            }
            return;
        }
    }

    private void Replace(WindowInfo w)
    {
        foreach (var list in _running.Values)
        {
            var i = list.FindIndex(x => x.Handle == w.Handle);
            if (i < 0) continue;
            if (list[i].AppId == w.AppId) list[i] = w;
            else Track(w);
            return;
        }
    }

    private void BringToFront(nint handle)
    {
        foreach (var list in _running.Values)
        {
            var i = list.FindIndex(x => x.Handle == handle);
            if (i <= 0) continue;
            var w = list[i];
            list.RemoveAt(i);
            list.Insert(0, w);
            return;
        }
    }

    private void Mutate(Action change)
    {
        change();
        var old = Items;
        Items = Build();
        var args = Diff(old, Items);
        if (args.Added.Count + args.Removed.Count + args.Updated.Count > 0) ItemsChanged?.Invoke(this, args);
    }

    private IReadOnlyList<DockItem> Build()
    {
        var foreground = _windows.Foreground;
        IReadOnlyList<WindowInfo> WindowsOf(AppId id) => _running.TryGetValue(id, out var l) ? l.ToList() : NoWindows;
        bool Active(IReadOnlyList<WindowInfo> ws) => foreground != 0 && ws.Any(w => w.Handle == foreground);

        var items = new List<DockItem>();
        foreach (var pin in _pins)
        {
            var ws = WindowsOf(pin.AppId);
            items.Add(new DockItem(pin.AppId.Value, DockItemKind.App, pin.AppId, pin.DisplayName, pin.LaunchTarget, pin.IconPath, true, ws, Active(ws)));
        }

        var pinned = _pins.Select(p => p.AppId).ToHashSet();
        var unpinned = _firstSeen.Where(id => !pinned.Contains(id)).ToList();
        if (unpinned.Count > 0)
        {
            if (_pins.Count > 0) items.Add(Fixed(DockItem.PinnedSeparatorKey, DockItemKind.Separator, ""));
            foreach (var id in unpinned)
            {
                var ws = WindowsOf(id);
                var first = ws[0];
                items.Add(new DockItem(id.Value, DockItemKind.App, id, first.DisplayName, first.ExePath, first.ExePath, false, ws, Active(ws)));
            }
        }

        items.Add(Fixed(DockItem.TrashSeparatorKey, DockItemKind.Separator, ""));
        items.Add(Fixed(DockItem.RecycleBinKey, DockItemKind.RecycleBin, "Recycle Bin"));
        return items;
    }

    private static DockItem Fixed(string key, DockItemKind kind, string name) =>
        new(key, kind, null, name, null, null, false, NoWindows, false);

    private static DockItemsChangedEventArgs Diff(IReadOnlyList<DockItem> old, IReadOnlyList<DockItem> now)
    {
        var oldByKey = old.ToDictionary(i => i.Key);
        var newByKey = now.ToDictionary(i => i.Key);
        var added = now.Where(i => !oldByKey.ContainsKey(i.Key)).Select(i => i.Key).ToList();
        var removed = old.Where(i => !newByKey.ContainsKey(i.Key)).Select(i => i.Key).ToList();

        // Common keys whose content changed or whose relative order changed.
        var oldCommon = old.Where(i => newByKey.ContainsKey(i.Key)).Select(i => i.Key).ToList();
        var newCommon = now.Where(i => oldByKey.ContainsKey(i.Key)).Select(i => i.Key).ToList();
        var updated = newCommon
            .Where((key, index) => oldByKey[key] != newByKey[key] || oldCommon[index] != key)
            .ToList();

        return new DockItemsChangedEventArgs(added, removed, updated);
    }
}
