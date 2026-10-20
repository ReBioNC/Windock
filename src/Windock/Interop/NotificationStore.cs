using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;
using Windock.Core.Apps;
using Windock.Core.Logging;

namespace Windock.Interop;

/// Reads Windows' own notification store (%LocalAppData%\Microsoft\Windows\Notifications\wpndatabase.db),
/// read-only, with the SQLite that ships with Windows (winsqlite3.dll). Only which app has a badge and when it
/// last showed a toast is read — never the notification text.
internal static class NotificationStore
{
    public static readonly string Folder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Windows\Notifications");

    private const string Query =
        "select h.PrimaryId, n.Type, n.Payload, n.ArrivalTime from Notification n " +
        "join NotificationHandler h on h.RecordId = n.HandlerId where n.Type in ('badge', 'toast')";

    /// Per app (AUMID in lower case): badge value and newest toast time. Null when the store cannot be read.
    public static Dictionary<string, AppNotifications>? Read(ILog log)
    {
        var path = Path.Combine(Folder, "wpndatabase.db");
        if (!File.Exists(path)) return null;
        IntPtr db = IntPtr.Zero, stmt = IntPtr.Zero;
        try
        {
            if (sqlite3_open_v2(Utf8(path), out db, SQLITE_OPEN_READONLY, IntPtr.Zero) != SQLITE_OK) return null;
            sqlite3_busy_timeout(db, 500);
            if (sqlite3_prepare_v2(db, Utf8(Query), -1, out stmt, IntPtr.Zero) != SQLITE_OK) return null;
            var result = new Dictionary<string, AppNotifications>(StringComparer.Ordinal);
            int rc;
            while ((rc = sqlite3_step(stmt)) == SQLITE_ROW)
            {
                var app = Text(stmt, 0)?.ToLowerInvariant();
                var type = Text(stmt, 1);
                if (app is null) continue;
                var current = result.GetValueOrDefault(app);
                if (type == "badge") current = current with { Badge = NotificationChanges.ParseBadge(Blob(stmt, 2)) };
                else current = current with { LastToast = Math.Max(current.LastToast, sqlite3_column_int64(stmt, 3)) };
                result[app] = current;
            }
            return rc == SQLITE_DONE ? result : null;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            log.Error("notification store unavailable", ex);
            return null;
        }
        finally
        {
            if (stmt != IntPtr.Zero) sqlite3_finalize(stmt);
            if (db != IntPtr.Zero) sqlite3_close_v2(db);
        }
    }

    private static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s + "\0");

    private static string? Text(IntPtr stmt, int column)
    {
        var p = sqlite3_column_text(stmt, column);
        return p == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(p);
    }

    private static string? Blob(IntPtr stmt, int column)
    {
        var p = sqlite3_column_blob(stmt, column);
        var n = sqlite3_column_bytes(stmt, column);
        if (p == IntPtr.Zero || n <= 0) return null;
        var bytes = new byte[n];
        Marshal.Copy(p, bytes, 0, n);
        return Encoding.UTF8.GetString(bytes);
    }

    private const int SQLITE_OK = 0, SQLITE_ROW = 100, SQLITE_DONE = 101, SQLITE_OPEN_READONLY = 0x1;

    [DllImport("winsqlite3.dll")] private static extern int sqlite3_open_v2(byte[] filename, out IntPtr db, int flags, IntPtr vfs);
    [DllImport("winsqlite3.dll")] private static extern int sqlite3_busy_timeout(IntPtr db, int ms);
    [DllImport("winsqlite3.dll")] private static extern int sqlite3_prepare_v2(IntPtr db, byte[] sql, int bytes, out IntPtr stmt, IntPtr tail);
    [DllImport("winsqlite3.dll")] private static extern int sqlite3_step(IntPtr stmt);
    [DllImport("winsqlite3.dll")] private static extern IntPtr sqlite3_column_text(IntPtr stmt, int column);
    [DllImport("winsqlite3.dll")] private static extern IntPtr sqlite3_column_blob(IntPtr stmt, int column);
    [DllImport("winsqlite3.dll")] private static extern int sqlite3_column_bytes(IntPtr stmt, int column);
    [DllImport("winsqlite3.dll")] private static extern long sqlite3_column_int64(IntPtr stmt, int column);
    [DllImport("winsqlite3.dll")] private static extern int sqlite3_finalize(IntPtr stmt);
    [DllImport("winsqlite3.dll")] private static extern int sqlite3_close_v2(IntPtr db);
}

/// Watches the notification store and reports which apps got new notifications (or read them all).
/// Event-driven: a FileSystemWatcher on the store's folder, debounced; the read runs off the UI thread.
internal sealed class NotificationWatcher : IDisposable
{
    private readonly ILog _log;
    private readonly Dispatcher _ui;
    private readonly FileSystemWatcher? _watcher;
    private readonly DispatcherTimer _debounce;
    private Dictionary<string, AppNotifications>? _last;
    private bool _reading;

    public NotificationWatcher(Dispatcher ui, ILog log)
    {
        _ui = ui;
        _log = log;
        _debounce = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromMilliseconds(700) };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            ReadAsync();
        };
        try
        {
            _watcher = new FileSystemWatcher(NotificationStore.Folder, "wpndatabase.db*")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size, InternalBufferSize = 4096,
            };
            _watcher.Changed += (_, _) => _ui.BeginInvoke(Poke);
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            _log.Error("notification store not watched", ex);
        }
        ReadAsync();
    }

    /// App AUMIDs (lower case) that got a new toast or a higher badge.
    public event Action<IReadOnlyList<string>>? Raised;

    /// App AUMIDs whose badge went back to zero (everything read).
    public event Action<IReadOnlyList<string>>? Cleared;

    /// Current unread counts (badges) per app, raised when any of them changes.
    public event Action<IReadOnlyDictionary<string, int>>? BadgesChanged;

    private void Poke()
    {
        _debounce.Stop();
        _debounce.Start();
    }

    private async void ReadAsync()
    {
        if (_reading)
        {
            Poke();  // read again once the current read is done
            return;
        }
        _reading = true;
        try
        {
            var now = await Task.Run(() => NotificationStore.Read(_log));
            if (now is null) return;
            var (raise, clear) = NotificationChanges.Compare(_last, now);
            var badgesChanged = _last is null || now.Count != _last.Count || now.Any(p => _last.GetValueOrDefault(p.Key).Badge != p.Value.Badge);
            _last = now;
            if (badgesChanged) BadgesChanged?.Invoke(now.ToDictionary(p => p.Key, p => p.Value.Badge));
            if (raise.Count > 0) Raised?.Invoke(raise);
            if (clear.Count > 0) Cleared?.Invoke(clear);
        }
        finally
        {
            _reading = false;
        }
    }

    public void Dispose()
    {
        _debounce.Stop();
        _watcher?.Dispose();
    }
}
