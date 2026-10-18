using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Windock.Core.Status;

namespace Windock.Interop;

/// Battery, network and keyboard-language readings for the status area (cheap, read on demand).
internal static class SystemStatus
{
    public sealed record Battery(int Percent, bool Charging, bool PluggedIn, TimeSpan? Remaining);

    /// Null on a PC without a battery (or when Windows does not know).
    public static Battery? ReadBattery()
    {
        if (!GetSystemPowerStatus(out var s)) return null;
        if ((s.BatteryFlag & 128) != 0 || s.BatteryFlag == 255 || s.BatteryLifePercent == 255) return null;
        var remaining = s.BatteryLifeTime is > 0 and < int.MaxValue ? TimeSpan.FromSeconds(s.BatteryLifeTime) : (TimeSpan?)null;
        return new Battery(s.BatteryLifePercent, (s.BatteryFlag & 8) != 0, s.ACLineStatus == 1, remaining);
    }

    /// The connection that carries traffic: an interface that is up and has a default gateway.
    public static (NetworkKind Kind, string? Name) ReadNetwork()
    {
        try
        {
            NetworkInterface? best = null;
            foreach (var n in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (n.OperationalStatus != OperationalStatus.Up) continue;
                if (n.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                if (!n.GetIPProperties().GatewayAddresses.Any(g => !g.Address.Equals(System.Net.IPAddress.Any) && !g.Address.Equals(System.Net.IPAddress.IPv6Any))) continue;
                if (best is null || n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) best = n;
            }
            if (best is null) return (NetworkKind.None, null);
            return (best.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? NetworkKind.Wifi : NetworkKind.Ethernet, best.Name);
        }
        catch (NetworkInformationException)
        {
            return (NetworkKind.None, null);
        }
    }

    /// Input language of the window in front (its thread's keyboard layout).
    public static int ForegroundLanguageId()
    {
        var thread = Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out _);
        return (int)((long)GetKeyboardLayout(thread) & 0xFFFF);
    }

    /// Win+Space: Windows' own input-language switcher, for the window in front.
    public static void SwitchInputLanguage()
    {
        keybd_event(VK_LWIN, 0, 0, IntPtr.Zero);
        keybd_event(VK_SPACE, 0, 0, IntPtr.Zero);
        keybd_event(VK_SPACE, 0, KEYEVENTF_KEYUP, IntPtr.Zero);
        keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, IntPtr.Zero);
    }

    private const byte VK_LWIN = 0x5B, VK_SPACE = 0x20;
    private const uint KEYEVENTF_KEYUP = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public int BatteryLifeTime, BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")] private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);
    [DllImport("user32.dll")] private static extern IntPtr GetKeyboardLayout(uint thread);
    [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint flags, IntPtr extra);
}
