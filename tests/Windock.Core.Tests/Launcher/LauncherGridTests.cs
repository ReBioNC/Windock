using System.Text;
using Windock.Core.Launcher;
using Windock.Core.Logging;

namespace Windock.Core.Tests.Launcher;

public class AppCategorizerTests
{
    [Theory]
    [InlineData("Steam", @"C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Steam\Steam.lnk", LauncherCategory.Games)]
    [InlineData("Growtopia", @"C:\Users\me\Desktop\Growtopia.lnk", LauncherCategory.Games)]
    [InlineData("Some Game", "steam://rungameid/123", LauncherCategory.Games)]
    [InlineData("Hollow", @"D:\SteamLibrary\steamapps\common\Hollow\hollow.exe", LauncherCategory.Games)]
    [InlineData("Visual Studio Code", @"C:\x\Visual Studio Code.lnk", LauncherCategory.Development)]
    [InlineData("Android Studio", @"C:\x\Android Studio.lnk", LauncherCategory.Development)]
    [InlineData("Google Chrome", @"C:\x\Google Chrome.lnk", LauncherCategory.Internet)]
    [InlineData("Discord", @"C:\x\Discord.lnk", LauncherCategory.Internet)]
    [InlineData("Windows Media Player", @"C:\x\Windows Media Player.lnk", LauncherCategory.Media)]
    [InlineData("Spotify", @"C:\x\Spotify.lnk", LauncherCategory.Media)]
    [InlineData("Excel", @"C:\x\Excel.lnk", LauncherCategory.Office)]
    [InlineData("Task Manager", @"C:\x\Task Manager.lnk", LauncherCategory.System)]
    [InlineData("Xbox", @"shell:AppsFolder\Microsoft.GamingApp_8wekyb3d8bbwe!Microsoft.Xbox.App", LauncherCategory.Games)]
    [InlineData("Weird Tool", @"C:\x\Weird Tool.lnk", LauncherCategory.Other)]
    public void Guesses_categories(string name, string target, LauncherCategory expected) =>
        Assert.Equal(expected, AppCategorizer.Categorize(name, target, exePath: null));

    [Fact]
    public void Exe_name_counts_when_the_display_name_is_unknown() =>
        Assert.Equal(LauncherCategory.Internet, AppCategorizer.Categorize("Browser", @"C:\x\Browser.lnk", @"C:\Program Files\BraveSoftware\brave.exe"));

    [Fact]
    public void Whole_words_only() =>
        Assert.Equal(LauncherCategory.Other, AppCategorizer.Categorize("Barcode Studio", @"C:\x\a.lnk", null));

    [Theory]
    [InlineData("Uninstall Steam", true)]
    [InlineData("Python 3.12 Documentation", true)]
    [InlineData("Readme", true)]
    [InlineData("Helper Tool", false)]
    [InlineData("Discord", false)]
    public void Spots_start_menu_clutter(string name, bool expected) => Assert.Equal(expected, AppCategorizer.IsClutter(name));

    [Fact]
    public void User_choice_wins() =>
        Assert.Equal(LauncherCategory.Development, AppCategorizer.Categorize("Discord", @"C:\x\Discord.lnk", null,
            new[] { new LauncherCategoryOverride("discord", LauncherCategory.Development) }));
}

public class UsageKeysTests
{
    private static readonly Guid System32 = new("1AC14E77-02E7-4E5D-B744-2EB1AE5198B7");
    private static string? Known(Guid id) => id == System32 ? @"C:\Windows\System32" : null;

    [Fact]
    public void Exe_paths_use_the_dock_app_id() =>
        Assert.Equal(new[] { @"path:c:\windows\system32\taskmgr.exe" }, UsageKeys.ForTarget(@"C:\Windows\System32\Taskmgr.exe", Known));

    [Fact]
    public void Known_folder_ids_are_expanded() =>
        Assert.Equal(new[] { @"path:c:\windows\system32\cmd.exe" }, UsageKeys.ForTarget(@"{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\cmd.exe", Known));

    [Fact]
    public void Apps_folder_ids_become_aumid_keys() =>
        Assert.Equal(new[] { "aumid:chrome" }, UsageKeys.ForTarget(@"shell:AppsFolder\Chrome", Known));

    [Fact]
    public void Apps_folder_paths_become_path_keys() =>
        Assert.Equal(new[] { @"path:c:\windows\system32\notepad.exe" },
            UsageKeys.ForTarget(@"shell:AppsFolder\{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\notepad.exe", Known));

    [Fact]
    public void Shortcuts_become_file_keys() =>
        Assert.Equal(new[] { @"file:c:\menu\chrome.lnk" }, UsageKeys.ForTarget(@"C:\Menu\Chrome.lnk", Known));

    [Fact]
    public void Unknown_known_folder_gives_no_key() =>
        Assert.Empty(UsageKeys.ForTarget(@"{00000000-0000-0000-0000-000000000001}\x.exe", Known));
}

public class UsageRankingTests
{
    private static readonly DateTime Now = new(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);
    private static LauncherEntry App(string name, params string[] keys) => new(name, name + ".lnk", LauncherEntryKind.App) { UsageKeys = keys };
    private static readonly IReadOnlySet<string> NoneHidden = new HashSet<string>();

    [Fact]
    public void Recent_use_beats_old_heavy_use()
    {
        var entries = new[] { App("Old", "k:old"), App("Daily", "k:daily") };
        var records = new[] { new UsageRecord("k:old", 50, Now.AddDays(-365)), new UsageRecord("k:daily", 10, Now.AddHours(-2)) };

        Assert.Equal(new[] { "Daily", "Old" }, UsageRanking.Frequent(entries, records, NoneHidden, Now, 10).Select(e => e.Name));
    }

    [Fact]
    public void Records_from_both_sources_add_up_and_count_once_per_entry()
    {
        var entries = new[] { App("Chrome", "path:chrome.exe", "file:chrome.lnk"), App("Edge", "path:edge.exe") };
        var shared = new UsageRecord("path:chrome.exe", 3, Now);
        var records = new[] { shared, new UsageRecord("file:chrome.lnk", 3, Now), new UsageRecord("path:edge.exe", 5, Now) };

        Assert.Equal(new[] { "Chrome", "Edge" }, UsageRanking.Frequent(entries, records, NoneHidden, Now, 10).Select(e => e.Name));
    }

    [Fact]
    public void Unused_hidden_and_duplicate_names_are_left_out()
    {
        var entries = new[] { App("A", "k:a"), App("A", "k:a2"), App("B", "k:b"), App("Never", "k:never"), App("Hidden", "k:h") };
        var records = new[] { new UsageRecord("k:a", 1, Now), new UsageRecord("k:a2", 9, Now), new UsageRecord("k:b", 2, Now), new UsageRecord("k:h", 99, Now) };

        var result = UsageRanking.Frequent(entries, records, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "hidden" }, Now, 10);

        Assert.Equal(new[] { "A", "B" }, result.Select(e => e.Name));
        Assert.Equal("k:a2", result[0].UsageKeys[0]);
    }

    [Fact]
    public void Max_limits_the_list() =>
        Assert.Single(UsageRanking.Frequent(new[] { App("A", "a"), App("B", "b") },
            new[] { new UsageRecord("a", 1, Now), new UsageRecord("b", 1, Now) }, NoneHidden, Now, 1));
}

public class UserAssistDecoderTests
{
    [Fact]
    public void Rot13_round_trips_and_leaves_symbols() =>
        Assert.Equal(@"{1AC14E77}\cmd.exe", UserAssistDecoder.Rot13(UserAssistDecoder.Rot13(@"{1AC14E77}\cmd.exe")));

    [Fact]
    public void Parses_windows_7_records()
    {
        var data = new byte[72];
        BitConverter.GetBytes(12).CopyTo(data, 4);
        var last = new DateTime(2026, 10, 20, 8, 30, 0, DateTimeKind.Utc);
        BitConverter.GetBytes(last.ToFileTimeUtc()).CopyTo(data, 60);

        var entry = UserAssistDecoder.Parse(UserAssistDecoder.Rot13("Chrome"), data);

        Assert.Equal(new UserAssistEntry("Chrome", 12, last), entry);
    }

    [Fact]
    public void Skips_bookkeeping_and_never_run_records()
    {
        Assert.Null(UserAssistDecoder.Parse(UserAssistDecoder.Rot13("UEME_CTLSESSION"), new byte[72]));
        Assert.Null(UserAssistDecoder.Parse(UserAssistDecoder.Rot13("Chrome"), new byte[72]));
        Assert.Null(UserAssistDecoder.Parse(UserAssistDecoder.Rot13("Chrome"), new byte[3]));
    }

    [Theory]
    [InlineData("Chrome", @"shell:AppsFolder\Chrome")]
    [InlineData(@"{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\cmd.exe", @"{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\cmd.exe")]
    [InlineData(@"C:\Tools\a.exe", @"C:\Tools\a.exe")]
    public void Ids_open_through_apps_folder(string name, string expected) => Assert.Equal(expected, UserAssistDecoder.TargetFor(name));
}

public class GridNavigationTests
{
    // 7 items in 3 columns:  0 1 2 / 3 4 5 / 6
    [Theory]
    [InlineData(0, GridMove.Left, 0)]
    [InlineData(2, GridMove.Right, 3)]
    [InlineData(6, GridMove.Right, 6)]
    [InlineData(4, GridMove.Up, 1)]
    [InlineData(1, GridMove.Up, 1)]
    [InlineData(1, GridMove.Down, 4)]
    [InlineData(4, GridMove.Down, 6)]
    [InlineData(6, GridMove.Down, 6)]
    [InlineData(4, GridMove.First, 0)]
    [InlineData(0, GridMove.Last, 6)]
    public void Moves_in_two_dimensions(int from, GridMove move, int expected) => Assert.Equal(expected, GridNavigation.Move(from, 7, 3, move));

    [Fact]
    public void Empty_grid_has_no_selection() => Assert.Equal(-1, GridNavigation.Move(0, 0, 3, GridMove.Down));
}

public sealed class UsageStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "windock-usage-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTime Now = new(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Records_and_hidden_names_survive_a_reload()
    {
        var store = new UsageStore(_dir, NullLog.Instance);
        store.Record("path:a.exe", Now.AddDays(-1));
        store.Record("path:a.exe", Now);
        store.Hide("Notepad");

        var again = new UsageStore(_dir, NullLog.Instance);

        Assert.Equal(new UsageRecord("path:a.exe", 2, Now), Assert.Single(again.Records));
        Assert.Contains("notepad", again.HiddenNames);
        Assert.False(File.Exists(Path.Combine(_dir, "usage.json.tmp")));
    }

    [Fact]
    public void Clear_empties_everything()
    {
        var store = new UsageStore(_dir, NullLog.Instance);
        store.Record("k", Now);
        store.Hide("x");
        store.Clear();

        var again = new UsageStore(_dir, NullLog.Instance);
        Assert.Empty(again.Records);
        Assert.Empty(again.HiddenNames);
    }

    [Fact]
    public void Broken_file_starts_empty()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "usage.json"), "{{{", Encoding.UTF8);

        Assert.Empty(new UsageStore(_dir, NullLog.Instance).Records);
    }
}
