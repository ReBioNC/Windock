using System.IO;
using System.Runtime.InteropServices;
using Windock.Core.Launcher;

namespace Windock.Interop;

/// What the launcher can find: every app in Start (shell:AppsFolder, mostly Store apps and registered
/// programs), every Start Menu shortcut (the AppsFolder list misses some), and optionally the files directly
/// on the Desktop and in Documents.
/// Built when the launcher opens and dropped with it.
internal static class LauncherIndex
{
    private const int MaxFiles = 3000;
    private static readonly Guid BHID_EnumItems = new("94F60519-2850-4924-AA5A-D15E84868039");
    private static readonly Guid IID_IEnumShellItems = new("70629033-E363-4A28-A567-0DB78006E6D7");
    private const uint SIGDN_NORMALDISPLAY = 0, SIGDN_PARENTRELATIVEPARSING = 0x80018001;

    /// Runs on its own STA thread (the shell folder enumeration expects one).
    public static Task<IReadOnlyList<LauncherEntry>> BuildAsync(bool includeFiles, Action<string> log)
    {
        var done = new TaskCompletionSource<IReadOnlyList<LauncherEntry>>();
        var thread = new Thread(() =>
        {
            try
            {
                var list = new List<LauncherEntry>();
                AddApps(list);
                log($"launcher index: {list.Count} apps in shell:AppsFolder");
                AddStartMenuShortcuts(list, log);
                if (includeFiles) AddFiles(list);
                done.SetResult(list);
            }
            catch (Exception ex)
            {
                done.SetException(ex);
            }
        }) { IsBackground = true, Name = "Windock launcher index" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task;
    }

    private static void AddApps(List<LauncherEntry> list)
    {
        if (SHCreateItemFromParsingName("shell:AppsFolder", IntPtr.Zero, typeof(ShellInterop.IShellItem).GUID, out var folder) != 0) return;
        try
        {
            folder.BindToHandler(IntPtr.Zero, BHID_EnumItems, IID_IEnumShellItems, out var enumPtr);
            var items = (IEnumShellItems)Marshal.GetObjectForIUnknown(enumPtr);
            Marshal.Release(enumPtr);
            try
            {
                while (items.Next(1, out var item, out var fetched) == 0 && fetched == 1)
                {
                    try
                    {
                        var name = Name(item, SIGDN_NORMALDISPLAY);
                        var parsing = Name(item, SIGDN_PARENTRELATIVEPARSING);
                        if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(parsing))
                            list.Add(new LauncherEntry(name, AppLauncher.AppsFolderPrefix + parsing, LauncherEntryKind.App));
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(item);
                    }
                }
            }
            finally
            {
                Marshal.ReleaseComObject(items);
            }
        }
        catch (COMException)
        {
            // Partial list is still useful.
        }
        finally
        {
            Marshal.ReleaseComObject(folder);
        }
    }

    private static readonly EnumerationOptions Recursive = new()
    {
        RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = 0, MatchCasing = MatchCasing.CaseInsensitive,
    };

    private static void AddStartMenuShortcuts(List<LauncherEntry> list, Action<string> log)
    {
        foreach (var root in new[] { Environment.SpecialFolder.Programs, Environment.SpecialFolder.CommonPrograms })
        {
            var folder = Environment.GetFolderPath(root);
            var before = list.Count;
            try
            {
                foreach (var path in Directory.EnumerateFiles(folder, "*.lnk", Recursive))
                    list.Add(new LauncherEntry(Path.GetFileNameWithoutExtension(path), path, LauncherEntryKind.App));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                log($"launcher index: reading {folder} failed: {ex.Message}");
            }
            log($"launcher index: {list.Count - before} shortcuts in {folder}");
        }
    }

    private static void AddFiles(List<LauncherEntry> list)
    {
        var count = 0;
        foreach (var root in new[] { Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.MyDocuments })
        {
            try
            {
                foreach (var path in Directory.EnumerateFileSystemEntries(Environment.GetFolderPath(root)))
                {
                    var name = Path.GetFileName(path);
                    if (name.StartsWith('.') || name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                    list.Add(new LauncherEntry(name, path, LauncherEntryKind.File));
                    if (++count >= MaxFiles) return;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private static string? Name(ShellInterop.IShellItem item, uint sigdn)
    {
        item.GetDisplayName(sigdn, out var ptr);
        try { return Marshal.PtrToStringUni(ptr); }
        finally { Marshal.FreeCoTaskMem(ptr); }
    }

    [ComImport, Guid("70629033-E363-4A28-A567-0DB78006E6D7"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEnumShellItems
    {
        [PreserveSig] int Next(uint count, out ShellInterop.IShellItem item, out uint fetched);
        [PreserveSig] int Skip(uint count);
        [PreserveSig] int Reset();
        [PreserveSig] int Clone(out IEnumShellItems copy);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr bindCtx, in Guid riid, [MarshalAs(UnmanagedType.Interface)] out ShellInterop.IShellItem item);
}
