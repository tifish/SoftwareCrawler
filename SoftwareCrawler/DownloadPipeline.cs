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
            if (!string.IsNullOrWhiteSpace(Settings.Proxy))
                handler.Proxy = new WebProxy(Settings.Proxy);

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
                _item.Status = DownloadingStatus.WaitingForLoadEnd;
                for (var seconds = 0; seconds < Settings.LoadPageEndTimeout; seconds++)
                {
                    if (_item.HasCancelled)
                        return DownloadOnceResult.FailedAndNoRetry;
                    if (await Browser.WaitForMainFrameLoadEnd(TimeSpan.FromSeconds(1)))
                        break;
                }

                // Some script may be executed after page loaded, wait for it.
                await Task.Delay((_item.WaitSecondsBeforeClick + 1) * 1000);

                // If still not loaded, try to click the link directly.
                Browser.PrepareLoadEvents();

                var xpathOrScript = xpathOrScripts[i];
                var frameName = i < frameNames.Count ? frameNames[i] : string.Empty;

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

                    // Then click
                    if (
                        !await Browser.TryClick(
                            xpathOrScript,
                            frameName,
                            Settings.TryClickCount,
                            Settings.TryClickInterval * 1000
                        )
                    )
                        return Failed(
                            $"Failed to click, error: {Browser.LastJavascriptError}",
                            DownloadOnceResult.FailedAndRetry
                        );
                }
                else // Is JavaScript
                {
                    _item.Status = DownloadingStatus.ExecutingScript;
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
            downloadedFilePath = StagingPathFor(targetFilePath);

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
                    // An interrupted attempt is not a previous version to compare against.
                    .FirstOrDefault(file =>
                        !file.EndsWith(PartialSuffix, StringComparison.OrdinalIgnoreCase)
                    );
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
                bool isSameFile;
                if (downloadFileSize > 0)
                    isSameFile = fileInfo.Length == downloadFileSize;
                else if (downloadFileTime.HasValue)
                    isSameFile =
                        Math.Abs((fileInfo.LastWriteTime - downloadFileTime.Value).TotalSeconds)
                        < 2;
                else
                    isSameFile = false;

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

            // Clear out what an interrupted attempt left behind, or the browser
            // would download to "name.exe (1).partial" instead.
            try
            {
                if (File.Exists(downloadedFilePath))
                    File.Delete(downloadedFilePath);
            }
            catch (Exception ex)
            {
                Log.ZLogWarning($"Could not remove the stale {downloadedFilePath}: {ex.Message}");
            }

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
                // Delete other old files in the same directory.
                await DeleteOtherFilesInSameDirectory(targetFilePath);

                // Copy file from downloading folder to target directory.
                if (File.Exists(downloadedFilePath))
                {
                    if (downloadFileTime.HasValue)
                        File.SetLastWriteTime(downloadedFilePath, downloadFileTime.Value);

                    if (await CopyFileIfChanged(downloadedFilePath, targetFilePath, true))
                        await CallEventScript(
                            _item.FinalDownloadDirectory,
                            "AfterDownload",
                            targetFilePath
                        );
                }

                // Extract target file and copy to download directory 2.
                if (File.Exists(targetFilePath))
                {
                    if (downloadFileTime.HasValue)
                        File.SetLastWriteTime(targetFilePath, downloadFileTime.Value);

                    // Extract only if the file is newly downloaded.
                    if (finalStatus == DownloadingStatus.Downloaded)
                    {
                        await ExtractArchiveFile(targetFilePath);
                        await CallEventScript(
                            _item.FinalDownloadDirectory,
                            "AfterExtract",
                            targetFilePath
                        );
                    }

                    // Copy file from downloading folder to download directory 2.
                    if (!string.IsNullOrEmpty(_item.DownloadDirectory2))
                    {
                        var targetFile2 = Path.Combine(_item.DownloadDirectory2, suggestedFileName);
                        // Delete other old files in the same directory.
                        await DeleteOtherFilesInSameDirectory(targetFile2);

                        // Copy file from download directory 1 to download directory 2.
                        if (await CopyFileIfChanged(targetFilePath, targetFile2))
                            await CallEventScript(_item.DownloadDirectory2, "AfterDownload", targetFile2);

                        // Extract only if the file is newly downloaded.
                        if (finalStatus == DownloadingStatus.Downloaded)
                        {
                            await ExtractArchiveFile(targetFile2);
                            await CallEventScript(_item.DownloadDirectory2, "AfterExtract", targetFile2);
                        }
                    }
                }
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

        async Task CallEventScript(string directory, string eventName, string filePath)
        {
            var script = Path.Join(directory, eventName + ".cmd");
            var isBatch = File.Exists(script);
            if (!isBatch)
            {
                script = Path.Join(directory, eventName + ".ps1");
                if (!File.Exists(script))
                    return;
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
        }
    }

    private static readonly List<string> ExecutableFileTypes = [".exe", ".msi", ".vsix", ".msix"];

    private static readonly List<string> ArchiveFileTypes = [".zip", ".rar", ".7z"];

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
    internal const string PartialSuffix = ".partial";

    /// <summary>
    /// Where the bytes go while they are arriving.
    ///
    /// A local destination is staged in place: finishing is then a rename rather
    /// than a copy, which for a 3 GB installer heading to another volume is the
    /// difference between instant and writing it twice.
    ///
    /// A network destination is not, because streaming a download straight onto a
    /// share means every write crosses the wire and one blip loses the transfer.
    /// Those keep the old route - land locally, copy once when it is complete.
    /// </summary>
    internal static string StagingPathFor(string targetFilePath) =>
        IsNetworkPath(Path.GetDirectoryName(targetFilePath))
            ? Path.Combine(SoftwareItem.SystemDownloadFolder, Path.GetFileName(targetFilePath))
            : targetFilePath + PartialSuffix;

    /// <summary>True for a UNC path or a drive letter mapped to a share.</summary>
    internal static bool IsNetworkPath(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return false;

        try
        {
            var full = Path.GetFullPath(directory);
            if (full.StartsWith(@"\\", StringComparison.Ordinal))
                return true;

            var root = Path.GetPathRoot(full);
            return !string.IsNullOrEmpty(root)
                && new DriveInfo(root).DriveType == DriveType.Network;
        }
        catch
        {
            // An unreadable path is not worth guessing about; treat it as local
            // and let the download itself report the real problem.
            return false;
        }
    }

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
            // The download in flight lives here too, and a pattern like "name.*"
            // would happily match it.
            .Where(file => !file.EndsWith(PartialSuffix, StringComparison.OrdinalIgnoreCase))
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

    private async Task ExtractArchiveFile(string archiveFile)
    {
        if (!_item.ExtractAfterDownload)
            return;

        if (!ArchiveFileTypes.Contains(Path.GetExtension(archiveFile).ToLower()))
            return;

        var archiveDir = Path.GetDirectoryName(archiveFile)!;
        var pattern = _item.FilePatternToDeleteBeforeExtractionAndExtractOnly;

        if (pattern != "")
            await Task.Run(() =>
                Directory.GetFiles(archiveDir, pattern).ToList().ForEach(File.Delete)
            );

        _item.Status = DownloadingStatus.Extracting;

        // extract files to root directory.
        var exitCode = await RunProcessAsync(
            SevenZipPath,
            $@"e -y -o""{archiveDir}"" ""{archiveFile}"" {pattern} -r",
            archiveDir,
            $"7-Zip extracting {Path.GetFileName(archiveFile)}"
        );

        // 7-Zip reports 1 for non-fatal warnings, such as a file it could not read;
        // 2 and up are real failures, and -1 means it never started.
        if (exitCode < 0 || exitCode >= 2)
            throw new PostProcessException($"7-Zip exited with code {exitCode}: {archiveFile}");

        // Delete empty sub-directories in archiveDir
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
}
