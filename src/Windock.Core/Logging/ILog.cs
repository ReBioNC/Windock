namespace Windock.Core.Logging;

public interface ILog
{
    void Info(string message);
    void Error(string message, Exception? ex = null);
}

public sealed class NullLog : ILog
{
    public static readonly NullLog Instance = new();

    public void Info(string message) { }

    public void Error(string message, Exception? ex = null) { }
}
