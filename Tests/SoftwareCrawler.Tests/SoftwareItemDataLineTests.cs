using SoftwareCrawler;

namespace SoftwareCrawler.Tests;

/// <summary>
/// The .tab format is the only copy of the crawl recipes and of every machine's
/// own settings, and it has to keep reading files written by older versions.
/// </summary>
public class SoftwareItemDataLineTests
{
    private static SoftwareItem CreateFullyPopulatedItem() =>
        new()
        {
            Enabled = true,
            Name = "Example",
            WebPage = "https://example.com/download",
            XPathOrScript1 = "//a[@id='download']",
            XPathOrScript2 = "//button[text()='Windows']",
            XPathOrScript3 = "",
            XPathOrScript4 = "",
            XPathOrScript5 = "",
            Frames = "outer`inner",
            WaitSecondsBeforeClick = 3,
            StartDownloadTimeout = 45,
            DownloadDirectory = @"D:\Downloads\Example",
            DownloadDirectory2 = @"\\server\share\Example",
            FilePatternToDeleteBeforeDownload = "*.exe",
            ExtractAfterDownload = true,
            FilePatternToDeleteBeforeExtractionAndExtractOnly = "*.dll",
            DirectDownload = true,
            ExtractToRoot = true,
        };

    /// <summary>
    /// Written against the property list rather than named columns, so a newly
    /// added field is covered here the moment it joins DataProperties.
    /// </summary>
    [Fact]
    public void EveryRecipeColumnSurvivesARoundTrip()
    {
        var item = CreateFullyPopulatedItem();

        var restored = new SoftwareItem();
        restored.FromDataLine(item.ToDataLine(SoftwareItem.DataProperties), SoftwareItem.DataProperties);

        foreach (var property in SoftwareItem.DataProperties)
            Assert.Equal(property.GetValue(item), property.GetValue(restored));
    }

    [Fact]
    public void EveryPerMachineColumnSurvivesARoundTrip()
    {
        var item = CreateFullyPopulatedItem();

        var restored = new SoftwareItem();
        restored.FromDataLine(
            item.ToDataLine(SoftwareItem.ExtraProperties),
            SoftwareItem.ExtraProperties
        );

        foreach (var property in SoftwareItem.ExtraProperties)
            Assert.Equal(property.GetValue(item), property.GetValue(restored));
    }

    /// <summary>A file written before a column existed must still load.</summary>
    [Fact]
    public void MissingTrailingColumnsKeepTheirDefaults()
    {
        // Name, WebPage, XPathOrScript1..5, Frames, WaitSecondsBeforeClick,
        // StartDownloadTimeout, FilePatternToDeleteBeforeDownload,
        // ExtractAfterDownload - and nothing after it.
        var line = string.Join(
            '\t',
            "Example",
            "https://example.com",
            "//a",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "*.exe",
            "true"
        );

        var item = new SoftwareItem();
        item.FromDataLine(line, SoftwareItem.DataProperties);

        Assert.Equal("Example", item.Name);
        Assert.True(item.ExtractAfterDownload);
        Assert.Equal("", item.FilePatternToDeleteBeforeExtractionAndExtractOnly);
        Assert.False(item.DirectDownload);
        Assert.False(item.ExtractToRoot);
    }

    [Fact]
    public void MoreColumnsThanPropertiesIsRejected()
    {
        var line = string.Join('\t', Enumerable.Repeat("x", SoftwareItem.DataProperties.Count + 1));

        var item = new SoftwareItem();
        Assert.Throws<Exception>(() => item.FromDataLine(line, SoftwareItem.DataProperties));
    }

    /// <summary>Builds a data line carrying one value in one named column.</summary>
    private static string LineWithOnly(string columnName, string value) =>
        string.Join(
            '\t',
            SoftwareItem.DataProperties.Select(property =>
                property.Name == columnName ? value : ""
            )
        );

    [Theory]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("1", true)]
    [InlineData("", false)]
    [InlineData("false", false)]
    [InlineData("anything else", false)]
    public void BooleansAcceptTheFormsFoundInRealFiles(string value, bool expected)
    {
        var item = new SoftwareItem();
        item.FromDataLine(
            LineWithOnly(nameof(SoftwareItem.ExtractAfterDownload), value),
            SoftwareItem.DataProperties
        );

        Assert.Equal(expected, item.ExtractAfterDownload);
    }

    [Fact]
    public void UnparsableNumbersFallBackToZero()
    {
        var item = new SoftwareItem();
        item.FromDataLine(
            LineWithOnly(nameof(SoftwareItem.WaitSecondsBeforeClick), "not a number"),
            SoftwareItem.DataProperties
        );

        Assert.Equal(0, item.WaitSecondsBeforeClick);
    }

    /// <summary>Zero and false are written as empty so the file stays readable.</summary>
    [Fact]
    public void DefaultsAreWrittenAsEmptyColumns()
    {
        var line = new SoftwareItem { Name = "Example" }.ToDataLine(SoftwareItem.DataProperties);

        Assert.Equal("Example", line.Split('\t')[0]);
        Assert.All(line.Split('\t').Skip(1), column => Assert.Equal("", column));
    }

    /// <summary>
    /// Scripts are multi-line but a row is one line, so newlines travel as the
    /// literal `n and must come back as newlines.
    /// </summary>
    [Fact]
    public void ScriptNewlinesSurviveTheSingleLineFormat()
    {
        var scripts = new List<string>
        {
            "const a = 1;\nconst b = 2;\nclick(a, b);",
            "//a[@id='second']",
        };

        var item = new SoftwareItem();
        item.SetXPathOrScripts(scripts);

        Assert.DoesNotContain('\n', item.XPathOrScript1);
        Assert.Contains("`n", item.XPathOrScript1);

        var restored = new SoftwareItem();
        restored.FromDataLine(item.ToDataLine(SoftwareItem.DataProperties), SoftwareItem.DataProperties);

        Assert.Equal(scripts, restored.GetXPathOrScripts());
    }

    /// <summary>
    /// An external editor indenting a script with tabs used to shift every later
    /// column of the row on the next save.
    /// </summary>
    [Fact]
    public void TabsInAScriptCannotShiftTheColumns()
    {
        var script = "function download() {\n\tconst a = 1;\n\tclick(a);\n}";

        var item = new SoftwareItem { Name = "Example" };
        item.SetXPathOrScripts([script]);
        var line = item.ToDataLine(SoftwareItem.DataProperties);

        Assert.Equal(SoftwareItem.DataProperties.Count, line.Split('\t').Length);

        var restored = new SoftwareItem();
        restored.FromDataLine(line, SoftwareItem.DataProperties);

        Assert.Equal(script, restored.GetXPathOrScripts().Single());
    }

    /// <summary>Any column can pick one up from a paste, not just the script ones.</summary>
    [Theory]
    [InlineData("tab\there")]
    [InlineData("newline\nhere")]
    [InlineData("carriage\r\nreturn")]
    [InlineData("lone\rcarriage")]
    public void ControlCharactersInAnyColumnCannotBreakTheRow(string value)
    {
        var item = new SoftwareItem { Name = value, DownloadDirectory = value };

        var dataLine = item.ToDataLine(SoftwareItem.DataProperties);
        var extraLine = item.ToDataLine(SoftwareItem.ExtraProperties);

        Assert.Equal(SoftwareItem.DataProperties.Count, dataLine.Split('\t').Length);
        Assert.Equal(SoftwareItem.ExtraProperties.Count, extraLine.Split('\t').Length);
        Assert.DoesNotContain('\n', dataLine);
        Assert.DoesNotContain('\r', dataLine);
        Assert.DoesNotContain('\n', extraLine);
        Assert.DoesNotContain('\r', extraLine);
    }

    /// <summary>
    /// The escaped form is what the file has always held and what the grid shows,
    /// so writing a value that already carries it must not double-escape.
    /// </summary>
    [Fact]
    public void AlreadyEscapedValuesAreWrittenUnchanged()
    {
        var item = new SoftwareItem { XPathOrScript1 = "const a = 1;`nclick(a);" };

        var line = item.ToDataLine(SoftwareItem.DataProperties);

        Assert.Contains("const a = 1;`nclick(a);", line);
        Assert.DoesNotContain("``n", line);
    }

    [Fact]
    public void SettingFewerScriptsClearsTheRemainingSlots()
    {
        var item = CreateFullyPopulatedItem();

        item.SetXPathOrScripts(["//a[@id='only']"]);

        Assert.Equal("//a[@id='only']", item.XPathOrScript1);
        Assert.Equal("", item.XPathOrScript2);
        Assert.Single(item.GetXPathOrScripts());
    }

    [Fact]
    public void CloneCopiesSettingsButNotRuntimeState()
    {
        var item = CreateFullyPopulatedItem();
        item.Status = DownloadingStatus.Failed;

        var clone = item.Clone();

        foreach (var property in SoftwareItem.DataProperties.Concat(SoftwareItem.ExtraProperties))
            Assert.Equal(property.GetValue(item), property.GetValue(clone));

        Assert.Equal(DownloadingStatus.Idle, clone.Status);
        Assert.Equal("", clone.ErrorMessage);
        Assert.Equal("", clone.Progress);
    }

    /// <summary>
    /// Files that still carry Enabled as the first shared column are read with
    /// the layout they were written in.
    /// </summary>
    [Fact]
    public void TheLayoutThatStillHadEnabledFirstStillLoads()
    {
        var line = string.Join('\t', "true", "Example", "https://example.com", "//a");

        var item = new SoftwareItem();
        item.FromDataLine(line, SoftwareItem.LegacyDataProperties);

        Assert.True(item.Enabled);
        Assert.Equal("Example", item.Name);
        Assert.Equal("https://example.com", item.WebPage);
        Assert.Equal("//a", item.XPathOrScript1);
    }

    [Fact]
    public void TheHeaderNamesTheColumnsInOrder()
    {
        var header = SoftwareItem.GetDataHeaderLine(SoftwareItem.DataProperties);

        Assert.Equal(
            SoftwareItem.DataProperties.Select(property => property.Name),
            header.Split('\t')
        );
    }
}
