using System.Text.Json;
using SoftwareCrawler;

namespace SoftwareCrawler.Tests;

public class DownloadMetadataStoreTests
{
    [Fact]
    public void EntriesRoundTripAndReplaceByItemName()
    {
        var directory = Directory.CreateTempSubdirectory("SoftwareCrawlerMetadata");
        try
        {
            var first = new DownloadMetadataStore.Entry
            {
                ItemName = "NaiveProxy",
                Source = "https://example.test/naive",
                FileName = "naive.zip",
                Size = 123,
                LastModified = new DateTime(2026, 8, 15, 1, 2, 3, DateTimeKind.Utc),
            };
            DownloadMetadataStore.Write(directory.FullName, first);

            first.FileName = "naive-new.zip";
            first.Size = 456;
            DownloadMetadataStore.Write(directory.FullName, first);

            Assert.True(
                DownloadMetadataStore.TryGet(directory.FullName, "naiveproxy", out var restored)
            );
            Assert.Equal("naive-new.zip", restored.FileName);
            Assert.Equal(456, restored.Size);
            Assert.Single(
                JsonSerializer.Deserialize<JsonElement>(
                    File.ReadAllText(Path.Join(directory.FullName, DownloadMetadataStore.FileName))
                )
                    .GetProperty("Downloads")
                    .EnumerateArray()
            );
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public void CorruptSidecarIsIgnored()
    {
        var directory = Directory.CreateTempSubdirectory("SoftwareCrawlerMetadata");
        try
        {
            File.WriteAllText(
                Path.Join(directory.FullName, DownloadMetadataStore.FileName),
                "not json"
            );

            Assert.False(DownloadMetadataStore.TryGet(directory.FullName, "NaiveProxy", out _));
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public void DownloadComparisonUsesSizeThenLastModified()
    {
        Assert.True(DownloadPipeline.IsSameDownload(100, null, 100, null));
        Assert.False(DownloadPipeline.IsSameDownload(100, null, 101, null));

        var time = new DateTime(2026, 8, 15, 1, 2, 3, DateTimeKind.Local);
        Assert.True(DownloadPipeline.IsSameDownload(0, time, 0, time.AddSeconds(1)));
        Assert.False(DownloadPipeline.IsSameDownload(0, time, 0, time.AddSeconds(2)));
    }

    [Fact]
    public void HeaderSizeMatchesWhenTheBrowserHasNotReportedOne()
    {
        // The browser reported no size, and Last-Modified has drifted since the
        // file was stamped. The Content-Length on that response is the real size.
        var stored = new DateTime(2026, 11, 10, 21, 16, 15, DateTimeKind.Local);
        var drifted = stored.AddHours(4);
        var size = DownloadPipeline.ResolveDownloadSize(
            browserReportedSize: 0,
            observedContentLength: 60_732_976
        );

        Assert.Equal(60_732_976, size);
        Assert.True(DownloadPipeline.IsSameDownload(size, drifted, 60_732_976, stored));
        Assert.False(DownloadPipeline.IsSameDownload(0, drifted, 60_732_976, stored));
        Assert.Equal(60_732_976, DownloadPipeline.ResolveDownloadSize(60_732_976, 1));
    }

    [Fact]
    public void ResponseHeaderFactsKeepContentLengthAndLastModified()
    {
        const string payload = """
            {
              "response": {
                "url": "https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/9.0.23/windowsdesktop-runtime-9.0.23-win-x64.exe",
                "headers": {
                  "content-length": "60732976",
                  "last-modified": "Tue, 10 Nov 2026 17:11:32 GMT"
                }
              }
            }
            """;

        Assert.True(BrowserObject.TryParseResponseHeaderFacts(payload, out var facts));
        Assert.Equal(
            "https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/9.0.23/windowsdesktop-runtime-9.0.23-win-x64.exe",
            facts.Url
        );
        Assert.Equal(60_732_976, facts.ContentLength);
        Assert.False(facts.ContentEncoded);
        Assert.Equal(
            new DateTime(2026, 11, 10, 17, 11, 32, DateTimeKind.Utc),
            facts.LastModified?.ToUniversalTime()
        );
        Assert.False(BrowserObject.TryParseResponseHeaderFacts("{}", out _));
    }

    [Fact]
    public void GzipContentLengthIsNotTheFileSize()
    {
        // builds.dotnet.microsoft.com answers a browser with gzip. The compressed
        // length is not the installer that lands on disk.
        const string payload = """
            {
              "response": {
                "url": "https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/9.0.23/windowsdesktop-runtime-9.0.23-win-x64.exe",
                "headers": {
                  "content-encoding": "gzip",
                  "content-length": "60495713"
                }
              }
            }
            """;

        Assert.True(BrowserObject.TryParseResponseHeaderFacts(payload, out var facts));
        Assert.True(facts.ContentEncoded);
        Assert.Equal(60_495_713, facts.ContentLength);
        Assert.Equal(
            0,
            DownloadPipeline.ResolveDownloadSize(
                browserReportedSize: 0,
                observedContentLength: facts.ContentEncoded ? 0 : facts.ContentLength
            )
        );
        Assert.False(BrowserObject.IsEncodedContent(null));
        Assert.False(BrowserObject.IsEncodedContent("identity"));
        Assert.True(BrowserObject.IsEncodedContent("gzip"));
    }

    [Fact]
    public void ArchiveMetadataOverridesTheRetainedFile()
    {
        var directory = Directory.CreateTempSubdirectory("SoftwareCrawlerMetadataPriority");
        try
        {
            var archive = Path.Join(directory.FullName, "retained.zip");
            File.WriteAllBytes(archive, new byte[999]);
            var item = new SoftwareItem
            {
                Name = "MetadataPriority",
                DownloadDirectory = directory.FullName,
            };
            DownloadMetadataStore.Write(
                directory.FullName,
                new DownloadMetadataStore.Entry
                {
                    ItemName = item.Name,
                    FileName = Path.GetFileName(archive),
                    Size = 100,
                }
            );

            Assert.True(
                DownloadPipeline.TryCompareArchiveMetadata(
                    item,
                    archive,
                    currentSize: 100,
                    currentLastModified: null,
                    out var metadataFilePath,
                    out var metadataMatches
                )
            );
            Assert.True(metadataMatches);
            Assert.Equal(archive, metadataFilePath);

            Assert.True(
                DownloadPipeline.TryCompareArchiveMetadata(
                    item,
                    archive,
                    currentSize: 999,
                    currentLastModified: null,
                    out _,
                    out metadataMatches
                )
            );
            Assert.False(metadataMatches);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Theory]
    [InlineData("package.zip", true)]
    [InlineData("package.rar", true)]
    [InlineData("package.7z", true)]
    [InlineData("package.exe", false)]
    public void ArchiveExtensionsAreRecognized(string fileName, bool expected)
    {
        Assert.Equal(expected, DownloadPipeline.IsArchiveFile(fileName));
    }

    [Fact]
    public async Task ArchiveIsDeletedAfterSuccessfulProcessing()
    {
        var directory = Directory.CreateTempSubdirectory("SoftwareCrawlerArchive");
        try
        {
            var archive = Path.Join(directory.FullName, "package.zip");
            await File.WriteAllTextAsync(archive, "archive bytes");
            var item = new SoftwareItem
            {
                Name = "ArchiveOnly",
                WebPage = "https://example.test/package.zip",
                ExtractAfterDownload = false,
            };

            await DownloadPipeline.FinalizeArchiveFile(
                item,
                archive,
                processed: true,
                deleteArchive: true
            );

            Assert.False(File.Exists(archive));
            Assert.True(
                DownloadMetadataStore.TryGet(directory.FullName, item.Name, out var metadata)
            );
            Assert.Equal("package.zip", metadata.FileName);
            Assert.Equal("archive bytes".Length, metadata.Size);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public async Task EveryArchiveGetsMetadataButIsRetainedWithoutSuccessfulProcessing()
    {
        var directory = Directory.CreateTempSubdirectory("SoftwareCrawlerArchive");
        try
        {
            var archive = Path.Join(directory.FullName, "package.zip");
            await File.WriteAllTextAsync(archive, "archive bytes");
            // Existence alone must not count as a successfully executed script.
            await File.WriteAllTextAsync(Path.Join(directory.FullName, "AfterDownload.cmd"), "");
            var item = new SoftwareItem
            {
                Name = "ArchiveOnly",
                WebPage = "https://example.test/package.zip",
                ExtractAfterDownload = false,
            };

            await DownloadPipeline.FinalizeArchiveFile(
                item,
                archive,
                processed: false,
                deleteArchive: false
            );

            Assert.True(File.Exists(archive));
            Assert.True(
                DownloadMetadataStore.TryGet(directory.FullName, item.Name, out var metadata)
            );
            Assert.Equal("package.zip", metadata.FileName);
            Assert.Equal("archive bytes".Length, metadata.Size);
            Assert.False(metadata.Processed);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public async Task SameVersionKeepsItsProcessedFlagAndNewVersionResetsIt()
    {
        var directory = Directory.CreateTempSubdirectory("SoftwareCrawlerArchive");
        try
        {
            var archive = Path.Join(directory.FullName, "package.tar.gz");
            await File.WriteAllTextAsync(archive, "archive bytes");
            var item = new SoftwareItem
            {
                Name = "ScriptedArchive",
                WebPage = "https://example.test/package.tar.gz",
            };

            // An AfterDownload script ran: the archive stays, marked as processed.
            await DownloadPipeline.FinalizeArchiveFile(
                item,
                archive,
                processed: true,
                deleteArchive: false
            );
            Assert.True(File.Exists(archive));

            // A later run finds the same version and must not forget that.
            await DownloadPipeline.FinalizeArchiveFile(
                item,
                archive,
                processed: null,
                deleteArchive: false
            );
            Assert.True(
                DownloadMetadataStore.TryGet(directory.FullName, item.Name, out var same)
            );
            Assert.True(same.Processed);

            // A new download starts over.
            await DownloadPipeline.FinalizeArchiveFile(
                item,
                archive,
                processed: false,
                deleteArchive: false
            );
            Assert.True(
                DownloadMetadataStore.TryGet(directory.FullName, item.Name, out var fresh)
            );
            Assert.False(fresh.Processed);
        }
        finally
        {
            directory.Delete(true);
        }
    }
}
