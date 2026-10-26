using System.Text;

namespace Windock.Core.Launcher;

/// Launcher tabs. Windows keeps no app categories, so AppCategorizer guesses them.
public enum LauncherCategory { Games, Development, Internet, Media, Office, System, Other }

/// The user's own choice for one app, from right-click › Move to category (matched by display name).
public sealed record LauncherCategoryOverride(string Name, LauncherCategory Category);

/// Guesses an app's category: the user's choice, then where it is installed (game libraries), its Start Menu
/// folder, Store app ids, then a list of well-known apps. Anything else is Other.
public static class AppCategorizer
{
    private static readonly string[] GamePaths =
    {
        @"\steamapps\", @"\epic games\", @"\riot games\", @"\xboxgames\", @"\gog games\", @"\gog galaxy\games\",
        @"\ubisoft game launcher\games\", @"\ea games\", @"\games\",
    };

    private static readonly string[] GameFolders = { "steam", "epic games", "games", "riot games", "ubisoft", "ea", "gog.com" };

    private static readonly (string Prefix, LauncherCategory Category)[] AumidHints =
    {
        ("microsoft.xbox", LauncherCategory.Games), ("microsoft.gaming", LauncherCategory.Games), ("microsoft.minecraft", LauncherCategory.Games),
        ("microsoft.microsoftsolitaire", LauncherCategory.Games), ("microsoft.zune", LauncherCategory.Media), ("microsoft.office", LauncherCategory.Office),
        ("microsoft.windows", LauncherCategory.System),
    };

    /// Checked in this order, so "Windows Media Player" is Media before anything looks at System.
    private static readonly (LauncherCategory Category, string[] Words)[] KnownApps =
    {
        (LauncherCategory.Games, new[]
        {
            "steam", "epic games", "epic games launcher", "riot client", "valorant", "league of legends", "minecraft", "roblox", "growtopia",
            "battle net", "ubisoft connect", "ea", "ea app", "origin", "gog galaxy", "xbox", "genshin impact", "honkai", "osu", "fortnite",
            "counter strike", "dota 2", "playnite", "heroic", "itch", "solitaire", "game bar", "rockstar games launcher", "geforce now",
        }),
        (LauncherCategory.Development, new[]
        {
            "visual studio", "visual studio code", "vs code", "code", "code oss", "cursor", "antigravity", "android studio", "intellij idea",
            "pycharm", "webstorm", "rider", "clion", "goland", "datagrip", "git", "git bash", "git gui", "github desktop", "sourcetree",
            "postman", "insomnia", "docker desktop", "wsl", "ubuntu", "powershell", "windows powershell", "windows terminal", "terminal",
            "command prompt", "cmd", "node js", "python", "idle", "unity", "unity hub", "unreal engine", "godot", "notepad++", "sublime text",
            "eclipse", "netbeans", "dbeaver", "mysql workbench", "putty", "winscp", "app cert kit", "windows kits", "sdk", "dotnet",
        }),
        (LauncherCategory.Media, new[]
        {
            "spotify", "vlc", "vlc media player", "obs studio", "obs", "windows media player", "media player", "movies tv", "photos",
            "photoshop", "premiere pro", "after effects", "illustrator", "lightroom", "adobe express", "adobe express photos", "gimp", "krita",
            "blender", "audacity", "capcut", "davinci resolve", "handbrake", "netflix", "youtube", "crunchyroll", "paint", "paint 3d",
            "canva", "figma", "itunes", "groove music", "foobar2000", "musicbee", "shotcut", "kdenlive", "clipchamp", "camera", "inkscape",
        }),
        (LauncherCategory.Office, new[]
        {
            "word", "excel", "powerpoint", "onenote", "outlook", "access", "publisher", "acrobat", "adobe acrobat", "acrobat reader",
            "libreoffice", "notion", "obsidian", "evernote", "microsoft to do", "to do", "calendar", "mail", "grammarly", "wps office",
            "foxit pdf reader", "claude", "chatgpt", "copilot", "sticky notes", "teams",
        }),
        (LauncherCategory.Internet, new[]
        {
            "chrome", "google chrome", "firefox", "microsoft edge", "edge", "brave", "opera", "opera gx", "vivaldi", "tor browser", "discord",
            "whatsapp", "telegram", "telegram desktop", "slack", "zoom", "skype", "signal", "messenger", "thunderbird", "proton vpn",
            "proton mail", "proton drive", "cloudflare warp", "cloudflare one client", "filezilla", "qbittorrent", "utorrent", "line",
            "wechat", "onedrive", "google drive", "dropbox", "nordvpn",
        }),
        (LauncherCategory.System, new[]
        {
            "settings", "control panel", "task manager", "file explorer", "explorer", "registry editor", "device manager",
            "disk management", "event viewer", "services", "resource monitor", "system information", "windows security", "snipping tool",
            "snip sketch", "notepad", "calculator", "character map", "magnifier", "narrator", "on screen keyboard", "remote desktop connection",
            "windows update", "amd software", "nvidia control panel", "intel graphics command center", "realtek audio console",
            "armoury crate", "myasus", "windock", "windock settings", "7 zip", "7 zip file manager", "winrar", "ccleaner", "revo uninstaller",
            "everything", "powertoys", "windhawk", "disk cleanup", "defragment and optimize drives", "computer management",
            "windows tools", "administrative tools", "windows backup", "recovery drive", "system configuration", "task scheduler",
            "performance monitor", "print management", "windows fax and scan", "quick assist", "steps recorder", "get help", "tips",
        }),
    };

    private static readonly string[] ClutterWords = { "uninstall", "uninstaller", "readme", "read me", "help", "documentation", "manual", "release notes", "license", "website", "what s new", "changelog" };

    /// Start Menu extras that are not apps (Uninstall X, Readme, Help…): left out of the grid, still searchable.
    public static bool IsClutter(string name)
    {
        var words = Words(name);
        return ClutterWords.Any(w => words.Contains(Words(w), StringComparison.Ordinal));
    }

    public static LauncherCategory Categorize(string name, string target, string? exePath, IReadOnlyList<LauncherCategoryOverride>? overrides = null)
    {
        if (overrides is not null)
            foreach (var o in overrides)
                if (string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase)) return o.Category;

        var t = target.ToLowerInvariant();
        if (t.StartsWith("steam://", StringComparison.Ordinal) || t.StartsWith("com.epicgames.launcher://", StringComparison.Ordinal)) return LauncherCategory.Games;
        foreach (var path in new[] { t, exePath?.ToLowerInvariant() })
            if (path is not null && GamePaths.Any(p => path.Contains(p, StringComparison.Ordinal))) return LauncherCategory.Games;

        if (StartMenuFolder(t) is { } folder && GameFolders.Contains(folder)) return LauncherCategory.Games;

        var words = Words(name);
        var exeWords = exePath is null ? null : Words(Path.GetFileNameWithoutExtension(exePath));
        foreach (var (category, known) in KnownApps)
            foreach (var word in known)
            {
                var w = Words(word);
                if (words == w || words.Contains(w, StringComparison.Ordinal) || exeWords == w) return category;
            }

        const string appsFolder = @"shell:appsfolder\";
        if (t.StartsWith(appsFolder, StringComparison.Ordinal))
        {
            var aumid = t[appsFolder.Length..];
            foreach (var (prefix, category) in AumidHints)
                if (aumid.StartsWith(prefix, StringComparison.Ordinal)) return category;
        }
        return LauncherCategory.Other;
    }

    /// " visual studio code " — lower case, letters and digits only, padded so " code " matches whole words.
    private static string Words(string text)
    {
        var sb = new StringBuilder(text.Length + 2).Append(' ');
        var space = true;
        foreach (var c in LauncherSearch.Fold(text))
        {
            if (char.IsLetterOrDigit(c)) { sb.Append(c); space = false; }
            else if (!space) { sb.Append(' '); space = true; }
        }
        if (!space) sb.Append(' ');
        return sb.ToString();
    }

    /// The Start Menu subfolder a shortcut sits in ("steam" for …\Programs\Steam\Game.url), or null.
    private static string? StartMenuFolder(string loweredPath)
    {
        const string programs = @"\start menu\programs\";
        var at = loweredPath.IndexOf(programs, StringComparison.Ordinal);
        if (at < 0) return null;
        var rest = loweredPath[(at + programs.Length)..];
        var slash = rest.IndexOf('\\');
        return slash > 0 ? rest[..slash] : null;
    }
}
