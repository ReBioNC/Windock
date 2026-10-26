using System.IO;
using System.Runtime.InteropServices;
using Windock.Core.Launcher;
using Windock.Core.Logging;
using Windock.Core.Settings;

namespace Windock.Interop;

/// What the launcher found, plus Windows' launch history when asked for (empty otherwise).
internal sealed record LauncherIndexResult(IReadOnlyList<LauncherEntry> Entries, IReadOnlyList<UsageRecord> WindowsHistory);

/// What the launcher can find: every app in Start (shell:AppsFolder, mostly Store apps and registered
/// programs), every Start Menu shortcut (the AppsFolder list misses some; .url ones are mostly Steam/Epic games),
/// and optionally the files directly on the Desktop and in Documents. Apps get their usage keys and category,
/// and entries with the same name are merged into one.
/// Built when the launcher opens and dropped with it.
internal static class LauncherIndex
{
    private const int MaxFiles = 3000;
    private static readonly Guid BHID_EnumItems = new("94F60519-2850-4924-AA5A-D15E84868039");
    private static readonly Guid IID_IEnumShellItems = new("70629033-E363-4A28-A567-0DB78006E6D7");
    private const uint SIGDN_NORMALDISPLAY = 0, SIGDN_PARENTRELATIVEPARSING = 0x80018001;

    /// Runs on its own STA thread (the shell folder enumeration expects one).
    public static Task<LauncherIndexResult> BuildAsync(bool includeFiles, bool readWindowsHistory,
        IReadOnlyList<LauncherCategoryOverride> overrides, ILog log)
    {
        var done = new TaskCompletionSource<LauncherIndexResult>();
        var thread = new Thread(() =>
        {
            try
            {
                var list = new List<LauncherEntry>();
                AddApps(list);
                log.Info($"launcher index: {list.Count} apps in shell:AppsFolder");
                AddStartMenuShortcuts(list, message => log.Info(message));
                AddSelf(list);
                var apps = Describe(list, overrides, log);
                if (includeFiles) AddFiles(apps);
                var history = readWindowsHistory ? UserAssistReader.Read(log) : Array.Empty<UsageRecord>();
                log.Info($"launcher index: {apps.Count} entries, {history.Count} history records");
                done.SetResult(new LauncherIndexResult(apps, history));
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

    /// Adds usage keys and a category to every app, and merges apps listed twice (Start and a Start Menu
    /// shortcut) into the first one, keeping all their keys.
    private static List<LauncherEntry> Describe(List<LauncherEntry> raw, IReadOnlyList<LauncherCategoryOverride> overrides, ILog log)
    {
        var resolver = new PinResolver(log);  // its own instance: this is not the UI thread
        var byName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var result = new List<LauncherEntry>(raw.Count);
        foreach (var entry in raw)
        {
            var (keys, exe, url) = KeysFor(entry.Target, resolver);
            var category = url?.StartsWith("steam://", StringComparison.OrdinalIgnoreCase) == true
                || url?.StartsWith("com.epicgames.launcher://", StringComparison.OrdinalIgnoreCase) == true
                ? AppCategorizer.Categorize(entry.Name, url, exe, overrides)
                : AppCategorizer.Categorize(entry.Name, entry.Target, exe, overrides);
            if (byName.TryGetValue(entry.Name, out var at))
            {
                var first = result[at];
                result[at] = first with
                {
                    UsageKeys = first.UsageKeys.Concat(keys).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                    Category = first.Category == LauncherCategory.Other ? category : first.Category,
                };
                continue;
            }
            byName[entry.Name] = result.Count;
            result.Add(entry with { UsageKeys = keys, Category = category });
        }
        return result;
    }

    /// App identity keys first (what the dock records), then the shortcut file itself.
    private static (IReadOnlyList<string> Keys, string? Exe, string? Url) KeysFor(string target, PinResolver resolver)
    {
        var keys = new List<string>();
        string? exe = null, url = null;
        if (target.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            var id = resolver.Resolve(new PinEntry(target)).AppId.Value;
            keys.Add(id);
            if (id.StartsWith("path:", StringComparison.Ordinal)) exe = id[5..];
        }
        else if (target.EndsWith(".url", StringComparison.OrdinalIgnoreCase))
        {
            url = ReadUrl(target);
        }
        foreach (var key in UsageKeys.ForTarget(target, KnownFolders.Path))
        {
            if (!keys.Contains(key, StringComparer.OrdinalIgnoreCase)) keys.Add(key);
            if (exe is null && key.StartsWith("path:", StringComparison.Ordinal)) exe = key[5..];
        }
        return (keys, exe, url);
    }

    /// The URL= line of an Internet shortcut (steam://rungameid/… for Steam games).
    private static string? ReadUrl(string path)
    {
        try
        {
            foreach (var line in File.ReadLines(path).Take(40))
                if (line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase)) return line[4..].Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return null;
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
                foreach (var path in Directory.EnumerateFiles(folder, "*.lnk", Recursive).Concat(Directory.EnumerateFiles(folder, "*.url", Recursive)))
                    list.Add(new LauncherEntry(Path.GetFileNameWithoutExtension(path), path, LauncherEntryKind.App));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                log($"launcher index: reading {folder} failed: {ex.Message}");
            }
            log($"launcher index: {list.Count - before} shortcuts in {folder}");
        }
    }

    /// Windock itself, even when its Start Menu shortcuts were missed or never made (run from source).
    /// Starting it again opens Settings; search drops the duplicate when a shortcut has the same name.
    private static void AddSelf(List<LauncherEntry> list)
    {
        if (Environment.ProcessPath is not { } exe) return;
        list.Add(new LauncherEntry("Windock", exe, LauncherEntryKind.App));
        list.Add(new LauncherEntry("Windock Settings", exe, LauncherEntryKind.App));
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
