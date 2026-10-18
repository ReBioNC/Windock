using Windock.Core.Abstractions;
using Windock.Core.Logging;
using Windock.Core.Settings;
using Windock.Interop;

namespace Windock.Settings;

/// What the settings pages need from the rest of the app.
internal sealed record SettingsContext(
    SettingsStore Store,
    IconProvider Icons,
    IPinResolver Pins,
    Func<IReadOnlyList<PinEntry>> DefaultPins,
    Func<string, bool> TryRegisterHotkey,
    Func<string, bool> TryRegisterLauncherHotkey,
    Action<bool> SuspendHotkey,
    string DataDirectory,
    ILog Log);
