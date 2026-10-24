using Microsoft.Win32;

namespace Windock.Interop;

/// "Pin to Windock" in the Explorer/Desktop right-click menu for programs and shortcuts.
/// Per-user registration under HKCU\Software\Classes — no administrator rights, removed by uninstall.ps1.
internal static class ExplorerContextMenu
{
    private const string Verb = "WindockPin";
    private const string Label = "Pin to Windock";
    private static readonly string[] ProgIds = { "exefile", "lnkfile" };

    public static void Register(string exePath)
    {
        foreach (var progId in ProgIds)
        {
            using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{progId}\shell\{Verb}");
            key.SetValue("MUIVerb", Label);
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
        using var verb = Registry.CurrentUser.OpenSubKey($@"Software\Classes\exefile\shell\{Verb}");
        using var command = Registry.CurrentUser.OpenSubKey($@"Software\Classes\exefile\shell\{Verb}\command");
        // A different label (an older Windock in another language) counts as not registered, so it is rewritten.
        return verb?.GetValue("MUIVerb") is Label && command?.GetValue("") is string value && value.Contains(exePath, StringComparison.OrdinalIgnoreCase);
    }
}
