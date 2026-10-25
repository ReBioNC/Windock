using System.Runtime.InteropServices;
using Windock.Core.Island;
using Windock.Core.Logging;
using Windock.Interop;

namespace Windock.Island;

/// Screen brightness changes (laptop panels): Windows sends the new percentage as a power-setting
/// notification. The first value after registering is the current level, not a change.
internal sealed class BrightnessWatcher : IDisposable
{
    // GUID_VIDEO_CURRENT_MONITOR_BRIGHTNESS, and the power plan's brightness (updated by the brightness keys).
    private static readonly Guid[] Settings =
    {
        new("8ffee2c6-2d01-46be-adb9-398addc5b4ff"),
        new("aded5e82-b909-4619-9949-f5d71dac0bcb"),
    };

    private readonly MessageWindow _messages;
    private readonly List<IntPtr> _registrations = new();
    private int? _level;

    public BrightnessWatcher(MessageWindow messages, ILog log)
    {
        _messages = messages;
        foreach (var guid in Settings)
        {
            var g = guid;
            var handle = RegisterPowerSettingNotification(messages.Handle, ref g, 0 /* DEVICE_NOTIFY_WINDOW_HANDLE */);
            if (handle != IntPtr.Zero) _registrations.Add(handle);
        }
        if (_registrations.Count == 0) log.Info("brightness notifications unavailable");
        messages.Message += OnMessage;
    }

    /// Percent 0..100; raised only when the level differs from the last one seen.
    public event Action<int>? Changed;

    private void OnMessage(int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_POWERBROADCAST || wParam != (IntPtr)PBT_POWERSETTINGCHANGE || lParam == IntPtr.Zero) return;
        var setting = Marshal.PtrToStructure<Guid>(lParam);
        if (!Settings.Contains(setting) || Marshal.ReadInt32(lParam, 16) < 4) return;
        var level = Math.Clamp(Marshal.ReadInt32(lParam, 20), 0, 100);
        var first = _level is null;
        if (level == _level) return;
        _level = level;
        if (!first) Changed?.Invoke(level);
    }

    public void Dispose()
    {
        _messages.Message -= OnMessage;
        foreach (var h in _registrations) UnregisterPowerSettingNotification(h);
        _registrations.Clear();
    }

    private const int WM_POWERBROADCAST = 0x0218, PBT_POWERSETTINGCHANGE = 0x8013;

    [DllImport("user32.dll")] private static extern IntPtr RegisterPowerSettingNotification(IntPtr recipient, ref Guid setting, int flags);
    [DllImport("user32.dll")] private static extern bool UnregisterPowerSettingNotification(IntPtr handle);
}

/// Keyboards, mice and monitors plugged in or removed (WM_DEVICECHANGE for those interface classes).
internal sealed class DeviceWatcher : IDisposable
{
    private static readonly (Guid Class, DeviceKind Kind)[] Classes =
    {
        (new("884b96c3-56ef-11d1-bc8c-00a0c91405dd"), DeviceKind.Keyboard),
        (new("378de44c-56ef-11d1-bc8c-00a0c91405dd"), DeviceKind.Mouse),
        (new("e6f07b5f-ee97-4a90-b076-33f57bf4eaa7"), DeviceKind.Display),
    };

    private readonly MessageWindow _messages;
    private readonly List<IntPtr> _registrations = new();
    /// Names seen on arrival, so a removal can say which device left.
    private readonly Dictionary<string, string?> _names = new(StringComparer.OrdinalIgnoreCase);

    public DeviceWatcher(MessageWindow messages, ILog log)
    {
        _messages = messages;
        foreach (var (cls, _) in Classes)
        {
            var filter = new DEV_BROADCAST_DEVICEINTERFACE
            {
                Size = Marshal.SizeOf<DEV_BROADCAST_DEVICEINTERFACE>(), DeviceType = DBT_DEVTYP_DEVICEINTERFACE, ClassGuid = cls,
            };
            var handle = RegisterDeviceNotification(messages.Handle, ref filter, 0 /* DEVICE_NOTIFY_WINDOW_HANDLE */);
            if (handle != IntPtr.Zero) _registrations.Add(handle);
            else log.Info($"device notifications for {cls} unavailable");
        }
        messages.Message += OnMessage;
    }

    /// kind, arrived, a readable device name when one is known.
    public event Action<DeviceKind, bool, string?>? Changed;

    private void OnMessage(int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_DEVICECHANGE || lParam == IntPtr.Zero) return;
        var arrived = wParam == (IntPtr)DBT_DEVICEARRIVAL;
        if (!arrived && wParam != (IntPtr)DBT_DEVICEREMOVECOMPLETE) return;
        if (Marshal.ReadInt32(lParam, 4) != DBT_DEVTYP_DEVICEINTERFACE) return;
        var cls = Marshal.PtrToStructure<Guid>(lParam + 12);
        var match = Classes.FirstOrDefault(c => c.Class == cls);
        if (match.Class == Guid.Empty) return;
        var path = Marshal.PtrToStringUni(lParam + 28) ?? "";
        string? name;
        if (arrived) _names[path] = name = DeviceNames.Find(path);
        else if (_names.Remove(path, out var known)) name = known;
        else name = null;
        Changed?.Invoke(match.Kind, arrived, name);
    }

    public void Dispose()
    {
        _messages.Message -= OnMessage;
        foreach (var h in _registrations) UnregisterDeviceNotification(h);
        _registrations.Clear();
    }

    private const int WM_DEVICECHANGE = 0x0219, DBT_DEVICEARRIVAL = 0x8000, DBT_DEVICEREMOVECOMPLETE = 0x8004, DBT_DEVTYP_DEVICEINTERFACE = 5;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEV_BROADCAST_DEVICEINTERFACE
    {
        public int Size, DeviceType, Reserved;
        public Guid ClassGuid;
        public char Name;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr RegisterDeviceNotification(IntPtr recipient, ref DEV_BROADCAST_DEVICEINTERFACE filter, int flags);
    [DllImport("user32.dll")] private static extern bool UnregisterDeviceNotification(IntPtr handle);
}

/// A product name for a device interface: the device or one of its parents usually knows it ("G304",
/// "MX Keys", "DELL P2419H"); generic driver names ("HID-compliant mouse") are skipped.
internal static class DeviceNames
{
    private static readonly DEVPROPKEY InstanceId = new(new Guid("78c34fc8-104a-4aca-9ea4-524d52996e57"), 256);
    private static readonly DEVPROPKEY FriendlyName = new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);
    private static readonly DEVPROPKEY BusReportedDescription = new(new Guid("540b947e-8b40-45bc-a8a2-6a0b894cbda2"), 4);
    private static readonly string[] Generic =
        {
            "hid", "usb input device", "usb composite device", "generic", "compliant", "root hub", "receiver", "pnp monitor", "keyboard device",
            // Walking up from a built-in (PS/2) keyboard reaches the chipset: never a product name.
            "bridge", "controller", "pci", "acpi", "bus", "enumerator", "system",
        };

    public static string? Find(string interfacePath)
    {
        try
        {
            if (InterfaceString(interfacePath, InstanceId) is not { } id || CM_Locate_DevNodeW(out var node, id, 0) != 0) return null;
            for (var level = 0; level < 4; level++)
            {
                foreach (var key in new[] { FriendlyName, BusReportedDescription })
                    if (NodeString(node, key) is { } name && !IsGeneric(name)) return name;
                if (CM_Get_Parent(out node, node, 0) != 0) break;
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
        }
        return null;
    }

    private static bool IsGeneric(string name) =>
        string.IsNullOrWhiteSpace(name) || Generic.Any(g => name.Contains(g, StringComparison.OrdinalIgnoreCase));

    private static string? InterfaceString(string path, DEVPROPKEY key)
    {
        var buffer = new byte[512];
        var size = (uint)buffer.Length;
        var k = key;
        return CM_Get_Device_Interface_PropertyW(path, ref k, out var type, buffer, ref size, 0) == 0 && type == DEVPROP_TYPE_STRING
            ? Text(buffer, size) : null;
    }

    private static string? NodeString(uint node, DEVPROPKEY key)
    {
        var buffer = new byte[512];
        var size = (uint)buffer.Length;
        var k = key;
        return CM_Get_DevNode_PropertyW(node, ref k, out var type, buffer, ref size, 0) == 0 && type == DEVPROP_TYPE_STRING
            ? Text(buffer, size) : null;
    }

    private static string Text(byte[] buffer, uint size) => System.Text.Encoding.Unicode.GetString(buffer, 0, (int)size).TrimEnd('\0').Trim();

    private const uint DEVPROP_TYPE_STRING = 0x12;

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct DEVPROPKEY(Guid fmtid, uint pid)
    {
        public readonly Guid FmtId = fmtid;
        public readonly uint Pid = pid;
    }

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_Interface_PropertyW(string path, ref DEVPROPKEY key, out uint type, byte[] buffer, ref uint size, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Locate_DevNodeW(out uint node, string id, uint flags);
    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Get_DevNode_PropertyW(uint node, ref DEVPROPKEY key, out uint type, byte[] buffer, ref uint size, uint flags);
    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Get_Parent(out uint parent, uint node, uint flags);
}
