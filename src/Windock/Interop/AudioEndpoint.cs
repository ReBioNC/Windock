using System.Runtime.InteropServices;
using System.Windows.Threading;
using Windock.Core.Logging;

namespace Windock.Interop;

/// Master volume of the default playback device (Core Audio). Changes made anywhere (keyboard keys,
/// the Windows mixer) arrive through callbacks; nothing is polled. Follows default-device switches.
internal sealed class AudioEndpoint : IDisposable
{
    private static readonly Guid EventContext = Guid.NewGuid();

    private readonly Dispatcher _ui;
    private readonly ILog _log;
    private readonly IMMDeviceEnumerator? _enumerator;
    private readonly DeviceNotifications? _deviceNotifications;
    private IAudioEndpointVolume? _volume;
    private VolumeCallback? _callback;

    public AudioEndpoint(Dispatcher ui, ILog log)
    {
        _ui = ui;
        _log = log;
        try
        {
            _enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            _deviceNotifications = new DeviceNotifications(this);
            _enumerator.RegisterEndpointNotificationCallback(_deviceNotifications);
            Attach();
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            _log.Error("core audio unavailable", ex);
        }
    }

    /// Raised on the UI thread when level, mute or the device changes.
    public event Action? Changed;

    public bool Available => _volume is not null;
    public double Level { get; private set; }
    public bool Muted { get; private set; }

    public void SetLevel(double level)
    {
        if (_volume is null) return;
        level = Math.Clamp(level, 0, 1);
        var context = EventContext;
        Try(() =>
        {
            _volume.SetMasterVolumeLevelScalar((float)level, ref context);
            if (Muted && level > 0) _volume.SetMute(false, ref context);
        });
        Read();
    }

    public void Step(double delta) => SetLevel(Level + delta);

    public void ToggleMute()
    {
        if (_volume is null) return;
        var context = EventContext;
        Try(() => _volume.SetMute(!Muted, ref context));
        Read();
    }

    private void Attach()
    {
        Detach();
        if (_enumerator is null) return;
        try
        {
            if (_enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out var device) != 0 || device is null) return;
            var iid = typeof(IAudioEndpointVolume).GUID;
            device.Activate(ref iid, 0x17 /* CLSCTX_ALL */, IntPtr.Zero, out var obj);
            Marshal.ReleaseComObject(device);
            _volume = (IAudioEndpointVolume)obj;
            _callback = new VolumeCallback(this);
            _volume.RegisterControlChangeNotify(_callback);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            _log.Error("default audio endpoint unavailable", ex);
            _volume = null;
        }
        Read();
    }

    private void Detach()
    {
        if (_volume is null) return;
        Try(() => _volume.UnregisterControlChangeNotify(_callback!));
        Marshal.ReleaseComObject(_volume);
        _volume = null;
        _callback = null;
    }

    private void Read()
    {
        if (_volume is not null)
        {
            Try(() =>
            {
                _volume.GetMasterVolumeLevelScalar(out var level);
                _volume.GetMute(out var muted);
                Level = level;
                Muted = muted;
            });
        }
        Changed?.Invoke();
    }

    private void Try(Action action)
    {
        try
        {
            action();
        }
        catch (COMException ex)
        {
            _log.Error("audio call failed", ex);
        }
    }

    public void Dispose()
    {
        Detach();
        if (_enumerator is not null && _deviceNotifications is not null)
            Try(() => _enumerator.UnregisterEndpointNotificationCallback(_deviceNotifications));
    }

    private sealed class VolumeCallback(AudioEndpoint owner) : IAudioEndpointVolumeCallback
    {
        public int OnNotify(IntPtr data)
        {
            owner._ui.BeginInvoke(owner.Read);
            return 0;
        }
    }

    private sealed class DeviceNotifications(AudioEndpoint owner) : IMMNotificationClient
    {
        public int OnDeviceStateChanged(string id, int state) => 0;
        public int OnDeviceAdded(string id) => 0;
        public int OnDeviceRemoved(string id) => 0;

        public int OnDefaultDeviceChanged(EDataFlow flow, ERole role, string? id)
        {
            if (flow == EDataFlow.Render && role == ERole.Multimedia) owner._ui.BeginInvoke(owner.Attach);
            return 0;
        }

        public int OnPropertyValueChanged(string id, PropertyKey key) => 0;
    }

    // ---- Core Audio COM declarations ----

    internal enum EDataFlow { Render = 0, Capture = 1, All = 2 }
    internal enum ERole { Console = 0, Multimedia = 1, Communications = 2 }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PropertyKey
    {
        public Guid FormatId;
        public int PropertyId;
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorComObject { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(EDataFlow flow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow flow, ERole role, out IMMDevice? device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        void RegisterEndpointNotificationCallback(IMMNotificationClient client);
        void UnregisterEndpointNotificationCallback(IMMNotificationClient client);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDevice
    {
        void Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioEndpointVolume
    {
        void RegisterControlChangeNotify(IAudioEndpointVolumeCallback notify);
        void UnregisterControlChangeNotify(IAudioEndpointVolumeCallback notify);
        void GetChannelCount(out uint count);
        void SetMasterVolumeLevel(float levelDb, ref Guid context);
        void SetMasterVolumeLevelScalar(float level, ref Guid context);
        void GetMasterVolumeLevel(out float levelDb);
        void GetMasterVolumeLevelScalar(out float level);
        void SetChannelVolumeLevel(uint channel, float levelDb, ref Guid context);
        void SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
        void GetChannelVolumeLevel(uint channel, out float levelDb);
        void GetChannelVolumeLevelScalar(uint channel, out float level);
        void SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        void GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    [ComImport, Guid("657804FA-D6AD-4496-8A60-352752AF4F89"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioEndpointVolumeCallback
    {
        [PreserveSig] int OnNotify(IntPtr notifyData);
    }

    [ComImport, Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMNotificationClient
    {
        [PreserveSig] int OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string id, int state);
        [PreserveSig] int OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int OnDefaultDeviceChanged(EDataFlow flow, ERole role, [MarshalAs(UnmanagedType.LPWStr)] string? id);
        [PreserveSig] int OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string id, PropertyKey key);
    }
}
