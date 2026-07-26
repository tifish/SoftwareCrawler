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

    public OldVersionDeletionTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
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

    [Fact]
    public async Task OldVersionsGoAndTheNewFileStays()
    {
        var keep = CreateFile("Example-2.0.exe");
        var old = CreateFile("Example-1.0.exe");

        var deleted = await DownloadPipeline.DeleteOldVersions(_folder, "*.exe", keep);

        Assert.Equal([old], deleted);
        Assert.True(File.Exists(keep));
        Assert.False(File.Exists(old));
    }

    [Fact]
    public async Task FilesThePatternDoesNotMatchAreLeftAlone()
    {
        var keep = CreateFile("Example-2.0.exe");
        var unrelated = CreateFile("notes.txt");

        await DownloadPipeline.DeleteOldVersions(_folder, "*.exe", keep);

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
            keep
        );

        Assert.Equal([myOldVersion], deleted);
        Assert.True(File.Exists(keep));
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

        var deleted = await DownloadPipeline.DeleteOldVersions(_folder, "*.exe", keep);

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
            ""
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
            ""
        );

        Assert.Empty(deleted);
    }
}
