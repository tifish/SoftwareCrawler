using SoftwareCrawler;

namespace SoftwareCrawler.Tests;

/// <summary>
/// Five slots become one file an external editor opens, and have to come back
/// as the same five. What the editor does to line endings and to the end of the
/// file on the way through must not reach the stored recipe.
/// </summary>
public class ScriptEditSessionTests
{
    [Fact]
    public void SlotsSurviveAJoinAndSplitUntouched()
    {
        List<string> scripts = ["//a[@id='x']", "document.querySelector('a').click();"];

        Assert.Equal(scripts, ScriptEditSession.Split(ScriptEditSession.Join(scripts)));
    }

    [Fact]
    public void TheTrailingNewlineWrittenForTheEditorIsNotReadBack()
    {
        List<string> scripts = ["//a", "//b"];

        // WriteAsync appends this; without the trim it would become part of the
        // last slot and change the recipe just by opening the editor.
        var edited = ScriptEditSession.Join(scripts) + "\n";

        Assert.Equal(scripts, ScriptEditSession.Split(edited));
    }

    [Fact]
    public void AnEditorThatSavesWithCrLfDoesNotChangeTheRecipe()
    {
        List<string> scripts = ["line one\nline two", "//b"];

        var edited = ScriptEditSession.Join(scripts).Replace("\n", "\r\n") + "\r\n";

        Assert.Equal(scripts, ScriptEditSession.Split(edited));
    }

    [Fact]
    public void ASingleScriptWithNoSeparatorComesBackAsOneSlot()
    {
        Assert.Equal(["//a"], ScriptEditSession.Split("//a\n"));
    }

    [Fact]
    public void AnEmptiedFileComesBackAsOneEmptySlot()
    {
        // SetXPathOrScripts blanks the slots past what it is given, so this is
        // what clearing the whole file means: no scripts at all.
        Assert.Equal([""], ScriptEditSession.Split("   \n\n"));
    }

    [Fact]
    public void NamesThatCannotBeFileNamesAreStillGivenOne()
    {
        Assert.Equal("VC Runtime x86_x64", ScriptEditSession.SanitizeFileName("VC Runtime x86/x64"));
        Assert.Equal("C__Tools", ScriptEditSession.SanitizeFileName("C:\\Tools"));
    }

    [Fact]
    public void ANameThatIsAlreadyValidIsLeftAlone()
    {
        Assert.Equal("Notepad++", ScriptEditSession.SanitizeFileName("Notepad++"));
        Assert.Equal("搜狗输入法", ScriptEditSession.SanitizeFileName("搜狗输入法"));
    }

    [Fact]
    public void TheFileIsNamedAfterTheItemAndLandsInTheTempFolder()
    {
        var session = new ScriptEditSession(new SoftwareItem { Name = "VC Runtime x86/x64" });

        Assert.Equal("VC Runtime x86_x64.js", Path.GetFileName(session.FilePath));
        Assert.Equal(
            Path.Join(Path.GetTempPath(), "SoftwareCrawler"),
            Path.GetDirectoryName(session.FilePath)
        );
    }

    [Fact]
    public async Task AnItemRoundTripsThroughTheFileUnchanged()
    {
        var item = new SoftwareItem
        {
            Name = "round-trip test",
            XPathOrScript1 = "//a[text()='下载']",
            XPathOrScript2 = "document.querySelector('a').click();",
        };
        var before = item.GetXPathOrScripts();

        var session = new ScriptEditSession(item);
        try
        {
            await session.WriteAsync();
            Assert.True(session.HasUnappliedFile);

            // Stand in for the editor: save it back with Windows line endings.
            var edited = await File.ReadAllTextAsync(session.FilePath);
            await File.WriteAllTextAsync(session.FilePath, edited.Replace("\n", "\r\n"));

            Assert.True(await session.ApplyAsync());
        }
        finally
        {
            session.Discard();
        }

        Assert.Equal(before, item.GetXPathOrScripts());
        // Applying clears the file, so the next session does not ask about it.
        Assert.False(session.HasUnappliedFile);
    }
}
