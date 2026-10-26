using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Windock.Interop;

/// Windows' own "Animate windows when minimizing and maximizing". Windock only changes it for the current
/// sign-in (never saved), so a sign-out, restart or crash can't leave it changed for good; Restore puts back the
/// value the user saved, which Windock never touches.
internal static class WindowsMinimizeAnimation
{
    private const uint SPI_GETANIMATION = 0x0048, SPI_SETANIMATION = 0x0049, SPIF_SENDCHANGE = 0x2;

    public static void Suppress(bool suppress)
    {
        var wanted = !suppress && SavedSetting();
        if (Current() != wanted) Set(wanted);
    }

    public static void Restore() => Suppress(false);

    private static bool Current()
    {
        var info = new ANIMATIONINFO { cbSize = (uint)Marshal.SizeOf<ANIMATIONINFO>() };
        return SystemParametersInfo(SPI_GETANIMATION, info.cbSize, ref info, 0) && info.iMinAnimate != 0;
    }

    private static void Set(bool on)
    {
        var info = new ANIMATIONINFO { cbSize = (uint)Marshal.SizeOf<ANIMATIONINFO>(), iMinAnimate = on ? 1 : 0 };
        SystemParametersInfo(SPI_SETANIMATION, info.cbSize, ref info, SPIF_SENDCHANGE);  // no SPIF_UPDATEINIFILE: not saved
    }

    /// The user's saved choice (Control Panel › Performance Options); on when never set.
    private static bool SavedSetting()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop\WindowMetrics");
            return key?.GetValue("MinAnimate") is not string value || value.Trim() != "0";
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
        {
            return true;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ANIMATIONINFO { public uint cbSize; public int iMinAnimate; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint param, ref ANIMATIONINFO info, uint winIni);
}
