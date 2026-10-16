using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace S2.Taskbar;

/// Spike: hide the Windows taskbar (autohide + SW_HIDE), reserve 70 px with a hidden AppBar window,
/// log AppBar notifications and taskbar visibility, and restore everything (also via --restore).
public partial class App : Application
{
    private static readonly string LogPath = Path.Combine(Path.GetTempPath(), "s2-log.txt");
    private static readonly string StatePath = Path.Combine(Path.GetTempPath(), "s2-state.txt");
    private HwndSource? _host;
    private uint _callbackMsg, _taskbarCreatedMsg;
    private bool _registered, _hideActive;
    private Native.RECT _lastRc;

    private static void Log(string s) => File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff} {s}{Environment.NewLine}");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var args = e.Args;
        if (args.Contains("--restore")) { Restore(); Shutdown(); return; }
        if (args.Contains("--fullscreen")) { ShowFullscreen(); return; }
        var seconds = args.Select(a => a.StartsWith("--scenario=") ? int.Parse(a[11..]) : 0).Max();
        File.WriteAllText(LogPath, "");
        RunScenario(seconds);
    }

    private void RunScenario(int seconds)
    {
        Log($"start workArea={Native.WorkArea()}");
        _taskbarCreatedMsg = Native.RegisterWindowMessage("TaskbarCreated");
        _callbackMsg = Native.RegisterWindowMessage("WindockSpike.AppBar");
        _host = new HwndSource(new HwndSourceParameters("S2AppBarHost")
        {
            WindowStyle = unchecked((int)0x80000000), // WS_POPUP, never shown
            ExtendedWindowStyle = 0x80,                // WS_EX_TOOLWINDOW
            Width = 1, Height = 1,
        });
        _host.AddHook(WndProc);

        // 1. Remember original state BEFORE touching anything.
        var data = Native.NewData();
        var original = (int)Native.SHAppBarMessage(Native.ABM_GETSTATE, ref data);
        File.WriteAllText(StatePath, original.ToString());
        Log($"original ABM_GETSTATE={original}");

        // 2. Reserve 70 px at the bottom.
        Reserve();

        // 3. Autohide + hide taskbar windows.
        var set = Native.NewData();
        set.lParam = new IntPtr(original | Native.ABS_AUTOHIDE);
        Native.SHAppBarMessage(Native.ABM_SETSTATE, ref set);
        // Autohide makes the shell re-show/slide the taskbar; hide after the transition settles.
        new DispatcherTimer(TimeSpan.FromMilliseconds(700), DispatcherPriority.Normal, (t, _) =>
        {
            ((DispatcherTimer)t!).Stop();
            foreach (var h in Native.TaskbarWindows()) Log($"hide 0x{h:X} -> {Native.ShowWindow(h, 0)}");
            Log($"hidden workArea={Native.WorkArea()}");
            _hideActive = true;
        }, Dispatcher).Start();

        // Watch taskbar visibility (spike-only polling).
        var last = false;
        new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Normal, (_, _) =>
        {
            var vis = Native.IsWindowVisible(Native.FindWindow("Shell_TrayWnd", null));
            if (vis != last) { Log($"Shell_TrayWnd visible={vis} fg={Native.ForegroundClass()} workArea={Native.WorkArea()}"); last = vis; }
            // Re-hide once Start/Search/taskbar itself is no longer in the foreground.
            if (vis && _hideActive && Native.ForegroundClass() is not ("Windows.UI.Core.CoreWindow" or "Shell_TrayWnd"))
            {
                foreach (var h in Native.TaskbarWindows()) Native.ShowWindow(h, 0);
                Log($"re-hidden (fg={Native.ForegroundClass()})");
            }
        }, Dispatcher).Start();

        new DispatcherTimer(TimeSpan.FromSeconds(seconds), DispatcherPriority.Normal, (_, _) => { Restore(); Shutdown(); }, Dispatcher).Start();
    }

    private void Reserve()
    {
        var d = Native.NewData();
        d.hWnd = _host!.Handle;
        if (!_registered)
        {
            d.uCallbackMessage = _callbackMsg;
            Log($"ABM_NEW={Native.SHAppBarMessage(Native.ABM_NEW, ref d)}");
            _registered = true;
        }
        var w = Native.GetSystemMetrics(0); var hgt = Native.GetSystemMetrics(1);
        d.uEdge = Native.ABE_BOTTOM;
        d.rc = new Native.RECT { Left = 0, Right = w, Top = hgt - 70, Bottom = hgt };
        Native.SHAppBarMessage(Native.ABM_QUERYPOS, ref d);
        d.rc.Top = d.rc.Bottom - 70;
        if (d.rc.Equals(_lastRc)) return;
        _lastRc = d.rc;
        Native.SHAppBarMessage(Native.ABM_SETPOS, ref d);
        Log($"reserved rc={d.rc} workArea={Native.WorkArea()}");
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == _callbackMsg)
        {
            var code = wParam.ToInt32();
            Log(code switch
            {
                Native.ABN_FULLSCREENAPP => $"ABN_FULLSCREENAPP lParam={lParam}",
                Native.ABN_POSCHANGED => "ABN_POSCHANGED",
                _ => $"appbar notify {code}",
            });
            if (code == Native.ABN_POSCHANGED) Reserve();
        }
        else if (msg == _taskbarCreatedMsg)
        {
            Log("TaskbarCreated received");
        }
        return IntPtr.Zero;
    }

    private void Restore()
    {
        _hideActive = false;
        foreach (var h in Native.TaskbarWindows()) Native.ShowWindow(h, 5);
        if (File.Exists(StatePath))
        {
            var d = Native.NewData();
            d.lParam = new IntPtr(int.Parse(File.ReadAllText(StatePath)));
            Native.SHAppBarMessage(Native.ABM_SETSTATE, ref d);
            File.Delete(StatePath);
        }
        if (_registered && _host is not null)
        {
            var d = Native.NewData();
            d.hWnd = _host.Handle;
            Native.SHAppBarMessage(Native.ABM_REMOVE, ref d);
            _registered = false;
        }
        var check = Native.NewData();
        Log($"restored ABM_GETSTATE={(int)Native.SHAppBarMessage(Native.ABM_GETSTATE, ref check)} workArea={Native.WorkArea()}");
    }

    private void ShowFullscreen()
    {
        var w = new Window
        {
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, Topmost = true,
            Left = 0, Top = 0, Width = SystemParameters.PrimaryScreenWidth, Height = SystemParameters.PrimaryScreenHeight,
            Background = Brushes.Black,
        };
        w.Show();
        w.Activate();
        new DispatcherTimer(TimeSpan.FromSeconds(3), DispatcherPriority.Normal, (_, _) => Shutdown(), Dispatcher).Start();
    }
}
