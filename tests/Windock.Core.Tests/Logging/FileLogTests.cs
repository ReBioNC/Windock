using Windock.Core.Logging;

namespace Windock.Core.Tests.Logging;

public sealed class FileLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "windock-tests-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTime Now = new(2026, 10, 16, 8, 0, 0);

    public FileLogTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Writes_daily_file_with_level()
    {
        var log = new FileLog(_dir, () => Now);

        log.Info("hello");
        log.Error("boom", new InvalidOperationException("bad"));

        var text = File.ReadAllText(Path.Combine(_dir, "windock-20261016.log"));
        Assert.Contains("08:00:00.000 [INFO] hello", text);
        Assert.Contains("[ERROR] boom", text);
        Assert.Contains("InvalidOperationException: bad", text);
    }

    [Fact]
    public void Deletes_only_old_windock_logs()
    {
        foreach (var name in new[] { "windock-20261001.log", "windock-20261012.log", "notes.txt", "other-20261001.log" })
            File.WriteAllText(Path.Combine(_dir, name), "x");

        _ = new FileLog(_dir, () => Now);

        Assert.Equal(new[] { "notes.txt", "other-20261001.log", "windock-20261012.log" },
            Directory.GetFiles(_dir).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public void Creates_missing_directory()
    {
        var nested = Path.Combine(_dir, "logs");

        new FileLog(nested, () => Now).Info("x");

        Assert.True(File.Exists(Path.Combine(nested, "windock-20261016.log")));
    }
}
