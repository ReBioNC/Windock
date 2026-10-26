using System.Runtime.InteropServices;
using Microsoft.Win32;
using Windock.Core.Launcher;
using Windock.Core.Logging;

namespace Windock.Interop;

/// Reads (never writes) Windows' per-account launch counts for the launcher's Frequent tab. They stay on this PC.
internal static class UserAssistReader
{
    private const string Root = @"Software\Microsoft\Windows\CurrentVersion\Explorer\UserAssist";

    public static IReadOnlyList<UsageRecord> Read(ILog log)
    {
        var records = new List<UsageRecord>();
        try
        {
            using var root = Registry.CurrentUser.OpenSubKey(Root);
            if (root is null) return records;
            foreach (var guid in root.GetSubKeyNames())
            {
                using var count = root.OpenSubKey(guid + @"\Count");
                if (count is null) continue;
                foreach (var valueName in count.GetValueNames())
                {
                    if (UserAssistDecoder.Parse(valueName, count.GetValue(valueName) as byte[]) is not { } entry) continue;
                    foreach (var key in UsageKeys.ForTarget(UserAssistDecoder.TargetFor(entry.Name), KnownFolders.Path))
                        records.Add(new UsageRecord(key, entry.Count, entry.LastUsedUtc));
                }
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
        {
            log.Error("reading Windows launch history failed", ex);
        }
        return records;
    }
}

/// Paths of Windows Known Folders ("{1AC14E77-…}" = System32), as used in launch history and app ids.
internal static class KnownFolders
{
    private static readonly Dictionary<Guid, string?> Cache = new();

    public static string? Path(Guid id)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(id, out var cached)) return cached;
            string? path = null;
            if (SHGetKnownFolderPath(id, 0, IntPtr.Zero, out var ptr) == 0)
            {
                path = Marshal.PtrToStringUni(ptr);
                Marshal.FreeCoTaskMem(ptr);
            }
            else if (ptr != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(ptr);
            }
            return Cache[id] = path;
        }
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);
}
