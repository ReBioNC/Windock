using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace Windock.Core.Apps;

/// Packaged (Store/UWP) apps cannot be started by their exe; they are started through their AUMID
/// (PackageFamilyName!AppId). This works the AUMID out from the package's AppxManifest.xml.
public static class PackageIdentity
{
    private const string Base32 = "0123456789abcdefghjkmnpqrstvwxyz";

    /// The 13-character publisher id in a package family name: Crockford base32 of the first 8 bytes of
    /// SHA-256 over the UTF-16 publisher string (e.g. "CN=Microsoft Corporation, …" → 8wekyb3d8bbwe).
    public static string PublisherId(string publisher)
    {
        var hash = SHA256.HashData(Encoding.Unicode.GetBytes(publisher));
        ulong bits = 0;
        for (var i = 0; i < 8; i++) bits = (bits << 8) | hash[i];
        var sb = new StringBuilder(13);
        // 64 bits padded with one zero bit to 65 = 13 groups of 5.
        for (var group = 0; group < 13; group++)
        {
            var shift = 64 - 5 * (group + 1);
            var value = shift >= 0 ? (int)((bits >> shift) & 0x1F) : (int)((bits << 1) & 0x1F);
            sb.Append(Base32[value]);
        }
        return sb.ToString();
    }

    /// AUMID of the manifest's application whose Executable is exeRelativePath (relative to the package folder), or null.
    public static string? Aumid(string manifestXml, string exeRelativePath)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Parse(manifestXml);
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
        var identity = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Identity");
        var name = (string?)identity?.Attribute("Name");
        var publisher = (string?)identity?.Attribute("Publisher");
        if (name is null || publisher is null) return null;
        var wanted = Normalize(exeRelativePath);
        var app = doc.Descendants()
            .Where(e => e.Name.LocalName == "Application")
            .FirstOrDefault(e => (string?)e.Attribute("Executable") is { } exe && Normalize(exe) == wanted);
        var appId = (string?)app?.Attribute("Id");
        return appId is null ? null : $"{name}_{PublisherId(publisher)}!{appId}";
    }

    private static string Normalize(string path) => path.Replace('/', '\\').TrimStart('\\').ToLowerInvariant();
}
