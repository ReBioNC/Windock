using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Windock.Core.Apps;
using Windock.Core.Logging;

namespace Windock.Interop;

/// Carries out dock clicks and menu commands (ARCHITECTURE §6.4). Never kills processes.
internal sealed class AppLauncher(ILog log, Action<string, string> notifyError)
{
    public const string AppsFolderPrefix = @"shell:AppsFolder\";

    public void Execute(ClickAction action, DockItem item)
    {
        switch (action)
        {
            case ClickAction.Launch launch:
                Launch(LaunchTargetFor(item, launch.Target), item.DisplayName);
                break;
            case ClickAction.Activate activate:
                Activate(activate.Hwnd);
                break;
            case ClickAction.Minimize minimize:
                Native.ShowWindow(minimize.Hwnd, Native.SW_MINIMIZE);
                break;
            case ClickAction.OpenRecycleBin:
                Start("explorer.exe", "shell:RecycleBinFolder");
                break;
        }
    }

    public void Activate(IntPtr hwnd)
    {
        if (Native.IsIconic(hwnd)) Native.ShowWindow(hwnd, Native.SW_RESTORE);
        if (!Native.SetForegroundWindow(hwnd)) log.Info($"SetForegroundWindow refused for 0x{hwnd:X}");
    }

    public void Launch(string target, string displayName)
    {
        try
        {
            if (target.StartsWith(AppsFolderPrefix, StringComparison.OrdinalIgnoreCase))
            {
                Start("explorer.exe", target);
                return;
            }
            if (!target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) && !File.Exists(target) && !Directory.Exists(target))
                throw new FileNotFoundException("Launch target not found", target);
            var info = new ProcessStartInfo(target) { UseShellExecute = true };
            if (target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) info.WorkingDirectory = Path.GetDirectoryName(target) ?? "";
            Process.Start(info)?.Dispose();
        }
        catch (Exception ex) when (ex is Win32Exception or FileNotFoundException or InvalidOperationException)
        {
            log.Error($"launch failed: {target}", ex);
            notifyError("Gagal membuka", displayName);
        }
    }

    /// Asks each window to close normally (it may still ask to save).
    public void CloseWindows(IEnumerable<IntPtr> hwnds)
    {
        foreach (var h in hwnds) Native.PostMessage(h, Native.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
    }

    public void RevealInExplorer(string path) => Start("explorer.exe", $"/select,\"{path}\"");

    /// Store apps run from WindowsApps and cannot be started by exe path; start them through their AUMID.
    public static string LaunchTargetFor(DockItem item, string target)
    {
        var id = item.AppId?.Value;
        if (id is not null && id.StartsWith("aumid:", StringComparison.Ordinal) &&
            (target.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase) || !File.Exists(target)))
            return AppsFolderPrefix + id["aumid:".Length..];
        return target;
    }

    private void Start(string file, string args)
    {
        try
        {
            Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = true })?.Dispose();
        }
        catch (Win32Exception ex)
        {
            log.Error($"start failed: {file} {args}", ex);
        }
    }
}
