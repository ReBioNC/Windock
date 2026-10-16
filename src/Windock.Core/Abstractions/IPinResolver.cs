using Windock.Core.Apps;
using Windock.Core.Settings;

namespace Windock.Core.Abstractions;

public sealed record PinResolution(AppId AppId, string DisplayName, string LaunchTarget, string IconPath, bool TargetExists);

public interface IPinResolver
{
    PinResolution Resolve(PinEntry pin);
}
