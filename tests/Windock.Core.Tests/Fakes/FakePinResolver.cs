using Windock.Core.Abstractions;
using Windock.Core.Apps;
using Windock.Core.Settings;

namespace Windock.Core.Tests.Fakes;

/// Resolves "X.exe" pins to AppId.FromExePath; specific paths can be overridden.
internal sealed class FakePinResolver : IPinResolver
{
    public Dictionary<string, PinResolution> Overrides { get; } = new(StringComparer.OrdinalIgnoreCase);

    public PinResolution Resolve(PinEntry pin) =>
        Overrides.TryGetValue(pin.Path, out var r)
            ? r
            : new PinResolution(AppId.FromExePath(pin.Path), Path.GetFileNameWithoutExtension(pin.Path), pin.Path, pin.Path, TargetExists: true);
}
