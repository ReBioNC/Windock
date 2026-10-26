using Microsoft.Win32;

namespace Windock.Interop;

/// "Start Windock automatically": the Windock value under HKCU\...\Run. Per-user, removed by uninstall.ps1.
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

    /// True when Windows will really start Windock at the next sign-in: the Run value exists and is not
    /// turned off in Task Manager › Startup (StartupApproved, first byte odd = disabled).
    public static bool WillStartAtSignIn()
    {
        try
        {
            using var run = Registry.CurrentUser.OpenSubKey(RunKey);
            if (run?.GetValue(ValueName) is not string) return false;
            using var approved = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run");
            return approved?.GetValue(ValueName) is not byte[] { Length: > 0 } state || (state[0] & 1) == 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
        {
            return false;
        }
    }
}
