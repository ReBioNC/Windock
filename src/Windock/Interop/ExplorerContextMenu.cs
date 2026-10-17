using Microsoft.Win32;

namespace Windock.Interop;

/// "Pin ke Windock" in the Explorer/Desktop right-click menu for programs and shortcuts.
/// Per-user registration under HKCU\Software\Classes — no administrator rights, removed by uninstall.ps1.
internal static class ExplorerContextMenu
{
    private const string Verb = "WindockPin";
    private static readonly string[] ProgIds = { "exefile", "lnkfile" };

    public static void Register(string exePath)
    {
        foreach (var progId in ProgIds)
        {
            using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{progId}\shell\{Verb}");
            key.SetValue("MUIVerb", "Pin ke Windock");
            key.SetValue("Icon", $"\"{exePath}\",0");
            using var command = key.CreateSubKey("command");
            command.SetValue("", $"\"{exePath}\" --pin \"%1\"");
        }
    }

    public static void Unregister()
    {
        foreach (var progId in ProgIds)
            Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{progId}\shell\{Verb}", throwOnMissingSubKey: false);
    }

    public static bool IsRegisteredFor(string exePath)
    {
        using var command = Registry.CurrentUser.OpenSubKey($@"Software\Classes\exefile\shell\{Verb}\command");
        return command?.GetValue("") is string value && value.Contains(exePath, StringComparison.OrdinalIgnoreCase);
    }
}
