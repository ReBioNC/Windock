using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace Windock.Interop;

/// The Windows Recycle Bin: state, sending files to it (always undoable) and emptying it with Windows' own confirmation.
internal static class RecycleBin
{
    public const int StockEmpty = 31;  // SIID_RECYCLER
    public const int StockFull = 32;   // SIID_RECYCLERFULL

    /// This user's bin folder on each fixed drive (X:\$Recycle.Bin\<SID>), existing or not.
    public static IReadOnlyList<string> UserFolders()
    {
        var sid = WindowsIdentity.GetCurrent().User?.Value;
        if (sid is null) return Array.Empty<string>();
        return DriveInfo.GetDrives()
            .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
            .Select(d => Path.Combine(d.RootDirectory.FullName, "$Recycle.Bin", sid))
            .ToList();
    }

    /// Looks for the first recycled item only; SHQueryRecycleBin counts every item and takes ~0.5 s on a big bin.
    public static bool IsEmpty()
    {
        foreach (var folder in UserFolders())
        {
            try
            {
                if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder, "$R*").Any()) return false;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return true;
    }

    /// Moves files to the Recycle Bin. Windows asks first if an item cannot be recycled (it would be destroyed),
    /// so nothing is ever deleted permanently without the user saying so. Returns false when cancelled or failed.
    public static bool Send(IReadOnlyList<string> paths, IntPtr owner)
    {
        if (paths.Count == 0) return false;
        var op = new SHFILEOPSTRUCT
        {
            hwnd = owner,
            wFunc = FO_DELETE,
            pFrom = string.Join("\0", paths) + "\0\0",
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_WANTNUKEWARNING,
        };
        return SHFileOperation(ref op) == 0 && !op.fAnyOperationsAborted;
    }

    /// Empties the Recycle Bin; Windows shows its own confirmation and may be cancelled.
    public static void Empty(IntPtr owner) => SHEmptyRecycleBin(owner, null, 0);

    private const uint FO_DELETE = 3;
    private const ushort FOF_NOCONFIRMATION = 0x0010, FOF_ALLOWUNDO = 0x0040, FOF_WANTNUKEWARNING = 0x4000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHFileOperation(ref SHFILEOPSTRUCT op);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? rootPath, uint flags);
}

/// Raises Changed (debounced, on the UI thread) when items enter or leave this user's Recycle Bin folders.
internal sealed class RecycleBinWatcher : IDisposable
{
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly System.Windows.Threading.DispatcherTimer _debounce;

    public RecycleBinWatcher()
    {
        _debounce = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _debounce.Tick += (_, _) => { _debounce.Stop(); Changed?.Invoke(); };
        foreach (var folder in RecycleBin.UserFolders().Where(Directory.Exists))
        {
            try
            {
                var w = new FileSystemWatcher(folder) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName, InternalBufferSize = 4096 };
                w.Created += OnFileEvent;
                w.Deleted += OnFileEvent;
                w.Renamed += OnFileEvent;
                w.Error += OnFileEvent;
                w.EnableRaisingEvents = true;
                _watchers.Add(w);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
        }
    }

    public event Action? Changed;

    /// Re-check soon (after our own operations, or when a bin folder did not exist yet).
    public void Poke() => _debounce.Dispatcher.BeginInvoke(() => { _debounce.Stop(); _debounce.Start(); });

    private void OnFileEvent(object sender, EventArgs e) => Poke();

    public void Dispose()
    {
        _debounce.Stop();
        foreach (var w in _watchers) w.Dispose();
        _watchers.Clear();
    }
}
