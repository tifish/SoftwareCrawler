namespace SoftwareCrawler.Tests;

/// <summary>
/// The release-download gateway on github.com answers 504 for a release that
/// lists a lot of assets. These cover the URL and JSON shape the direct
/// download uses to ask the API for the same file.
/// </summary>
public class GitHubReleaseAssetTests
{
    [Fact]
    public void LatestDownloadUrlNamesTheFileAndNoTag()
    {
        var parsed = GitHubReleaseAsset.TryParse(
            "https://github.com/openai/codex/releases/latest/download/codex-package-x86_64-pc-windows-msvc.tar.gz",
            out var asset
        );

        Assert.True(parsed);
        Assert.Equal("openai", asset.Owner);
        Assert.Equal("codex", asset.Repo);
        Assert.Null(asset.Tag);
        Assert.Equal("codex-package-x86_64-pc-windows-msvc.tar.gz", asset.FileName);
        Assert.Equal(
            "https://api.github.com/repos/openai/codex/releases/latest",
            GitHubReleaseAsset.ReleaseApiUrl(asset)
        );
    }

    [Fact]
    public void TaggedDownloadUrlKeepsTheTag()
    {
        var parsed = GitHubReleaseAsset.TryParse(
            "https://github.com/openai/codex/releases/download/rust-v0.167.0/codex-package-aarch64-apple-darwin.tar.gz?no-cache=1",
            out var asset
        );

        Assert.True(parsed);
        Assert.Equal("rust-v0.167.0", asset.Tag);
        Assert.Equal("codex-package-aarch64-apple-darwin.tar.gz", asset.FileName);
        Assert.Equal(
            "https://api.github.com/repos/openai/codex/releases/tags/rust-v0.167.0",
            GitHubReleaseAsset.ReleaseApiUrl(asset)
        );
    }

    [Theory]
    [InlineData("https://github.com/openai/codex/releases/latest")]
    [InlineData("https://github.com/openai/codex/archive/refs/tags/rust-v0.167.0.tar.gz")]
    [InlineData("https://api.github.com/repos/openai/codex/releases/latest")]
    [InlineData("https://example.com/releases/latest/download/app.exe")]
    [InlineData("not a url")]
    public void OtherAddressesAreNotReleaseAssets(string url)
    {
        Assert.False(GitHubReleaseAsset.TryParse(url, out _));
    }

    [Fact]
    public void AssetLookupUsesTheApiUrlForTheExactName()
    {
        const string json = """
            {
              "assets": [
                {
                  "name": "other.tar.gz",
                  "url": "https://api.github.com/repos/openai/codex/releases/assets/1",
                  "browser_download_url": "https://github.com/openai/codex/releases/download/v1/other.tar.gz"
                },
                {
                  "name": "codex-package-x86_64-pc-windows-msvc.tar.gz",
                  "url": "https://api.github.com/repos/openai/codex/releases/assets/715505184",
                  "browser_download_url": "https://github.com/openai/codex/releases/download/rust-v0.167.0/codex-package-x86_64-pc-windows-msvc.tar.gz"
                }
              ]
            }
            """;

        Assert.Equal(
            "https://api.github.com/repos/openai/codex/releases/assets/715505184",
            GitHubReleaseAsset.FindAssetApiUrl(json, "codex-package-x86_64-pc-windows-msvc.tar.gz")
        );
        Assert.Null(GitHubReleaseAsset.FindAssetApiUrl(json, "missing.tar.gz"));
        Assert.Null(GitHubReleaseAsset.FindAssetApiUrl("not json", "missing.tar.gz"));
    }

    [Fact]
    public void WildcardDownloadUrlIsAPatternAgainstLatest()
    {
        var parsed = GitHubReleaseAsset.TryParse(
            "https://github.com/pingdotgg/t3code/releases/latest/download/*-x64.exe",
            out var asset
        );

        Assert.True(parsed);
        Assert.Equal("pingdotgg", asset.Owner);
        Assert.Equal("t3code", asset.Repo);
        Assert.Null(asset.Tag);
        Assert.Equal("*-x64.exe", asset.FileName);
        Assert.True(asset.IsPattern);
        Assert.Equal(
            "https://api.github.com/repos/pingdotgg/t3code/releases/latest",
            GitHubReleaseAsset.ReleaseApiUrl(asset)
        );
    }

    [Fact]
    public void WildcardMatchesOneAssetAndRejectsZeroOrSeveral()
    {
        const string json = """
            {
              "assets": [
                {
                  "name": "T3-Code-0.1.7-arm64.exe",
                  "url": "https://api.github.com/repos/pingdotgg/t3code/releases/assets/1"
                },
                {
                  "name": "T3-Code-0.1.7-x64.exe",
                  "url": "https://api.github.com/repos/pingdotgg/t3code/releases/assets/2"
                },
                {
                  "name": "T3-Code-0.1.7-x64.exe.blockmap",
                  "url": "https://api.github.com/repos/pingdotgg/t3code/releases/assets/3"
                },
                {
                  "name": "T3-Code-0.1.7-x64.dmg",
                  "url": "https://api.github.com/repos/pingdotgg/t3code/releases/assets/4"
                }
              ]
            }
            """;

        Assert.Equal(
            "https://api.github.com/repos/pingdotgg/t3code/releases/assets/2",
            GitHubReleaseAsset.FindAssetApiUrl(json, "*-x64.exe")
        );
        Assert.Null(GitHubReleaseAsset.FindAssetApiUrl(json, "*-no-such-file"));

        const string ambiguous = """
            {
              "assets": [
                {
                  "name": "App-1-x64.exe",
                  "url": "https://api.github.com/repos/example/app/releases/assets/1"
                },
                {
                  "name": "App-2-x64.exe",
                  "url": "https://api.github.com/repos/example/app/releases/assets/2"
                }
              ]
            }
            """;

        Assert.Null(GitHubReleaseAsset.FindAssetApiUrl(ambiguous, "*-x64.exe"));
    }
}
