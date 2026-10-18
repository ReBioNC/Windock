using Microsoft.Win32;

namespace Windock.Interop;

/// "Jalan saat Windows nyala": the Windock value under HKCU\...\Run. Per-user, removed by uninstall.ps1.
internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Windock";

    /// Makes the Run value match the setting; only writes when something differs.
    public static void Sync(bool enabled, string exePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        var wanted = $"\"{exePath}\"";
        var current = key.GetValue(ValueName) as string;
        if (enabled && !string.Equals(current, wanted, StringComparison.OrdinalIgnoreCase)) key.SetValue(ValueName, wanted);
        else if (!enabled && current is not null) key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
