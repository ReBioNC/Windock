using Windock.Core.Apps;

namespace Windock.Core.Tests.Apps;

public class PackageIdentityTests
{
    [Theory]
    [InlineData("CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US", "8wekyb3d8bbwe")]
    [InlineData("CN=Microsoft Windows, O=Microsoft Corporation, L=Redmond, S=Washington, C=US", "cw5n1h2txyewy")]
    public void Publisher_ids_match_windows(string publisher, string expected) => Assert.Equal(expected, PackageIdentity.PublisherId(publisher));

    private const string SettingsManifest = """
        <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10">
          <Identity Name="windows.immersivecontrolpanel" Publisher="CN=Microsoft Windows, O=Microsoft Corporation, L=Redmond, S=Washington, C=US" Version="10.0.2.1000" />
          <Applications>
            <Application Id="microsoft.windows.immersivecontrolpanel" Executable="SystemSettings.exe" />
          </Applications>
        </Package>
        """;

    [Fact]
    public void Finds_the_aumid_of_an_exe() =>
        Assert.Equal("windows.immersivecontrolpanel_cw5n1h2txyewy!microsoft.windows.immersivecontrolpanel",
            PackageIdentity.Aumid(SettingsManifest, "systemsettings.exe"));

    [Fact]
    public void Unknown_exe_or_bad_xml_gives_null()
    {
        Assert.Null(PackageIdentity.Aumid(SettingsManifest, "Other.exe"));
        Assert.Null(PackageIdentity.Aumid("<not xml", "SystemSettings.exe"));
    }

    [Fact]
    public void Executable_in_a_subfolder_matches()
    {
        var xml = SettingsManifest.Replace("Executable=\"SystemSettings.exe\"", "Executable=\"app/Tool.exe\"");
        Assert.EndsWith("!microsoft.windows.immersivecontrolpanel", PackageIdentity.Aumid(xml, "App\\Tool.exe"));
    }
}
