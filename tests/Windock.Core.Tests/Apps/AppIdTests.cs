using Windock.Core.Apps;

namespace Windock.Core.Tests.Apps;

public class AppIdTests
{
    [Fact]
    public void Exe_paths_compare_case_insensitively()
    {
        Assert.Equal(AppId.FromExePath(@"C:\Program Files\App\APP.exe"), AppId.FromExePath(@"c:\program files\app\app.exe"));
    }

    [Fact]
    public void Kinds_are_prefixed_and_distinct()
    {
        Assert.Equal("aumid:microsoft.windows.settings", AppId.FromAumid("Microsoft.Windows.Settings").Value);
        Assert.Equal("proc:notepad", AppId.FromProcessName("Notepad").Value);
        Assert.StartsWith("path:", AppId.FromExePath(@"C:\a.exe").Value);
    }

    [Fact]
    public void Choose_prefers_aumid_then_path_then_process_then_hwnd()
    {
        Assert.Equal(AppId.FromAumid("X.Y"), AppId.Choose("X.Y", @"C:\a.exe", "a", 1));
        Assert.Equal(AppId.FromExePath(@"C:\a.exe"), AppId.Choose(null, @"C:\a.exe", "a", 1));
        Assert.Equal(AppId.FromProcessName("a"), AppId.Choose("", null, "a", 1));
        Assert.Equal("hwnd:42", AppId.Choose(null, null, null, 42).Value);
    }

    [Fact]
    public void Choose_without_path_uses_process_name() // Review Focus 4
    {
        Assert.Equal("proc:secureapp", AppId.Choose(null, null, "SecureApp", 42).Value);
    }
}
