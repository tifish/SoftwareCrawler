using SoftwareCrawler;

namespace SoftwareCrawler.Tests;

/// <summary>
/// FilePatternToDeleteBeforeDownload wipes files the app did not create, so it
/// is the one place a configuration mistake can destroy someone's data.
/// </summary>
public class OldVersionDeletionTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(),
        "SoftwareCrawler.Tests",
        Guid.NewGuid().ToString("N")
    );

    public OldVersionDeletionTests()
    {
        Directory.CreateDirectory(_folder);
        SoftwareManager.Items.Clear();
    }

    public void Dispose()
    {
        SoftwareManager.Items.Clear();
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch
        {
            // A leftover temp folder is not worth failing a test over.
        }
        GC.SuppressFinalize(this);
    }

    private string CreateFile(string name)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllText(path, "x");
        return path;
    }

    private static string[] NoOtherPatterns => [];

    private static string[] Names(IReadOnlyList<SoftwareItem> items) =>
        items.Select(item => item.Name).ToArray();

    [Fact]
    public async Task OldVersionsGoAndTheNewFileStays()
    {
        var keep = CreateFile("Example-2.0.exe");
        var old = CreateFile("Example-1.0.exe");

        var deleted = await DownloadPipeline.DeleteOldVersions(_folder, "*.exe", keep, NoOtherPatterns);

        Assert.Equal([old], deleted);
        Assert.True(File.Exists(keep));
        Assert.False(File.Exists(old));
    }

    [Fact]
    public async Task FilesThePatternDoesNotMatchAreLeftAlone()
    {
        var keep = CreateFile("Example-2.0.exe");
        var unrelated = CreateFile("notes.txt");

        await DownloadPipeline.DeleteOldVersions(_folder, "*.exe", keep, NoOtherPatterns);

        Assert.True(File.Exists(unrelated));
    }

    /// <summary>
    /// Sharing a folder and telling the files apart by name is how the list is
    /// actually used - every JetBrains IDE in one folder, every CUDA release in
    /// another - so a precise pattern must still collect its own old versions.
    /// </summary>
    [Fact]
    public async Task PrecisePatternsInASharedFolderStillDeleteTheirOwn()
    {
        var keep = CreateFile("bellsoft-jdk21.0.5.exe");
        var myOldVersion = CreateFile("bellsoft-jdk21.0.4.exe");
        var theirs = CreateFile("bellsoft-jdk17.0.9.exe");

        var deleted = await DownloadPipeline.DeleteOldVersions(
            _folder,
            "bellsoft-jdk21*.exe",
            keep,
            ["bellsoft-jdk17*.exe", "bellsoft-jdk8*.exe"]
        );

        Assert.Equal([myOldVersion], deleted);
        Assert.True(File.Exists(keep));
        Assert.True(File.Exists(theirs));
    }

    /// <summary>
    /// A pattern broad enough to match another item's files does not get to
    /// delete them, which is what makes "*.exe" in a shared folder survivable.
    /// </summary>
    [Fact]
    public async Task FilesAnotherItemsPatternClaimsAreLeftAlone()
    {
        var keep = CreateFile("Example-2.0.exe");
        var myOldVersion = CreateFile("Example-1.0.exe");
        var theirs = CreateFile("CLion-2024.1.exe");

        var deleted = await DownloadPipeline.DeleteOldVersions(
            _folder,
            "*.exe",
            keep,
            ["CLion-*.exe"]
        );

        Assert.Equal([myOldVersion], deleted);
        Assert.True(File.Exists(theirs));
    }

    [Fact]
    public async Task AnotherItemsPatternIsMatchedCaseInsensitively()
    {
        var keep = CreateFile("Example-2.0.exe");
        var theirs = CreateFile("CLION-2024.1.EXE");

        var deleted = await DownloadPipeline.DeleteOldVersions(
            _folder,
            "*.exe",
            keep,
            ["clion-*.exe"]
        );

        Assert.Empty(deleted);
        Assert.True(File.Exists(theirs));
    }

    /// <summary>
    /// A pattern aimed at a general downloads folder matches far more than one
    /// item's history, which is the shape of the accident this guards against.
    /// </summary>
    [Fact]
    public async Task APatternThatSweepsUpTooMuchDeletesNothing()
    {
        var keep = CreateFile("Example-2.0.exe");
        for (var i = 0; i < 11; i++)
            CreateFile($"Unrelated-{i}.exe");

        var deleted = await DownloadPipeline.DeleteOldVersions(_folder, "*.exe", keep, NoOtherPatterns);

        Assert.Empty(deleted);
        Assert.Equal(12, Directory.GetFiles(_folder, "*.exe").Length);
    }

    /// <summary>
    /// The download in flight is staged in this very folder, and a pattern like
    /// "Example-*" would match it.
    /// </summary>
    [Fact]
    public async Task TheDownloadInFlightIsNeverDeleted()
    {
        var inFlight = CreateFile("Example-2.0.exe" + DownloadPipeline.PartialSuffix);
        var old = CreateFile("Example-1.0.exe");

        var deleted = await DownloadPipeline.DeleteOldVersions(
            _folder,
            "Example-*",
            "",
            NoOtherPatterns
        );

        Assert.Equal([old], deleted);
        Assert.True(File.Exists(inFlight));
    }

    [Fact]
    public async Task AMissingFolderIsNotAnError()
    {
        var deleted = await DownloadPipeline.DeleteOldVersions(
            Path.Combine(_folder, "does-not-exist"),
            "*.exe",
            "",
            NoOtherPatterns
        );

        Assert.Empty(deleted);
    }

    [Fact]
    public void ItemsSharingADirectoryFindEachOther()
    {
        var mine = new SoftwareItem { Name = "Mine", DownloadDirectory = _folder };
        var theirs = new SoftwareItem { Name = "Theirs", DownloadDirectory = _folder };
        SoftwareManager.Items.AddRange([mine, theirs]);

        Assert.Equal(["Theirs"], Names(SoftwareManager.OtherItemsUsingDirectory(mine, _folder)));
        Assert.Equal(["Mine"], Names(SoftwareManager.OtherItemsUsingDirectory(theirs, _folder)));
    }

    [Fact]
    public void TheSecondDownloadDirectoryCountsAsUse()
    {
        var mine = new SoftwareItem { Name = "Mine", DownloadDirectory = _folder };
        var theirs = new SoftwareItem { Name = "Theirs", DownloadDirectory2 = _folder };
        SoftwareManager.Items.AddRange([mine, theirs]);

        Assert.Equal(["Theirs"], Names(SoftwareManager.OtherItemsUsingDirectory(mine, _folder)));
    }

    /// <summary>
    /// The patterns are what the delete actually consults, and an item that
    /// deletes nothing contributes no claim on anyone else's files.
    /// </summary>
    [Fact]
    public void OnlyTheOtherItemsPatternsAreCollected()
    {
        var mine = new SoftwareItem
        {
            Name = "JDK 21",
            DownloadDirectory = _folder,
            FilePatternToDeleteBeforeDownload = "bellsoft-jdk21*.msi",
        };
        var theirs = new SoftwareItem
        {
            Name = "JDK 17",
            DownloadDirectory = _folder,
            FilePatternToDeleteBeforeDownload = "bellsoft-jdk17*.msi",
        };
        var deletesNothing = new SoftwareItem { Name = "Syncthing", DownloadDirectory = _folder };
        SoftwareManager.Items.AddRange([mine, theirs, deletesNothing]);

        Assert.Equal(
            ["bellsoft-jdk17*.msi"],
            SoftwareManager.OtherItemPatternsInDirectory(mine, _folder)
        );
    }

    [Fact]
    public void TheSameFolderSpelledDifferentlyStillCounts()
    {
        var mine = new SoftwareItem { Name = "Mine", DownloadDirectory = _folder };
        var theirs = new SoftwareItem
        {
            Name = "Theirs",
            DownloadDirectory = _folder.ToUpperInvariant() + Path.DirectorySeparatorChar,
        };
        SoftwareManager.Items.AddRange([mine, theirs]);

        Assert.Equal(["Theirs"], Names(SoftwareManager.OtherItemsUsingDirectory(mine, _folder)));
    }

    [Fact]
    public void AnItemDoesNotCollideWithItself()
    {
        var mine = new SoftwareItem { Name = "Mine", DownloadDirectory = _folder };
        SoftwareManager.Items.Add(mine);

        Assert.Empty(SoftwareManager.OtherItemsUsingDirectory(mine, _folder));
    }

    /// <summary>
    /// Items that leave the directory blank get one named after themselves, so
    /// blank must never read as "the same folder".
    /// </summary>
    [Fact]
    public void BlankDirectoriesAreNotAMatch()
    {
        var mine = new SoftwareItem { Name = "Mine", DownloadDirectory = _folder };
        SoftwareManager.Items.AddRange([mine, new SoftwareItem { Name = "Blank" }]);

        Assert.Empty(SoftwareManager.OtherItemsUsingDirectory(mine, _folder));
        Assert.Empty(SoftwareManager.OtherItemsUsingDirectory(mine, ""));
    }
}
