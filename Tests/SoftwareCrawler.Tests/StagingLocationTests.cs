using SoftwareCrawler;

namespace SoftwareCrawler.Tests;

/// <summary>
/// Where a download is staged decides whether finishing it is a rename or a copy,
/// and whether every write during it crosses the network.
/// </summary>
public class StagingLocationTests
{
    private const string Unc = @"\\server\share\Example";

    [Fact]
    public void ALocalDestinationIsStagedInPlace()
    {
        var target = Path.Combine(Path.GetTempPath(), "Example", "Setup-2.0.exe");

        var staged = DownloadPipeline.StagingPathFor(target);

        Assert.Equal(target + DownloadPipeline.PartialSuffix, staged);
        Assert.Equal(Path.GetDirectoryName(target), Path.GetDirectoryName(staged));
    }

    /// <summary>
    /// Streaming onto a share would put every write on the wire and lose the
    /// transfer to one blip, so those land locally and are copied once.
    /// </summary>
    [Fact]
    public void ANetworkDestinationIsStagedLocally()
    {
        var staged = DownloadPipeline.StagingPathFor(Path.Combine(Unc, "Setup-2.0.exe"));

        Assert.Equal(Path.Combine(SoftwareItem.SystemDownloadFolder, "Setup-2.0.exe"), staged);
        Assert.DoesNotContain(DownloadPipeline.PartialSuffix, staged);
    }

    [Fact]
    public void UncPathsAreRecognized()
    {
        Assert.True(DownloadPipeline.IsNetworkPath(Unc));
        Assert.True(DownloadPipeline.IsNetworkPath(@"\\127.0.0.1\c$\temp"));
    }

    [Theory]
    [InlineData(@"C:\Downloads")]
    [InlineData(@"G:\Warez\AI\CUDA")]
    [InlineData("")]
    [InlineData(null)]
    public void LocalAndUnusablePathsAreNotNetwork(string? directory) =>
        Assert.False(DownloadPipeline.IsNetworkPath(directory));
}
