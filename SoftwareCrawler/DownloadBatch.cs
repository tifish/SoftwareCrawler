using JeekTools;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace SoftwareCrawler;

/// <summary>
/// Runs a set of items through their downloads, one at a time in order, and owns
/// the cancel flag for that run.
///
/// Split out of <see cref="MainForm"/>, which used to hold the flag as a private
/// field: the order, the reset pass, the retry count and what cancelling means
/// are rules about downloading, not about the window. Keeping them here is what
/// lets the debug tools and the tests drive a batch without a form.
/// </summary>
public sealed class DownloadBatch
{
    private static readonly ILogger Log = LogManager.CreateLogger(nameof(DownloadBatch));

    /// <summary>
    /// How one item is downloaded. Real runs go to <see cref="SoftwareItem.Download"/>;
    /// tests substitute their own so a batch can be exercised without a browser.
    /// </summary>
    private readonly Func<SoftwareItem, bool, int, Task<bool>> _download;

    public DownloadBatch()
        : this((item, testOnly, retryCount) => item.Download(testOnly, retryCount)) { }

    internal DownloadBatch(Func<SoftwareItem, bool, int, Task<bool>> download) =>
        _download = download;

    private volatile bool _hasCancelled;

    /// <summary>The item being downloaded right now, or null when nothing is running.</summary>
    public SoftwareItem? CurrentItem { get; private set; }

    /// <summary>True between the start and the end of a run, cancelled or not.</summary>
    public bool IsRunning { get; private set; }

    /// <summary>True once the current run was asked to stop.</summary>
    public bool HasCancelled => _hasCancelled;

    /// <summary>
    /// Stops after the current item. The item itself is told too, so a transfer
    /// already in flight is aborted rather than waited out.
    /// </summary>
    public void Cancel()
    {
        _hasCancelled = true;
        CurrentItem?.CancelDownload();
    }

    /// <summary>
    /// Resets every item's status, then downloads them in order, stopping at the
    /// first cancel. Returns false if any item failed or the run was cancelled.
    /// </summary>
    /// <param name="items">Snapshotted before the first download, so the caller is
    /// free to hand over a live collection.</param>
    /// <param name="testOnly">Check for an update without keeping the file.</param>
    /// <param name="retryCount">Extra attempts per item, passed straight through.</param>
    /// <param name="operation">Names this run in the log.</param>
    public async Task<bool> RunAsync(
        IEnumerable<SoftwareItem> items,
        bool testOnly = false,
        int retryCount = 0,
        string operation = "Download"
    )
    {
        var list = items.ToList();

        Log.ZLogInformation($"{operation} starts with {list.Count} item(s)");

        _hasCancelled = false;
        IsRunning = true;
        var success = true;

        try
        {
            foreach (var item in list)
            {
                if (_hasCancelled)
                {
                    success = false;
                    break;
                }

                item.ResetStatus();
            }

            foreach (var item in list)
            {
                if (_hasCancelled)
                {
                    success = false;
                    break;
                }

                CurrentItem = item;
                if (!await _download(item, testOnly, retryCount))
                    success = false;
            }
        }
        finally
        {
            CurrentItem = null;
            IsRunning = false;
        }

        Log.ZLogInformation($"{operation} ends with success = {success}");
        return success;
    }
}
