using System.IO;
using System.Windows;

namespace S1.BlurOverlay;

public partial class App : Application
{
    public static readonly string LogPath = Path.Combine(Path.GetTempPath(), "s1-log.txt");

    public static int Accent { get; private set; } = 3;
    public static bool Layered { get; private set; }
    public static bool ResizeMode { get; private set; }
    public static bool Raw { get; private set; }
    public static int AnimateSeconds { get; private set; } = 8;
    public static int ExitAfterSeconds { get; private set; } = 30;

    public static void Log(string line) =>
        File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff} {line}{Environment.NewLine}");

    protected override void OnStartup(StartupEventArgs e)
    {
        foreach (var a in e.Args)
        {
            if (a.StartsWith("--accent=")) Accent = int.Parse(a[9..]);
            else if (a == "--layered") Layered = true;
            else if (a == "--resize") ResizeMode = true;
            else if (a == "--raw") Raw = true;
            else if (a.StartsWith("--animate=")) AnimateSeconds = int.Parse(a[10..]);
            else if (a.StartsWith("--exit=")) ExitAfterSeconds = int.Parse(a[7..]);
        }
        File.WriteAllText(LogPath, "");
        base.OnStartup(e);
    }
}
