using System.Globalization;

namespace Windock.Core.Logging;

/// Daily text log in %AppData%\Windock\logs, kept for 7 days.
public sealed class FileLog : ILog
{
    private const int RetentionDays = 7;
    private readonly string _directory;
    private readonly Func<DateTime> _now;
    private readonly object _gate = new();

    public FileLog(string logsDirectory, Func<DateTime> now)
    {
        _directory = logsDirectory;
        _now = now;
        Directory.CreateDirectory(logsDirectory);
        DeleteOldLogs();
    }

    public void Info(string message) => Write("INFO", message, null);

    public void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    private void Write(string level, string message, Exception? ex)
    {
        var now = _now();
        var line = $"{now:HH:mm:ss.fff} [{level}] {message}{(ex is null ? "" : Environment.NewLine + ex)}{Environment.NewLine}";
        lock (_gate)
        {
            try
            {
                File.AppendAllText(Path.Combine(_directory, $"windock-{now:yyyyMMdd}.log"), line);
            }
            catch (IOException)
            {
                // Logging must never take the dock down.
            }
        }
    }

    private void DeleteOldLogs()
    {
        var cutoff = _now().Date.AddDays(-RetentionDays);
        foreach (var file in Directory.GetFiles(_directory, "windock-*.log"))
        {
            var stamp = Path.GetFileNameWithoutExtension(file)["windock-".Length..];
            if (DateTime.TryParseExact(stamp, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) && day < cutoff)
            {
                try { File.Delete(file); }
                catch (IOException) { }
            }
        }
    }
}
