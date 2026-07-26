using SoftwareCrawler;

namespace SoftwareCrawler.Tests;

/// <summary>
/// When the list changed on disk while the app held its own edits, neither side
/// may be thrown away silently: the app can reload, but a text editor cannot undo.
/// </summary>
public class ListMergeTests
{
    private static SoftwareItem Item(string name, string webPage = "", bool enabled = true) =>
        new()
        {
            Name = name,
            WebPage = webPage,
            Enabled = enabled,
        };

    private static Dictionary<string, (string Data, string Extra)> BaselineOf(
        params SoftwareItem[] items
    ) =>
        items.ToDictionary(
            item => item.Name,
            item => (
                item.ToDataLine(SoftwareItem.DataProperties),
                item.ToDataLine(SoftwareItem.ExtraProperties)
            ),
            StringComparer.OrdinalIgnoreCase
        );

    [Fact]
    public void AnOutsideEditToAnUntouchedRowIsKept()
    {
        var baseline = BaselineOf(Item("A", "https://old"), Item("B"));
        var local = new[] { Item("A", "https://old"), Item("B") };
        var disk = new List<SoftwareItem> { Item("A", "https://edited-outside"), Item("B") };

        var applied = SoftwareManager.ApplyLocalEdits(baseline, local, disk);

        Assert.Empty(applied);
        Assert.Equal("https://edited-outside", disk[0].WebPage);
    }

    [Fact]
    public void AnEditMadeInTheAppIsKept()
    {
        var baseline = BaselineOf(Item("A", "https://old"));
        var local = new[] { Item("A", "https://edited-in-app") };
        var disk = new List<SoftwareItem> { Item("A", "https://old") };

        var applied = SoftwareManager.ApplyLocalEdits(baseline, local, disk);

        Assert.Equal(["A"], applied);
        Assert.Equal("https://edited-in-app", disk[0].WebPage);
    }

    /// <summary>The case the whole merge exists for: both sides edited, different rows.</summary>
    [Fact]
    public void EachSideKeepsItsOwnRow()
    {
        var baseline = BaselineOf(Item("A", "https://a"), Item("B", "https://b"));
        var local = new[] { Item("A", "https://a-from-app"), Item("B", "https://b") };
        var disk = new List<SoftwareItem>
        {
            Item("A", "https://a"),
            Item("B", "https://b-from-editor"),
        };

        SoftwareManager.ApplyLocalEdits(baseline, local, disk);

        Assert.Equal("https://a-from-app", disk[0].WebPage);
        Assert.Equal("https://b-from-editor", disk[1].WebPage);
    }

    [Fact]
    public void TheAppWinsWhenBothChangedTheSameRow()
    {
        var baseline = BaselineOf(Item("A", "https://old"));
        var local = new[] { Item("A", "https://from-app") };
        var disk = new List<SoftwareItem> { Item("A", "https://from-editor") };

        SoftwareManager.ApplyLocalEdits(baseline, local, disk);

        Assert.Equal("https://from-app", disk[0].WebPage);
    }

    /// <summary>Per-machine columns merge on the same terms as the shared ones.</summary>
    [Fact]
    public void EnabledFlagsMergeIndependentlyOfTheRecipe()
    {
        var baseline = BaselineOf(Item("A", "https://a"), Item("B", "https://b"));
        var local = new[] { Item("A", "https://a", enabled: false), Item("B", "https://b") };
        var disk = new List<SoftwareItem>
        {
            Item("A", "https://a-from-editor"),
            Item("B", "https://b"),
        };

        SoftwareManager.ApplyLocalEdits(baseline, local, disk);

        Assert.False(disk[0].Enabled);
        Assert.Equal("https://a-from-editor", disk[0].WebPage);
    }

    [Fact]
    public void ARowAddedInTheAppIsAdded()
    {
        var baseline = BaselineOf(Item("A"));
        var local = new[] { Item("A"), Item("New") };
        var disk = new List<SoftwareItem> { Item("A") };

        var applied = SoftwareManager.ApplyLocalEdits(baseline, local, disk);

        Assert.Equal(["+New"], applied);
        Assert.Equal(["A", "New"], disk.Select(item => item.Name));
    }

    [Fact]
    public void ARowDeletedInTheAppIsDeleted()
    {
        var baseline = BaselineOf(Item("A"), Item("B"));
        var local = new[] { Item("A") };
        var disk = new List<SoftwareItem> { Item("A"), Item("B") };

        var applied = SoftwareManager.ApplyLocalEdits(baseline, local, disk);

        Assert.Equal(["-B"], applied);
        Assert.Equal(["A"], disk.Select(item => item.Name));
    }

    [Fact]
    public void ARowAddedOutsideIsKept()
    {
        var baseline = BaselineOf(Item("A"));
        var local = new[] { Item("A") };
        var disk = new List<SoftwareItem> { Item("A"), Item("AddedOutside") };

        SoftwareManager.ApplyLocalEdits(baseline, local, disk);

        Assert.Equal(["A", "AddedOutside"], disk.Select(item => item.Name));
    }

    /// <summary>A row somebody deleted in an editor does not come back.</summary>
    [Fact]
    public void ARowDeletedOutsideStaysDeleted()
    {
        var baseline = BaselineOf(Item("A"), Item("B", "https://b"));
        var local = new[] { Item("A"), Item("B", "https://edited-in-app") };
        var disk = new List<SoftwareItem> { Item("A") };

        SoftwareManager.ApplyLocalEdits(baseline, local, disk);

        Assert.Equal(["A"], disk.Select(item => item.Name));
    }

    [Fact]
    public void TheAppAddingARowThatAlsoAppearedOutsideDoesNotDuplicateIt()
    {
        var baseline = BaselineOf(Item("A"));
        var local = new[] { Item("A"), Item("Same") };
        var disk = new List<SoftwareItem> { Item("A"), Item("Same") };

        SoftwareManager.ApplyLocalEdits(baseline, local, disk);

        Assert.Equal(["A", "Same"], disk.Select(item => item.Name));
    }

    /// <summary>Order follows disk; the app's own reordering is what a merge cannot keep.</summary>
    [Fact]
    public void OrderComesFromDisk()
    {
        var baseline = BaselineOf(Item("A"), Item("B"), Item("C"));
        var local = new[] { Item("C"), Item("B"), Item("A") };
        var disk = new List<SoftwareItem> { Item("B"), Item("A"), Item("C") };

        SoftwareManager.ApplyLocalEdits(baseline, local, disk);

        Assert.Equal(["B", "A", "C"], disk.Select(item => item.Name));
    }
}
