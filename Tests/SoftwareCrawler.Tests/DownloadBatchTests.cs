using SoftwareCrawler;

namespace SoftwareCrawler.Tests;

/// <summary>
/// The queue behind the download and test menu items: what order items run in,
/// what a cancel stops, and what the run reports back. These used to be private
/// to the main window and could only be checked by clicking.
/// </summary>
public class DownloadBatchTests
{
    private static SoftwareItem Item(string name) => new() { Name = name, Enabled = true };

    /// <summary>Records what was asked for, and answers with whatever the test wants.</summary>
    private sealed class FakeDownloader
    {
        public List<string> Downloaded { get; } = [];
        public List<(bool TestOnly, int RetryCount)> Arguments { get; } = [];
        public Func<SoftwareItem, bool> Result { get; set; } = _ => true;
        public Action<SoftwareItem>? Before { get; set; }

        public Task<bool> Download(SoftwareItem item, bool testOnly, int retryCount)
        {
            Before?.Invoke(item);
            Downloaded.Add(item.Name);
            Arguments.Add((testOnly, retryCount));
            return Task.FromResult(Result(item));
        }
    }

    [Fact]
    public async Task ItemsRunInOrderAndTheRunReportsSuccess()
    {
        var fake = new FakeDownloader();
        var batch = new DownloadBatch(fake.Download);

        var succeeded = await batch.RunAsync([Item("A"), Item("B"), Item("C")]);

        Assert.True(succeeded);
        Assert.Equal(["A", "B", "C"], fake.Downloaded);
    }

    [Fact]
    public async Task OneFailureFailsTheRunWithoutStoppingIt()
    {
        var fake = new FakeDownloader { Result = item => item.Name != "B" };
        var batch = new DownloadBatch(fake.Download);

        var succeeded = await batch.RunAsync([Item("A"), Item("B"), Item("C")]);

        Assert.False(succeeded);
        // C still gets its turn: one broken recipe should not hold up the rest.
        Assert.Equal(["A", "B", "C"], fake.Downloaded);
    }

    [Fact]
    public async Task CancellingDuringAnItemSkipsTheRest()
    {
        var fake = new FakeDownloader();
        var batch = new DownloadBatch(fake.Download);
        fake.Before = item =>
        {
            if (item.Name == "B")
                batch.Cancel();
        };

        var succeeded = await batch.RunAsync([Item("A"), Item("B"), Item("C")]);

        Assert.False(succeeded);
        Assert.Equal(["A", "B"], fake.Downloaded);
        Assert.True(batch.HasCancelled);
    }

    [Fact]
    public async Task ACancelledRunDoesNotPoisonTheNextOne()
    {
        var fake = new FakeDownloader();
        var batch = new DownloadBatch(fake.Download);
        fake.Before = item =>
        {
            if (item.Name == "A")
                batch.Cancel();
        };

        Assert.False(await batch.RunAsync([Item("A"), Item("B")]));
        Assert.Equal(["A"], fake.Downloaded);

        fake.Before = null;
        fake.Downloaded.Clear();

        // The flag is cleared at the start of a run, not at the end of the last
        // one, so the next batch is not stopped before it begins.
        Assert.True(await batch.RunAsync([Item("C")]));
        Assert.Equal(["C"], fake.Downloaded);
        Assert.False(batch.HasCancelled);
    }

    [Fact]
    public async Task EveryItemIsResetBeforeTheFirstDownloadStarts()
    {
        var items = new[] { Item("A"), Item("B") };
        foreach (var item in items)
        {
            item.Status = DownloadingStatus.Failed;
            item.ErrorMessage = "from a previous run";
        }

        var seen = new List<DownloadingStatus>();
        // The status of the *other* item, sampled while the first one downloads.
        var fake = new FakeDownloader { Before = _ => seen.Add(items[1].Status) };

        await new DownloadBatch(fake.Download).RunAsync(items);

        Assert.Equal(DownloadingStatus.Idle, seen[0]);
        Assert.Equal(string.Empty, items[1].ErrorMessage);
    }

    [Fact]
    public async Task TestOnlyAndRetryCountReachEveryItem()
    {
        var fake = new FakeDownloader();

        await new DownloadBatch(fake.Download)
            .RunAsync([Item("A"), Item("B")], testOnly: true, retryCount: 3);

        Assert.All(fake.Arguments, argument => Assert.Equal((true, 3), argument));
    }

    [Fact]
    public async Task TheCurrentItemIsExposedWhileItRunsAndClearedAfter()
    {
        var fake = new FakeDownloader();
        var batch = new DownloadBatch(fake.Download);
        var current = new List<string?>();
        fake.Before = _ => current.Add(batch.CurrentItem?.Name);

        await batch.RunAsync([Item("A"), Item("B")]);

        Assert.Equal(["A", "B"], current);
        Assert.Null(batch.CurrentItem);
        Assert.False(batch.IsRunning);
    }
}
