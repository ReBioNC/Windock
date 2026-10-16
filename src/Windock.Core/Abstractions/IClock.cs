namespace Windock.Core.Abstractions;

/// Monotonic time source for animations.
public interface IClock
{
    TimeSpan Now { get; }
}
