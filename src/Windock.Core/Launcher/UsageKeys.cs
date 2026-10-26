using Windock.Core.Apps;

namespace Windock.Core.Launcher;

/// Keys that tie one app's launches together, whichever way it was opened: the dock's AppId values
/// ("path:…exe", "aumid:…") plus "file:…" for shortcuts and other files. Windows' launch history, Windock's own
/// launches and launcher entries all go through ForTarget, so the same app gets the same keys.
public static class UsageKeys
{
    public const string AppsFolderPrefix = @"shell:AppsFolder\";

    /// target: a path, "{KnownFolderGuid}\rest\of\path", or shell:AppsFolder\<id>.
    /// knownFolder resolves a Known Folder id to its path (null when unknown).
    public static IReadOnlyList<string> ForTarget(string target, Func<Guid, string?> knownFolder)
    {
        var keys = new List<string>();
        var t = target.Trim();
        if (t.StartsWith(AppsFolderPrefix, StringComparison.OrdinalIgnoreCase))
        {
            t = t[AppsFolderPrefix.Length..];
            if (ExpandPath(t, knownFolder) is null && t.Length > 0) keys.Add(AppId.FromAumid(t).Value);
        }
        if (ExpandPath(t, knownFolder) is { } path)
        {
            try
            {
                keys.Add(path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    ? AppId.FromExePath(path).Value
                    : "file:" + Path.GetFullPath(path).ToLowerInvariant());
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { }
        }
        return keys;
    }

    /// "{1AC14E77-…}\cmd.exe" → "C:\Windows\System32\cmd.exe"; a full path stays; anything else (an AUMID) → null.
    public static string? ExpandPath(string text, Func<Guid, string?> knownFolder)
    {
        if (text.Length > 38 && text[0] == '{' && text[37] == '}' && Guid.TryParse(text.AsSpan(0, 38), out var id))
            return knownFolder(id) is { } folder ? Path.Combine(folder, text[38..].TrimStart('\\')) : null;
        return text.Length > 2 && Path.IsPathFullyQualified(text) ? text : null;
    }
}
