using Windock.Core.Abstractions;

namespace Windock.Core.Tests.Fakes;

internal sealed class FakeWindowSource : IWindowSource
{
    private readonly List<WindowInfo> _windows = new();

    public nint Foreground { get; private set; }

    public event EventHandler<WindowInfo>? WindowAdded;
    public event EventHandler<nint>? WindowRemoved;
    public event EventHandler<WindowInfo>? WindowChanged;
    public event EventHandler<nint>? ForegroundChanged;

    public IReadOnlyList<WindowInfo> Snapshot() => _windows.ToList();

    public void Seed(params WindowInfo[] windows) => _windows.AddRange(windows);

    public void Add(WindowInfo w) { _windows.Add(w); WindowAdded?.Invoke(this, w); }

    public void Remove(nint handle) { _windows.RemoveAll(w => w.Handle == handle); WindowRemoved?.Invoke(this, handle); }

    public void Change(WindowInfo w)
    {
        _windows[_windows.FindIndex(x => x.Handle == w.Handle)] = w;
        WindowChanged?.Invoke(this, w);
    }

    public void SetForeground(nint handle) { Foreground = handle; ForegroundChanged?.Invoke(this, handle); }
}
