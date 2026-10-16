using System.IO;
using System.Runtime.InteropServices;
using Windock.Core.Abstractions;
using Windock.Core.Apps;
using Windock.Core.Logging;
using Windock.Core.Settings;

namespace Windock.Interop;

/// Turns a pinned .exe or .lnk into the AppId its running windows will have (ARCHITECTURE §6.2).
internal sealed class PinResolver(ILog log) : IPinResolver
{
    public PinResolution Resolve(PinEntry pin)
    {
        var name = Path.GetFileNameWithoutExtension(pin.Path);
        if (!pin.Path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            return new PinResolution(AppId.FromExePath(pin.Path), DisplayName(pin.Path) ?? name, pin.Path, pin.Path, File.Exists(pin.Path));

        string? target = null, arguments = null, aumid = null;
        if (File.Exists(pin.Path))
        {
            try
            {
                (target, arguments, aumid) = ShellInterop.ReadShortcut(pin.Path);
                target = SquirrelAppExe(target, arguments) ?? target;
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException or IOException)
            {
                log.Error($"cannot read shortcut {pin.Path}", ex);
            }
        }

        // Classic apps match by target exe path (that is how WindowTracker identifies them); Store apps by AUMID.
        var id = target is not null ? AppId.FromExePath(target) : AppId.Choose(aumid, null, name, 0);
        var exists = target is null ? File.Exists(pin.Path) : File.Exists(target);
        return new PinResolution(id, name, pin.Path, pin.Path, exists);
    }

    /// Squirrel apps (Discord, Slack…) pin "Update.exe --processStart App.exe"; the window belongs to the newest app-*\App.exe.
    private static string? SquirrelAppExe(string? target, string? arguments)
    {
        if (target is null || arguments is null || !Path.GetFileName(target).Equals("Update.exe", StringComparison.OrdinalIgnoreCase)) return null;
        var match = System.Text.RegularExpressions.Regex.Match(arguments, @"--processStart\s+""?([^""\s]+)");
        var root = Path.GetDirectoryName(target);
        if (!match.Success || root is null || !Directory.Exists(root)) return null;
        return Directory.GetDirectories(root, "app-*")
            .OrderByDescending(d => Version.TryParse(Path.GetFileName(d)[4..], out var v) ? v : new Version())
            .Select(d => Path.Combine(d, match.Groups[1].Value))
            .FirstOrDefault(File.Exists);
    }

    private static string? DisplayName(string exe)
    {
        try
        {
            var description = System.Diagnostics.FileVersionInfo.GetVersionInfo(exe).FileDescription;
            return string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }
}
