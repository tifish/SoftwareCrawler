using System.Diagnostics;
using System.Net;
using JeekTools;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace SoftwareCrawler;

/// <summary>
/// One download attempt for one <see cref="SoftwareItem"/>: drive the page (or
/// fetch the URL directly), decide whether the file is worth downloading, put it
/// in place, and run whatever the item asks for afterwards.
///
/// Split out of <see cref="SoftwareItem"/>, which is the recipe and the status
/// the UI binds to; this is the machinery that acts on them. One instance runs
/// once - the per-attempt state below lives for exactly that long.
/// </summary>
internal sealed class DownloadPipeline(SoftwareItem softwareItem, bool testOnly)
{
    /// <summary>The item being downloaded: its recipe drives this, its status reflects it.</summary>
    private readonly SoftwareItem _item = softwareItem;

    private static readonly ILogger Log = LogManager.CreateLogger(nameof(DownloadPipeline));

    /// <summary>How one attempt ended, and whether trying again could help.</summary>
    internal enum DownloadOnceResult
    {
        Succeeded,
        FailedAndRetry,
        FailedAndNoRetry,
    }

    /// <summary>What the decision at the start of a transfer came to.</summary>
    private enum BeginDownloadResult
    {
        NoDownload,
        Failed,
        Downloaded,
        HasUpdate,
        Started,
    }

    public async Task<DownloadOnceResult> RunAsync()
    {
        // Initialize
        _item.UiSynchronizationContext = SynchronizationContext.Current;

        _item.Status = DownloadingStatus.CheckingDownloadDirectory;
        _item.ErrorMessage = string.Empty;

        if (string.IsNullOrEmpty(_item.FinalDownloadDirectory))
            return Failed("Download directory is empty.", DownloadOnceResult.FailedAndNoRetry);

        if (!Directory.Exists(_item.FinalDownloadDirectory))
            try
            {
                Directory.CreateDirectory(_item.FinalDownloadDirectory);
            }
            catch (Exception)
            {
                return Failed(
                    "Download directory does not exist, and failed to create.",
                    DownloadOnceResult.FailedAndNoRetry
                );
            }

        if (_item.DownloadDirectory2 != "" && !Directory.Exists(_item.DownloadDirectory2))
            try
            {
                Directory.CreateDirectory(_item.DownloadDirectory2);
            }
            catch (Exception)
            {
                return Failed(
                    "Download directory 2 does not exist, and failed to create.",
                    DownloadOnceResult.FailedAndNoRetry
                );
            }

        var suggestedFileName = string.Empty;
        var downloadFileSize = 0L;
        DateTime? downloadFileTime = null;
        var targetFilePath = string.Empty;
        var downloadedFilePath = string.Empty;
        var beginDownloadResult = BeginDownloadResult.NoDownload;

        Browser.BeginDownloadHandler += OnBeginDownloadHandler;
        Browser.DownloadProgressHandler += OnDownloadProgressHandler;

        // Download
        try
        {
            if (_item.DirectDownload)
                return await DirectDownloadOverHttp();

            await Browser.EnsureProxy(_item.EffectiveProxy);
            await Browser.ResetToBlankPage();

            // Access download page.
            await Browser.Load(_item.WebPage);

            // Click links, last link is the download link.
            var clickResult = await ClickAndTriggerDownload();
            if (clickResult != DownloadOnceResult.Succeeded)
                return clickResult;

            // Wait for download to start.
            _item.Status = DownloadingStatus.WaitingForDownload;
            var startDownloadTimeout =
                _item.StartDownloadTimeout > 0 ? _item.StartDownloadTimeout : Settings.StartDownloadTimeout;
            var waitCounter = startDownloadTimeout * 2;
            while (beginDownloadResult == BeginDownloadResult.NoDownload)
            {
                if (_item.HasCancelled)
                    return DownloadOnceResult.FailedAndNoRetry;

                await Task.Delay(500);
                waitCounter--;
                if (waitCounter == 0)
                    return Failed("Failed to start download.", DownloadOnceResult.FailedAndRetry);
            }

            // Do not download.
            switch (beginDownloadResult)
            {
                case BeginDownloadResult.Failed: // Failed to download
                    return DownloadOnceResult.FailedAndRetry;
                case BeginDownloadResult.Downloaded: // Same file already downloaded
                    return await Succeeded(DownloadingStatus.SameFileAlreadyDownloaded);
                case BeginDownloadResult.HasUpdate: // Has update but test only
                    return await Succeeded(DownloadingStatus.HasUpdate);
            }

            // Wait for download to complete.
            _item.Status = DownloadingStatus.Downloading;
            if (!await Browser.WaitForDownloaded(TimeSpan.FromSeconds(Settings.DownloadTimeout)))
                return Failed("Failed to download file.", DownloadOnceResult.FailedAndRetry);

            return await Succeeded(DownloadingStatus.Downloaded);
        }
        catch (Exception ex)
        {
            Log.ZLogError(ex, $"Download {_item.Name} failed");
            return Failed(ex.Message, DownloadOnceResult.FailedAndNoRetry);
        }
        finally
        {
            if (!testOnly && !string.IsNullOrEmpty(downloadedFilePath))
                await DeleteStagedFile(downloadedFilePath);

            _item.UiSynchronizationContext = null;
            Browser.BeginDownloadHandler -= OnBeginDownloadHandler;
            Browser.DownloadProgressHandler -= OnDownloadProgressHandler;
        }

        // Fetch WebPage with HttpClient and stream it to disk, bypassing the browser.
        // A non-browser User-Agent avoids Cloudflare browser challenges; the
        // "Windows NT" hint makes SourceForge-style "latest" links resolve to the
        // Windows release instead of the generic default.
        async Task<DownloadOnceResult> DirectDownloadOverHttp()
        {
            _item.Status = DownloadingStatus.WaitingForDownload;

            using var handler = new HttpClientHandler();
            if (!string.IsNullOrWhiteSpace(_item.EffectiveProxy))
                handler.Proxy = new WebProxy(_item.EffectiveProxy);

            using var client = new HttpClient(handler);
            client.Timeout = Timeout.InfiniteTimeSpan;
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "SoftwareCrawler/1.0 (Windows NT 10.0; Win64; x64)"
            );

            var startDownloadTimeout =
                _item.StartDownloadTimeout > 0 ? _item.StartDownloadTimeout : Settings.StartDownloadTimeout;

            HttpResponseMessage response;
            try
            {
                using var headerCts = new CancellationTokenSource(
                    TimeSpan.FromSeconds(startDownloadTimeout)
                );
                response = await client.GetAsync(
                    _item.WebPage,
                    HttpCompletionOption.ResponseHeadersRead,
                    headerCts.Token
                );
            }
            catch (Exception ex)
            {
                return Failed(
                    $"Failed to request download URL: {ex.Message}",
                    DownloadOnceResult.FailedAndRetry
                );
            }

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                    return Failed(
                        $"Download URL returned {(int)response.StatusCode} {response.ReasonPhrase}.",
                        DownloadOnceResult.FailedAndRetry
                    );

                var contentDisposition = response.Content.Headers.ContentDisposition;
                var fileName =
                    contentDisposition?.FileNameStar ?? contentDisposition?.FileName?.Trim('"');
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    var finalUri = response.RequestMessage?.RequestUri ?? new Uri(_item.WebPage);
                    fileName = Uri.UnescapeDataString(Path.GetFileName(finalUri.LocalPath));
                }

                if (string.IsNullOrWhiteSpace(fileName))
                    return Failed(
                        "Cannot determine download file name.",
                        DownloadOnceResult.FailedAndNoRetry
                    );

                var item = new DownloadItem
                {
                    Url = response.RequestMessage?.RequestUri?.ToString() ?? _item.WebPage,
                    SuggestedFileName = fileName,
                    TotalBytes = response.Content.Headers.ContentLength ?? 0,
                    EndTime = response.Content.Headers.LastModified?.LocalDateTime,
                    LastUpdateTime = DateTime.Now,
                };

                // Reuse the same decision logic as browser downloads
                // (file type check, same-file detection, testOnly handling).
                OnBeginDownloadHandler(this, item);

                switch (beginDownloadResult)
                {
                    case BeginDownloadResult.Failed:
                        return DownloadOnceResult.FailedAndRetry;
                    case BeginDownloadResult.Downloaded:
                        return await Succeeded(DownloadingStatus.SameFileAlreadyDownloaded);
                    case BeginDownloadResult.HasUpdate:
                        return await Succeeded(DownloadingStatus.HasUpdate);
                }

                _item.Status = DownloadingStatus.Downloading;
                try
                {
                    using var downloadCts = new CancellationTokenSource(
                        TimeSpan.FromSeconds(Settings.DownloadTimeout)
                    );
                    await using var httpStream = await response.Content.ReadAsStreamAsync(
                        downloadCts.Token
                    );
                    await using var fileStream = new FileStream(
                        item.DownloadedFilePath,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.None,
                        81920,
                        useAsync: true
                    );

                    var buffer = new byte[81920];
                    int bytesRead;
                    while (
                        (bytesRead = await httpStream.ReadAsync(buffer, downloadCts.Token)) > 0
                    )
                    {
                        if (_item.HasCancelled)
                        {
                            item.IsCancelled = true;
                            return DownloadOnceResult.FailedAndNoRetry;
                        }

                        await fileStream.WriteAsync(
                            buffer.AsMemory(0, bytesRead),
                            downloadCts.Token
                        );

                        item.ReceivedBytes += bytesRead;
                        item.PercentComplete =
                            item.TotalBytes > 0
                                ? (int)((double)item.ReceivedBytes / item.TotalBytes * 100)
                                : 0;

                        var now = DateTime.Now;
                        var elapsed = (now - item.LastUpdateTime).TotalSeconds;
                        if (elapsed >= 0.2)
                        {
                            item.CurrentSpeed = (long)(
                                (item.ReceivedBytes - item.LastReceivedBytes) / elapsed
                            );
                            item.RemainingTime =
                                item.CurrentSpeed > 0 && item.TotalBytes > 0
                                    ? TimeSpan.FromSeconds(
                                        (item.TotalBytes - item.ReceivedBytes)
                                            / (double)item.CurrentSpeed
                                    )
                                    : TimeSpan.Zero;
                            item.LastReceivedBytes = item.ReceivedBytes;
                            item.LastUpdateTime = now;
                            OnDownloadProgressHandler(this, item);
                        }
                    }
                }
                catch (Exception ex)
                {
                    return Failed(
                        $"Failed to download file: {ex.Message}",
                        DownloadOnceResult.FailedAndRetry
                    );
                }

                if (item.TotalBytes > 0 && item.ReceivedBytes != item.TotalBytes)
                    return Failed(
                        $"Download incomplete: {item.ReceivedBytes} of {item.TotalBytes} bytes.",
                        DownloadOnceResult.FailedAndRetry
                    );

                if (item.TotalBytes == 0)
                    item.TotalBytes = item.ReceivedBytes;
                item.PercentComplete = 100;
                item.IsComplete = true;
                OnDownloadProgressHandler(this, item);

                return await Succeeded(DownloadingStatus.Downloaded);
            }
        }

        async Task<DownloadOnceResult> ClickAndTriggerDownload()
        {
            var frameNames = string.IsNullOrWhiteSpace(_item.Frames)
                ? []
                : _item.Frames.Split('`').Select(x => x.Trim()).ToList();

            var xpathOrScripts = _item.GetXPathOrScripts();
            for (var i = 0; i < xpathOrScripts.Count; i++)
            {
                var stepWatch = Stopwatch.StartNew();

                _item.Status = DownloadingStatus.WaitingForLoadEnd;
                var outcome = "timed out";
                for (var seconds = 0; seconds < Settings.LoadPageEndTimeout; seconds++)
                {
                    if (_item.HasCancelled)
                        return DownloadOnceResult.FailedAndNoRetry;
                    if (await Browser.WaitForMainFrameLoadEnd(TimeSpan.FromSeconds(1)))
                    {
                        outcome = "ended";
                        break;
                    }

                    // A click that swaps the page in place - GitHub's turbo does this -
                    // raises no load event, so the wait above can only time out. The URL
                    // changing under us says the new page is in; the network going quiet
                    // since then says it has finished arriving.
                    if (Browser.HasNavigatedInPlace && Browser.IsPageSettled)
                    {
                        outcome = "settled in place";
                        break;
                    }
                }

                // Which of the three it was decides how to read every number after it.
                Log.ZLogDebug(
                    $"{_item.Name} step {i + 1}: load wait {outcome} after {stepWatch.Elapsed.TotalSeconds:F1}s"
                );

                // A floor for the pages known to need one, no longer a toll every item pays:
                // what the page still owes us is waited for below, by watching for it.
                if (_item.WaitSecondsBeforeClick > 0)
                    await Task.Delay(_item.WaitSecondsBeforeClick * 1000);

                var xpathOrScript = xpathOrScripts[i];
                var frameName = i < frameNames.Count ? frameNames[i] : string.Empty;

                // Whatever is left of the page budget, and never less than the click budget.
                // The wait above now ends at DOMContentLoaded, so the slack it used to burn
                // is spent here instead - waiting for the page's own scripts, which is what
                // "the page is not ready yet" actually means.
                var readyBudget = TimeSpan.FromSeconds(
                    Math.Max(
                        Math.Max(1, Settings.TryClickCount * Settings.TryClickInterval),
                        Settings.LoadPageEndTimeout - stepWatch.Elapsed.TotalSeconds
                    )
                );

                // Is XPath
                if (
                    xpathOrScript.StartsWith("//")
                        && xpathOrScript.Length >= 3
                        && char.IsLetter(xpathOrScript[2])
                    || xpathOrScript.StartsWith("(//")
                        && xpathOrScript.Length >= 4
                        && char.IsLetter(xpathOrScript[3])
                )
                {
                    _item.Status = DownloadingStatus.Clicking;

                    var targetState = await WaitForClickTarget(
                        xpathOrScript,
                        frameName,
                        readyBudget
                    );
                    if (_item.HasCancelled)
                        return DownloadOnceResult.FailedAndNoRetry;
                    // Anything other than a link clicked on a page that never settled is a
                    // click that may well land on nothing - worth saying so up front when
                    // the download then fails to start.
                    if (targetState != ClickTargetState.ReadyLink && !Browser.IsPageSettled)
                        Log.ZLogInformation(
                            $"{_item.Name}: clicking a {targetState} target on an unsettled page after {stepWatch.Elapsed.TotalSeconds:F1}s: {xpathOrScript}"
                        );

                    // Arm the load and download waits before the click - the click is what
                    // navigates or starts the download.
                    Browser.PrepareLoadEvents();

                    // Scroll to the element first
                    var scrollScript = $$"""
                        {
                            let element = document.evaluate("{{xpathOrScript}}", document, null, XPathResult.FIRST_ORDERED_NODE_TYPE, null).singleNodeValue;
                            let elementRect = element.getBoundingClientRect();
                            let absoluteElementTop = elementRect.top + window.pageYOffset;
                            let middle = absoluteElementTop - (window.innerHeight / 2);
                            window.scrollTo(0, middle);
                        }
                        """;
                    if (!await Browser.TryEvaluateJavascript(scrollScript, frameName))
                    {
                        // If scroll failed, try to click directly.
                        Log.ZLogError(
                            $"Failed to scroll to, error: {Browser.LastJavascriptError}"
                        );
                    }

                    // Then click - exactly once. The target was waited for rather than
                    // hammered, and a second click on a page that did accept the first one
                    // would start a second download.
                    if (!await Browser.Click(xpathOrScript, frameName))
                        return Failed(
                            $"Failed to click, error: {Browser.LastJavascriptError}",
                            DownloadOnceResult.FailedAndRetry
                        );
                }
                else // Is JavaScript
                {
                    _item.Status = DownloadingStatus.ExecutingScript;

                    // The script drives the page, so the page's own scripts have to be there
                    // first: DOMContentLoaded is too early to tell, settling says they are.
                    await WaitForPageSettled(readyBudget);
                    if (_item.HasCancelled)
                        return DownloadOnceResult.FailedAndNoRetry;

                    Browser.PrepareLoadEvents();

                    // Scripts often include their own wait/retry loops (and may return a
                    // Promise that WebView2 awaits). Do not re-run the whole script many
                    // times — each attempt can take a long time.
                    if (!await Browser.TryEvaluateJavascript(xpathOrScript, frameName, count: 1))
                        return Failed(
                            $"Failed to execute script: {Browser.LastJavascriptError}",
                            DownloadOnceResult.FailedAndRetry
                        );
                }
            }

            return DownloadOnceResult.Succeeded;
        }

        // Called when download starts, decide whether to download.
        void OnBeginDownloadHandler(object? o, DownloadItem item)
        {
            suggestedFileName = item.SuggestedFileName;
            downloadFileSize = item.TotalBytes;
            downloadFileTime = item.EndTime;

            // Download to the system download folder first, then move to the
            // download directory once it is complete. The destination is not
            // assumed to be an ordinary local folder: it may be a network share,
            // or a folder something else is watching and syncing. Writing a
            // growing file there would have it synced over and over, and an
            // interrupted one would be propagated to every device. Nothing shows
            // up in the destination until it is a finished file.
            downloadedFilePath = Path.Combine(SoftwareItem.SystemDownloadFolder, suggestedFileName);
            var ext = Path.GetExtension(item.SuggestedFileName).ToLower();

            if (!ExecutableFileTypes.Contains(ext) && !ArchiveFileTypes.Contains(ext))
            {
                Failed(
                    $"Unexpected file name: {suggestedFileName}",
                    DownloadOnceResult.FailedAndNoRetry
                );
                beginDownloadResult = BeginDownloadResult.Failed;
                item.IsCancelled = true;
                return;
            }

            targetFilePath = Path.Join(_item.FinalDownloadDirectory, suggestedFileName);

            // Archive metadata is the durable server-side identity. Once it exists,
            // do not let a retained or hand-modified archive override that identity.
            if (
                TryCompareArchiveMetadata(
                    _item,
                    targetFilePath,
                    downloadFileSize,
                    downloadFileTime,
                    out var metadataFilePath,
                    out var metadataMatches
                )
            )
            {
                if (metadataMatches)
                {
                    beginDownloadResult = BeginDownloadResult.Downloaded;
                    targetFilePath = metadataFilePath;
                    item.IsCancelled = true;
                    return;
                }

                // A metadata mismatch means the server has an update. Comparing the
                // retained archive as a second opinion would violate that contract.
                if (testOnly)
                {
                    beginDownloadResult = BeginDownloadResult.HasUpdate;
                    item.IsCancelled = true;
                    return;
                }

                beginDownloadResult = BeginDownloadResult.Started;
                item.DownloadedFilePath = downloadedFilePath;
                return;
            }

            // Compare file size to determine download or not.
            // Epic Launcher download page may change its file name for each download.
            // Find the old file and check the size.
            var oldFile = targetFilePath;
            if (
                !File.Exists(oldFile)
                && !string.IsNullOrWhiteSpace(_item.FilePatternToDeleteBeforeDownload)
            )
            {
                oldFile = Directory
                    .GetFiles(_item.FinalDownloadDirectory, _item.FilePatternToDeleteBeforeDownload)
                    .FirstOrDefault();
            }

            if (File.Exists(oldFile))
            {
                var fileInfo = new FileInfo(oldFile);

                // Prefer size comparison when the server reports the file size.
                // When the size is unknown (no Content-Length, e.g. chunked transfer),
                // fall back to comparing the server's Last-Modified time against the local
                // file's modification time. Downloaded files are stamped with that time in
                // Succeeded(), so a match means the file is unchanged. Without either signal
                // we cannot tell, so treat it as needing download.
                var isSameFile = IsSameDownload(
                    downloadFileSize,
                    downloadFileTime,
                    fileInfo.Length,
                    fileInfo.LastWriteTime
                );

                if (isSameFile)
                {
                    beginDownloadResult = BeginDownloadResult.Downloaded;
                    targetFilePath = oldFile;
                    item.IsCancelled = true;
                    return;
                }
            }

            if (testOnly)
            {
                beginDownloadResult = BeginDownloadResult.HasUpdate;
                item.IsCancelled = true;
                return;
            }

            beginDownloadResult = BeginDownloadResult.Started;
            // Tell the browser to download to the downloadingFilePath.
            item.DownloadedFilePath = downloadedFilePath;
        }

        // Called when download progress changes.
        void OnDownloadProgressHandler(object? o, DownloadItem item)
        {
            // Download file name may change if same file exists.
            downloadedFilePath = item.DownloadedFilePath;

            _item.Progress =
                $"{item.SuggestedFileName}"
                + $" - {item.PercentComplete:00}%"
                + $" - {item.ReceivedBytes:#,###} / {item.TotalBytes:#,###} Bytes"
                + $" - {item.CurrentSpeed / 1024:#,###} KB/s"
                + $" - {item.RemainingTime:hh\\:mm\\:ss}";
        }

        // When download is completed, move file to target directory.
        // finalStatus can be: Downloaded, SameFileAlreadyDownloaded, HasUpdate
        async Task<DownloadOnceResult> Succeeded(DownloadingStatus finalStatus)
        {
            _item.Status = finalStatus;

            _item.Progress = $"{suggestedFileName} - {(double)downloadFileSize:#,###} Bytes";

            if (testOnly) // finalStatus == HasUpdate
                return DownloadOnceResult.Succeeded;

            try
            {
                var primaryArchiveProcessed = false;
                var primaryWasCopied = false;

                // Delete other old files in the same directory.
                await DeleteOtherFilesInSameDirectory(targetFilePath);

                // Copy file from downloading folder to target directory.
                if (File.Exists(downloadedFilePath))
                {
                    if (downloadFileTime.HasValue)
                        File.SetLastWriteTime(downloadedFilePath, downloadFileTime.Value);

                    primaryWasCopied = await CopyFileIfChanged(
                        downloadedFilePath,
                        targetFilePath,
                        true
                    );
                }

                // Extract target file and copy to download directory 2.
                if (File.Exists(targetFilePath))
                {
                    if (downloadFileTime.HasValue)
                        File.SetLastWriteTime(targetFilePath, downloadFileTime.Value);

                    await FinalizeArchiveFile(
                        _item,
                        targetFilePath,
                        processingSucceeded: false,
                        downloadFileSize,
                        downloadFileTime
                    );

                    var retryRetainedArchive =
                        finalStatus == DownloadingStatus.SameFileAlreadyDownloaded
                        && IsArchiveFile(targetFilePath);

                    string? targetFile2 = null;
                    var secondaryWasCopied = false;
                    if (!string.IsNullOrEmpty(_item.DownloadDirectory2))
                    {
                        targetFile2 = Path.Combine(
                            _item.DownloadDirectory2,
                            Path.GetFileName(targetFilePath)
                        );
                        await DeleteOtherFilesInSameDirectory(targetFile2);
                        secondaryWasCopied = await CopyFileIfChanged(targetFilePath, targetFile2);

                        if (File.Exists(targetFile2))
                            await FinalizeArchiveFile(
                                _item,
                                targetFile2,
                                processingSucceeded: false,
                                downloadFileSize,
                                downloadFileTime
                            );
                    }

                    if (primaryWasCopied || retryRetainedArchive)
                        primaryArchiveProcessed |= await CallEventScript(
                            _item.FinalDownloadDirectory,
                            "AfterDownload",
                            targetFilePath
                        );

                    // A retained same-version archive represents processing that has
                    // not completed yet, so its processors may be retried without a
                    // second download.
                    if (finalStatus == DownloadingStatus.Downloaded || retryRetainedArchive)
                    {
                        primaryArchiveProcessed |= await ExtractArchiveFile(targetFilePath);
                        primaryArchiveProcessed |= await CallEventScript(
                            _item.FinalDownloadDirectory,
                            "AfterExtract",
                            targetFilePath
                        );
                    }

                    if (targetFile2 is not null && File.Exists(targetFile2))
                    {
                        var secondaryArchiveProcessed = false;
                        var retryRetainedSecondaryArchive =
                            finalStatus == DownloadingStatus.SameFileAlreadyDownloaded
                            && IsArchiveFile(targetFile2);

                        if (secondaryWasCopied || retryRetainedSecondaryArchive)
                            secondaryArchiveProcessed |= await CallEventScript(
                                _item.DownloadDirectory2,
                                "AfterDownload",
                                targetFile2
                            );

                        if (
                            finalStatus == DownloadingStatus.Downloaded
                            || retryRetainedSecondaryArchive
                        )
                        {
                            secondaryArchiveProcessed |= await ExtractArchiveFile(targetFile2);
                            secondaryArchiveProcessed |= await CallEventScript(
                                _item.DownloadDirectory2,
                                "AfterExtract",
                                targetFile2
                            );
                        }

                        if (secondaryArchiveProcessed)
                            await FinalizeArchiveFile(
                                _item,
                                targetFile2,
                                processingSucceeded: true,
                                downloadFileSize,
                                downloadFileTime
                            );
                    }
                }

                if (primaryArchiveProcessed)
                    await FinalizeArchiveFile(
                        _item,
                        targetFilePath,
                        processingSucceeded: true,
                        downloadFileSize,
                        downloadFileTime
                    );
            }
            catch (PostProcessException ex)
            {
                // Status already names the step that failed. The file is on disk,
                // so downloading it again would not help.
                return Failed(ex.Message, DownloadOnceResult.FailedAndNoRetry);
            }
            catch (Exception ex)
            {
                // Only change status when copying file fails.
                _item.Status = DownloadingStatus.CopyingFile;
                return Failed(ex.Message, DownloadOnceResult.FailedAndNoRetry);
            }

            return DownloadOnceResult.Succeeded;
        }

        Task DeleteOtherFilesInSameDirectory(string filePath)
        {
            if (testOnly || string.IsNullOrWhiteSpace(_item.FilePatternToDeleteBeforeDownload))
                return Task.CompletedTask;

            var dir = Path.GetDirectoryName(filePath)!;

            return DeleteOldVersions(dir, _item.FilePatternToDeleteBeforeDownload, filePath);
        }

        // When download fails, return error message.
        DownloadOnceResult Failed(string errorMessage, DownloadOnceResult downloadOnceResult)
        {
            _item.ErrorMessage = $"When {_item.Status}: {errorMessage}";
            _item.Status = DownloadingStatus.Failed;
            _item.Progress = string.Empty;

            return downloadOnceResult;
        }

        async Task<bool> CallEventScript(string directory, string eventName, string filePath)
        {
            var script = Path.Join(directory, eventName + ".cmd");
            var isBatch = File.Exists(script);
            if (!isBatch)
            {
                script = Path.Join(directory, eventName + ".ps1");
                if (!File.Exists(script))
                    return false;
            }

            _item.Status = DownloadingStatus.RunningEventScript;

            var (fileName, arguments) = isBatch
                // A batch file is not an executable, so it goes through cmd.exe.
                // cmd strips the outermost pair of quotes from /c, hence the extra
                // pair around the whole command line for paths with spaces.
                ? ("cmd.exe", $"/c \"\"{script}\" \"{filePath}\"\"")
                : (
                    "powershell",
                    $"-ExecutionPolicy Bypass -NoProfile -File \"{script}\" \"{filePath}\""
                );

            var exitCode = await RunProcessAsync(
                fileName,
                arguments,
                directory,
                $"{eventName} script {script}"
            );

            // The user put the script there to finish the job; a failure that only
            // showed up as a vanished console window used to pass as success.
            if (exitCode != 0)
                throw new PostProcessException(
                    $"{eventName} script exited with code {exitCode}: {script}"
                );

            return true;
        }
    }

    internal static async Task FinalizeArchiveFile(
        SoftwareItem item,
        string archivePath,
        bool processingSucceeded,
        long knownSize = 0,
        DateTime? lastModified = null
    )
    {
        if (!IsArchiveFile(archivePath) || !File.Exists(archivePath))
            return;

        var info = new FileInfo(archivePath);
        DownloadMetadataStore.Write(
            Path.GetDirectoryName(archivePath)!,
            new DownloadMetadataStore.Entry
            {
                ItemName = item.Name,
                Source = item.WebPage,
                FileName = Path.GetFileName(archivePath),
                Size = knownSize > 0 ? knownSize : info.Length,
                LastModified = lastModified,
            }
        );

        if (processingSucceeded)
            await Task.Run(() => File.Delete(archivePath));
    }

    internal static bool IsArchiveFile(string path) =>
        ArchiveFileTypes.Contains(Path.GetExtension(path).ToLowerInvariant());

    internal static bool TryCompareArchiveMetadata(
        SoftwareItem item,
        string targetFilePath,
        long currentSize,
        DateTime? currentLastModified,
        out string metadataFilePath,
        out bool isSame
    )
    {
        metadataFilePath = string.Empty;
        isSame = false;
        if (
            !IsArchiveFile(targetFilePath)
            || !DownloadMetadataStore.TryGet(
                item.FinalDownloadDirectory,
                item.Name,
                out var metadata
            )
            || string.IsNullOrWhiteSpace(metadata.FileName)
            || !IsArchiveFile(metadata.FileName)
            || !string.Equals(
                Path.GetFileName(metadata.FileName),
                metadata.FileName,
                StringComparison.Ordinal
            )
        )
            return false;

        metadataFilePath = Path.Join(item.FinalDownloadDirectory, metadata.FileName);
        isSame = IsSameDownload(
            currentSize,
            currentLastModified,
            metadata.Size,
            metadata.LastModified
        );
        return true;
    }

    /// <summary>How often the page is asked whether it is ready to be acted on.</summary>
    private const int ReadyPollIntervalMs = 200;

    /// <summary>
    /// Waits for a click target to become clickable, and reports the state it is in when
    /// the wait ends. A plain link is clicked the moment it appears. Anything else only
    /// works if the page's scripts have run, so it waits for the page to settle - but no
    /// longer: once settled, nothing more is coming, and a missing target is waited for to
    /// the last moment because the click can only fail without it.
    /// </summary>
    private async Task<ClickTargetState> WaitForClickTarget(
        string xpath,
        string frameName,
        TimeSpan budget
    )
    {
        var watch = Stopwatch.StartNew();
        var state = ClickTargetState.Missing;

        while (!_item.HasCancelled)
        {
            state = await Browser.ProbeClickTarget(xpath, frameName);
            if (state == ClickTargetState.ReadyLink)
                break;
            if (state != ClickTargetState.Missing && Browser.IsPageSettled)
                break;
            if (watch.Elapsed >= budget)
                break;

            await Task.Delay(ReadyPollIntervalMs);
        }

        return state;
    }

    /// <summary>Waits until the page stops fetching, or the budget runs out.</summary>
    private async Task WaitForPageSettled(TimeSpan budget)
    {
        var watch = Stopwatch.StartNew();
        while (!_item.HasCancelled && !Browser.IsPageSettled && watch.Elapsed < budget)
            await Task.Delay(ReadyPollIntervalMs);
    }

    private static readonly List<string> ExecutableFileTypes = [".exe", ".msi", ".vsix", ".msix"];

    private static readonly List<string> ArchiveFileTypes = [".zip", ".rar", ".7z", ".gz", ".tgz"];

    /// <summary>
    /// Archives that are a tar wrapped in a compressor: 7-Zip peels one layer per
    /// run, so unpacking them takes a second pass over the tar that falls out.
    /// </summary>
    private static readonly List<string> CompressedTarFileTypes = [".gz", ".tgz"];

    internal static bool IsSameDownload(
        long currentSize,
        DateTime? currentLastModified,
        long storedSize,
        DateTime? storedLastModified
    )
    {
        if (currentSize > 0)
            return storedSize == currentSize;

        return currentLastModified.HasValue
            && storedLastModified.HasValue
            && Math.Abs(
                    (currentLastModified.Value - storedLastModified.Value).TotalSeconds
                ) < 2;
    }

    /// <summary>
    /// A step that runs after the file is in place - extraction, an event script -
    /// failed. The download itself is done, so the pipeline reports this without
    /// retrying, and <see cref="DownloadingStatus"/> already names the step.
    /// </summary>
    private sealed class PostProcessException(string message) : Exception(message);

    /// <summary>
    /// Runs a helper process to completion with no console window and returns its
    /// exit code, or -1 when it could not be started. Output is captured and logged
    /// on failure: these run unattended at night, where a window that flashes an
    /// error and closes tells nobody anything.
    /// </summary>
    internal static async Task<int> RunProcessAsync(
        string fileName,
        string arguments,
        string workingDirectory,
        string what
    )
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                Log.ZLogWarning($"{what} could not be started.");
                return -1;
            }

            // Start draining both pipes before waiting: a helper that fills a pipe
            // buffer blocks until somebody reads it, and we would never get there.
            var standardOutput = process.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                var output = string.Join(
                        Environment.NewLine,
                        new[] { await standardOutput, await standardError }.Where(text =>
                            !string.IsNullOrWhiteSpace(text)
                        )
                    )
                    .Trim();
                Log.ZLogWarning($"{what} exited with code {process.ExitCode}: {output}");
            }

            return process.ExitCode;
        }
        catch (Exception ex)
        {
            Log.ZLogWarning($"{what} could not be run: {ex.Message}");
            return -1;
        }
    }

    /// <summary>
    /// Marks a download still in flight. It sits in the destination directory, so
    /// it has to be recognisable there: never offered as the finished file, never
    /// swept up by a delete pattern.
    /// </summary>
    /// <summary>
    /// More matches than one item's download history could plausibly be. Past this
    /// the pattern is not describing old versions any more - it is describing
    /// somebody else's folder.
    /// </summary>
    private const int MaxOldVersionsToDelete = 10;

    /// <summary>
    /// Picks the files <paramref name="pattern"/> claims are this item's earlier
    /// downloads, keeping <paramref name="keepFile"/>. The pattern is the whole
    /// contract: it is written to name this item's files and nothing else, which
    /// is what lets several items share a folder. The only thing checked here is
    /// that it has not obviously stopped describing one item's history, which is
    /// the shape of a pattern aimed at a general downloads folder.
    ///
    /// Returns what it deleted, for the log and for the tests.
    /// </summary>
    internal static IReadOnlyList<string> SelectOldVersions(
        string directory,
        string pattern,
        string keepFile
    )
    {
        if (string.IsNullOrWhiteSpace(pattern) || !Directory.Exists(directory))
            return [];

        var doomed = Directory
            .GetFiles(directory, pattern)
            .Where(file => !string.Equals(file, keepFile, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (doomed.Count > MaxOldVersionsToDelete)
        {
            Log.ZLogWarning(
                $"Keeping the {doomed.Count} files matching '{pattern}' in {directory}: "
                    + $"more than one item's old versions should be"
            );
            return [];
        }

        return doomed;
    }

    /// <inheritdoc cref="SelectOldVersions"/>
    internal static Task<IReadOnlyList<string>> DeleteOldVersions(
        string directory,
        string pattern,
        string keepFile
    )
    {
        // Run on a background thread to avoid blocking the UI while scanning /
        // deleting files.
        return Task.Run<IReadOnlyList<string>>(() =>
        {
            try
            {
                var doomed = SelectOldVersions(directory, pattern, keepFile);

                foreach (var file in doomed)
                    File.Delete(file);

                if (doomed.Count > 0)
                    Log.ZLogInformation(
                        $"Deleted {doomed.Count} old file(s) in {directory}: "
                            + $"{string.Join(", ", doomed.Select(Path.GetFileName))}"
                    );

                return doomed;
            }
            catch (Exception ex)
            {
                Log.ZLogError(ex, $"Failed to delete other files in {directory}");
                return [];
            }
        });
    }

    private const int DeleteStagedFileAttempts = 10;

    private const int DeleteStagedFileIntervalMs = 500;

    /// <summary>
    /// Removes the file left in the staging folder, tolerating the brief lock
    /// WebView2's download scan holds after a transfer ends. Gives up after a few
    /// seconds rather than retrying forever: a leftover staged file is untidy, but
    /// a delete that never returns stalls every remaining item in the run.
    /// Returns whether the file is gone.
    /// </summary>
    internal static async Task<bool> DeleteStagedFile(string path)
    {
        for (var attempt = 1; attempt <= DeleteStagedFileAttempts; attempt++)
        {
            try
            {
                if (!File.Exists(path))
                    return true;

                File.Delete(path);
                return true;
            }
            catch when (attempt < DeleteStagedFileAttempts)
            {
                await Task.Delay(DeleteStagedFileIntervalMs);
            }
            catch (Exception ex)
            {
                Log.ZLogWarning(
                    $"Gave up deleting the staged file {path} after {DeleteStagedFileAttempts} attempts: {ex.Message}"
                );
            }
        }

        return false;
    }

    private static Task<bool> CopyFileIfChanged(
        string sourceFile,
        string targetFile,
        bool move = false
    )
    {
        // Run synchronous file I/O on a background thread so the UI thread
        // is not blocked while moving / copying potentially large files.
        return Task.Run(() =>
        {
            var sourceFileInfo = new FileInfo(sourceFile);
            var targetFileInfo = new FileInfo(targetFile);

            // Ignore same file.
            if (targetFileInfo.Exists)
            {
                if (
                    sourceFileInfo.Length == targetFileInfo.Length
                    && sourceFileInfo.LastWriteTime == targetFileInfo.LastWriteTime
                )
                    return false;
            }

            // Copy / move sourceFile to targetFile.
            if (move)
            {
                try
                {
                    File.Move(sourceFile, targetFile, true);
                }
                catch
                {
                    // WebView2 download safe check may lock the file. Try to copy.
                    File.Copy(sourceFile, targetFile, true);
                }
            }
            else
            {
                File.Copy(sourceFile, targetFile, true);
            }

            File.SetLastWriteTime(targetFile, sourceFileInfo.LastWriteTime);

            return true;
        });
    }

    private static readonly string SevenZipPath = Path.Combine(
        AppDomain.CurrentDomain.SetupInformation.ApplicationBase!,
        "7-Zip",
        "7z.exe"
    );

    /// <summary>
    /// Runs only the extraction step, so it can be exercised end to end without
    /// downloading anything first.
    /// </summary>
    internal static Task ExtractOnly(SoftwareItem item, string archiveFile) =>
        new DownloadPipeline(item, testOnly: false).ExtractArchiveFile(archiveFile);

    private async Task<bool> ExtractArchiveFile(string archiveFile)
    {
        if (!_item.ExtractAfterDownload)
            return false;

        if (!IsArchiveFile(archiveFile))
            return false;

        var archiveDir = Path.GetDirectoryName(archiveFile)!;
        // Older or hand-edited recipes can deserialize an empty optional column
        // as null. Treat it as no filter before passing it to Directory.GetFiles.
        var pattern = _item.FilePatternToDeleteBeforeExtraction ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(pattern))
            await Task.Run(() =>
                Directory.GetFiles(archiveDir, pattern).ToList().ForEach(File.Delete)
            );

        _item.Status = DownloadingStatus.Extracting;

        await RunSevenZip(archiveFile, archiveDir);

        // A .tar.gz only gives up its tar on the first pass. Unpack that tar too and
        // delete it, so the download directory does not end up holding an archive the
        // recipe never asked to keep.
        if (CompressedTarFileTypes.Contains(Path.GetExtension(archiveFile).ToLowerInvariant()))
        {
            var tarFile =
                FindUnwrappedTarFile(archiveDir, archiveFile)
                ?? throw new PostProcessException(
                    $"No tar file appeared after unpacking {archiveFile}"
                );

            await RunSevenZip(tarFile, archiveDir);
            await Task.Run(() => File.Delete(tarFile));
        }

        if (_item.ExtractToRoot)
        {
            // Flattening leaves the archive's empty directory entries behind.
            await Task.Run(() =>
            {
                foreach (var subDirectory in Directory.GetDirectories(archiveDir))
                    if (
                        Directory.GetFiles(subDirectory).Length == 0
                        && Directory.GetDirectories(subDirectory).Length == 0
                    )
                        Directory.Delete(subDirectory);
            });
        }

        return true;
    }

    private async Task RunSevenZip(string archiveFile, string archiveDir)
    {
        var extractCommand = _item.ExtractToRoot ? "e" : "x";
        var exitCode = await RunProcessAsync(
            SevenZipPath,
            $@"{extractCommand} -y -o""{archiveDir}"" ""{archiveFile}"" -r",
            archiveDir,
            $"7-Zip extracting {Path.GetFileName(archiveFile)}"
        );

        // 7-Zip reports 1 for non-fatal warnings, such as a file it could not read;
        // 2 and up are real failures, and -1 means it never started.
        if (exitCode < 0 || exitCode >= 2)
            throw new PostProcessException($"7-Zip exited with code {exitCode}: {archiveFile}");
    }

    /// <summary>
    /// Locates the tar 7-Zip just wrote next to its compressed original. The name comes
    /// from the compressed member, which is <c>foo.tar</c> for both <c>foo.tar.gz</c> and
    /// <c>foo.tgz</c> - but the .tgz spelling can also arrive as a bare <c>foo</c>.
    /// </summary>
    private static string? FindUnwrappedTarFile(string archiveDir, string archiveFile)
    {
        var stem = Path.Combine(archiveDir, Path.GetFileNameWithoutExtension(archiveFile));

        foreach (var candidate in new[] { stem, stem + ".tar" })
            if (File.Exists(candidate))
                return candidate;

        return null;
    }
}
