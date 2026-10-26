namespace Windock.Core.Launcher;

/// One program or shortcut from Windows' own launch history.
public sealed record UserAssistEntry(string Name, int Count, DateTime? LastUsedUtc);

/// Windows counts launches per account under HKCU\…\Explorer\UserAssist\{GUID}\Count: value names are
/// ROT13-scrambled paths or app ids, values a small binary record. Windock only reads it.
public static class UserAssistDecoder
{
    public static string Rot13(string text)
    {
        var chars = text.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            var c = chars[i];
            if (c is >= 'a' and <= 'z') chars[i] = (char)('a' + (c - 'a' + 13) % 26);
            else if (c is >= 'A' and <= 'Z') chars[i] = (char)('A' + (c - 'A' + 13) % 26);
        }
        return new string(chars);
    }

    /// null for Windows' own bookkeeping values and records that were never run.
    /// Windows 7+ records are 72 bytes: run count at 4, last run (FILETIME) at 60. XP-era records are 16 bytes:
    /// count + 5 at 4, last run at 8.
    public static UserAssistEntry? Parse(string scrambledName, byte[]? data)
    {
        var name = Rot13(scrambledName);
        if (data is null || name.StartsWith("UEME_", StringComparison.Ordinal) || name.Length == 0) return null;
        int count;
        long fileTime;
        if (data.Length >= 68)
        {
            count = BitConverter.ToInt32(data, 4);
            fileTime = BitConverter.ToInt64(data, 60);
        }
        else if (data.Length >= 16)
        {
            count = BitConverter.ToInt32(data, 4) - 5;
            fileTime = BitConverter.ToInt64(data, 8);
        }
        else
        {
            return null;
        }
        DateTime? last = fileTime > 0 && fileTime < DateTime.MaxValue.ToFileTimeUtc() ? DateTime.FromFileTimeUtc(fileTime) : null;
        if (count <= 0) return null;
        return new UserAssistEntry(name, count, last);
    }

    /// The history names paths ("{KnownFolder}\…\app.exe", "C:\…") or app ids ("Chrome",
    /// "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App"); ids are opened through shell:AppsFolder.
    public static string TargetFor(string name) =>
        name.StartsWith('{') || (name.Length > 2 && name[1] == ':') || name.StartsWith(@"\\", StringComparison.Ordinal)
            ? name
            : UsageKeys.AppsFolderPrefix + name;
}
