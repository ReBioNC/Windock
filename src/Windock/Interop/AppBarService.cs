using Windock.Core.Logging;

namespace Windock.Interop;

/// Reserves the strip the dock sits on, like the taskbar does, so maximized windows stop above it,
/// and reports fullscreen apps (ARCHITECTURE §7.1). Uses its own hidden window, not the dock window.
internal sealed class AppBarService : IDisposable
{
    private readonly ILog _log;
    private readonly MessageWindow _host = new("WindockAppBarHost");
    private readonly uint _callbackMessage = Native.RegisterWindowMessage("Windock.AppBar");
    private bool _registered;
    private int _thicknessPx;
    private Native.RECT _last;

    public AppBarService(ILog log)
    {
        _log = log;
        _host.Message += OnMessage;
    }

    public event Action<bool>? FullscreenChanged;

    /// Bottom edge (physical px) of the reserved strip; the dock sits on it (above a visible taskbar, for example).
    public event Action<int>? Reserved;

    public void Reserve(double thicknessDip, double dpiScale)
    {
        _thicknessPx = (int)Math.Round(thicknessDip * dpiScale);
        var data = Native.APPBARDATA.Create();
        data.hWnd = _host.Handle;
        if (!_registered)
        {
            data.uCallbackMessage = _callbackMessage;
            _registered = Native.SHAppBarMessage(Native.ABM_NEW, ref data) != UIntPtr.Zero;
            _last = default;
            if (!_registered)
            {
                _log.Error("ABM_NEW failed; screen space not reserved");
                return;
            }
        }
        Apply();
    }

    public void Release()
    {
        if (!_registered) return;
        var data = Native.APPBARDATA.Create();
        data.hWnd = _host.Handle;
        Native.SHAppBarMessage(Native.ABM_REMOVE, ref data);
        _registered = false;
        _last = default;
        Reserved?.Invoke(Native.GetSystemMetrics(Native.SM_CYSCREEN));
    }

    private void Apply()
    {
        int width = Native.GetSystemMetrics(Native.SM_CXSCREEN), height = Native.GetSystemMetrics(Native.SM_CYSCREEN);
        var data = Native.APPBARDATA.Create();
        data.hWnd = _host.Handle;
        data.uEdge = Native.ABE_BOTTOM;
        data.rc = new Native.RECT { Left = 0, Right = width, Top = height - _thicknessPx, Bottom = height };
        Native.SHAppBarMessage(Native.ABM_QUERYPOS, ref data);
        data.rc.Top = data.rc.Bottom - _thicknessPx;
        // Only SETPOS when something changed: every SETPOS triggers ABN_POSCHANGED (spike S2).
        if (data.rc.Equals(_last)) return;
        _last = data.rc;
        Native.SHAppBarMessage(Native.ABM_SETPOS, ref data);
        _log.Info($"reserved {data.rc}");
        Reserved?.Invoke(data.rc.Bottom);
    }

    private void OnMessage(int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != _callbackMessage) return;
        handled = true;
        switch (wParam.ToInt32())
        {
            case Native.ABN_POSCHANGED:
                if (_registered) Apply();
                break;
            case Native.ABN_FULLSCREENAPP:
                FullscreenChanged?.Invoke(lParam != IntPtr.Zero);
                break;
        }
    }

    public void Dispose()
    {
        Release();
        _host.Dispose();
    }
}
