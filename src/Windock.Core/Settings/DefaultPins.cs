namespace Windock.Core.Settings;

/// First-run pins: File Explorer plus common apps that are actually installed (DESIGN.md §6).
public static class DefaultPins
{
    private static readonly string[] Shortcuts =
    {
        "Google Chrome.lnk", "Microsoft Edge.lnk", @"Discord Inc\Discord.lnk", @"Steam\Steam.lnk", "Spotify.lnk",
    };

    public static IReadOnlyList<PinEntry> Detect(Func<string, bool> fileExists, string windowsDir, string commonStartMenuPrograms, string userStartMenuPrograms)
    {
        var pins = new List<PinEntry>();
        var explorer = Path.Combine(windowsDir, "explorer.exe");
        if (fileExists(explorer)) pins.Add(new PinEntry(explorer));

        foreach (var shortcut in Shortcuts)
        {
            var found = new[] { userStartMenuPrograms, commonStartMenuPrograms }
                .Select(root => Path.Combine(root, shortcut))
                .FirstOrDefault(fileExists);
            if (found is not null) pins.Add(new PinEntry(found));
        }
        return pins;
    }
}
