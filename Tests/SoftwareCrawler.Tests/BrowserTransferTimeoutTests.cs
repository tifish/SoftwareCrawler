namespace SoftwareCrawler.Tests;

public class BrowserTransferTimeoutTests
{
    [Fact]
    public async Task ATransferDeadlineIsDistinguishableFromCancellation()
    {
        var browser = new BrowserObject();
        browser.PrepareLoadEvents();

        await Assert.ThrowsAsync<TimeoutException>(() =>
            browser.WaitForDownloaded(TimeSpan.FromMilliseconds(30))
        );

        browser.Cancel();
        Assert.False(await browser.WaitForDownloaded(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task CancellingATransferReleasesItsWaiterWithoutWaitingForTheDeadline()
    {
        var browser = new BrowserObject();
        browser.PrepareLoadEvents();
        var wait = browser.WaitForDownloaded(TimeSpan.FromMinutes(15));

        browser.Cancel();

        Assert.False(await wait.WaitAsync(TimeSpan.FromSeconds(1)));
    }
}
