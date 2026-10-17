using Windock.Core.Apps;
using Windock.Core.Settings;

namespace Windock.Core.Tests.Apps;

public class PinListEditorTests
{
    private static readonly PinEntry A = new(@"C:\Apps\A.exe"), B = new(@"C:\Apps\B.exe"), C = new(@"C:\Apps\C.exe");

    private static AppId IdOf(PinEntry p) => AppId.FromExePath(p.Path);

    private static string[] Paths(IReadOnlyList<PinEntry> pins) => pins.Select(p => p.Path).ToArray();

    [Theory]
    [InlineData(0, new[] { "C", "A", "B" })]
    [InlineData(1, new[] { "A", "C", "B" })]
    [InlineData(2, new[] { "A", "B", "C" })]
    [InlineData(99, new[] { "A", "B", "C" })]
    [InlineData(-5, new[] { "C", "A", "B" })]
    public void Insert_places_new_pin_at_index(int index, string[] expected)
    {
        var result = PinListEditor.Insert(new[] { A, B }, C, index, IdOf);

        Assert.Equal(expected.Select(n => $@"C:\Apps\{n}.exe"), Paths(result));
    }

    [Fact]
    public void Insert_of_existing_app_moves_it_instead_of_duplicating()
    {
        var result = PinListEditor.Insert(new[] { A, B, C }, new PinEntry(@"c:\apps\c.EXE"), 0, IdOf);

        Assert.Equal(new[] { C.Path, A.Path, B.Path }, Paths(result));
    }

    [Theory]
    [InlineData(0, 2, new[] { "B", "A", "C" })]   // index counts gaps before removal: drop A after B
    [InlineData(0, 3, new[] { "B", "C", "A" })]
    [InlineData(2, 0, new[] { "C", "A", "B" })]
    [InlineData(1, 1, new[] { "A", "B", "C" })]
    [InlineData(1, 2, new[] { "A", "B", "C" })]
    public void Move_uses_insertion_index_in_the_original_list(int from, int to, string[] expected)
    {
        var result = PinListEditor.Move(new[] { A, B, C }, from, to);

        Assert.Equal(expected.Select(n => $@"C:\Apps\{n}.exe"), Paths(result));
    }

    [Fact]
    public void Remove_drops_every_pin_of_that_app()
    {
        var result = PinListEditor.Remove(new[] { A, B, new PinEntry(@"c:\apps\a.exe") }, AppId.FromExePath(A.Path), IdOf);

        Assert.Equal(new[] { B.Path }, Paths(result));
    }

    [Theory]
    [InlineData(@"C:\x\Game.exe", true)]
    [InlineData(@"C:\x\Game.LNK", true)]
    [InlineData(@"shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", true)]
    [InlineData(@"C:\x\notes.txt", false)]
    [InlineData(@"C:\x\folder", false)]
    [InlineData("", false)]
    public void CanPin_accepts_programs_and_shortcuts_only(string path, bool expected)
    {
        Assert.Equal(expected, PinListEditor.CanPin(path));
    }
}
