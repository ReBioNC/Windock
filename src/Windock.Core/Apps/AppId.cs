namespace Windock.Core.Apps;

/// Identity of one dock icon: windows and pins with the same AppId share an icon (ARCHITECTURE §6.2).
public readonly record struct AppId(string Value)
{
    public static AppId FromAumid(string aumid) => new("aumid:" + aumid.ToLowerInvariant());

    public static AppId FromExePath(string path) => new("path:" + System.IO.Path.GetFullPath(path).ToLowerInvariant());

    public static AppId FromProcessName(string name) => new("proc:" + name.ToLowerInvariant());

    public static AppId Choose(string? aumid, string? exePath, string? processName, nint hwnd)
    {
        if (!string.IsNullOrWhiteSpace(aumid)) return FromAumid(aumid);
        if (!string.IsNullOrWhiteSpace(exePath)) return FromExePath(exePath);
        if (!string.IsNullOrWhiteSpace(processName)) return FromProcessName(processName);
        return new AppId("hwnd:" + hwnd);
    }

    public override string ToString() => Value;
}
