using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using CefSharp;

namespace SoftwareCrawler;

public sealed class SoftwareItem : INotifyPropertyChanged
{
    private class NonSerializedAttribute : Attribute
    {
    }

    private DownloadStatus _status;

    [NonSerialized]
    public DownloadStatus Status
    {
        get => _status;
        set
        {
            if (_status != value)
            {
                _status = value;

                if (SynchronizationContext.Current == _uiSynchronizationContext)
                    OnPropertyChanged();
                else
                    _uiSynchronizationContext?.Post(_ => { OnPropertyChanged(); }, null);
            }
        }
    }

    private string _progress = string.Empty;

    [NonSerialized]
    public string Progress
    {
        get => _progress;
        private set
        {
            if (_progress != value)
            {
                _progress = value;

                if (SynchronizationContext.Current == _uiSynchronizationContext)
                    OnPropertyChanged();
                else
                    _uiSynchronizationContext?.Post(_ => { OnPropertyChanged(); }, null);
            }
        }
    }

    public bool Enabled { get; set; } = true;
    public string Name { get; set; } = string.Empty;
    public string WebPage { get; set; } = string.Empty;
    public string XPathOrScripts { get; set; } = string.Empty;
    public string Frames { get; set; } = string.Empty;
    public bool UseProxy { get; set; }
    public string DownloadDirectory { get; set; } = string.Empty;
    [Browsable(false)]
    public string FinalDownloadDirectory
    {
        get
        {
            var validName = string.Join("", Name.Split(Path.GetInvalidFileNameChars()));

            var downloadDirectory = DownloadDirectory;
            if (string.IsNullOrWhiteSpace(downloadDirectory))
            {
                if (string.IsNullOrEmpty(Settings.DefaultDownloadDirectory))
                    downloadDirectory = SystemDownloadFolder;
                else
                    downloadDirectory = Settings.DefaultDownloadDirectory;

                downloadDirectory = Path.Join(downloadDirectory, validName);
            }

            return downloadDirectory;
        }
    }
    public string DownloadDirectory2 { get; set; } = string.Empty;
    public string FilePatternToDeleteBeforeDownload { get; set; } = string.Empty;
    public bool ExtractAfterDownload { get; set; }
    public string FilePatternToDeleteBeforeExtractionAndExtractOnly { get; set; } = string.Empty;

    private string _errorMessage = string.Empty;

    [NonSerialized]
    public string ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (_errorMessage != value)
            {
                _errorMessage = value;

                if (SynchronizationContext.Current == _uiSynchronizationContext)
                    OnPropertyChanged();
                else
                    _uiSynchronizationContext?.Post(_ => { OnPropertyChanged(); }, null);
            }
        }
    }

    public SoftwareItem()
    {
    }

    public SoftwareItem(string dataLine, string extraLine)
    {
        FromDataLine(dataLine, DataProperties);
        FromDataLine(extraLine, ExtraProperties);
    }

    public static readonly List<PropertyInfo> DataProperties = [.. new []
    {
        nameof(Enabled),
        nameof(Name),
        nameof(WebPage),
        nameof(XPathOrScripts),
        nameof(Frames),
        nameof(UseProxy),
        nameof(FilePatternToDeleteBeforeDownload),
        nameof(ExtractAfterDownload),
        nameof(FilePatternToDeleteBeforeExtractionAndExtractOnly),
    }.Select(name => typeof(SoftwareItem).GetProperty(name)!).ToList()];

    public static readonly List<PropertyInfo> ExtraProperties = [.. new []
    {
        nameof(DownloadDirectory),
        nameof(DownloadDirectory2),
    }.Select(name => typeof(SoftwareItem).GetProperty(name)!).ToList()];

    public static string GetDataHeaderLine(List<PropertyInfo> properties)
    {
        return string.Join('\t', properties.Select(prop => prop.Name));
    }

    public void FromDataLine(string line, List<PropertyInfo> properties)
    {
        var items = line.Split('\t');
        if (items.Length != properties.Count)
            throw new Exception("items.Length != properties.Count");

        for (var i = 0; i < items.Length; i++)
        {
            var prop = properties[i];
            var item = items[i];
            if (prop.PropertyType == typeof(string))
            {
                prop.SetValue(this, item);
            }
            else if (prop.PropertyType == typeof(int))
            {
                if (!int.TryParse(item, out var value))
                    value = 0;
                prop.SetValue(this, value);
            }
            else if (prop.PropertyType == typeof(bool))
            {
                prop.SetValue(this,
                    item.ToLower() switch
                    {
                        "true" or "1" => true,
                        _ => false,
                    });
            }
        }
    }

    public string ToDataLine(List<PropertyInfo> properties)
    {
        var items = properties.Select(prop =>
            {
                var value = prop.GetValue(this);

                if (prop.PropertyType == typeof(string))
                    return (string)(value ?? string.Empty);

                if (prop.PropertyType == typeof(int))
                {
                    var intValue = (int)value!;
                    return intValue switch
                    {
                        0 => string.Empty,
                        _ => intValue.ToString(),
                    };
                }

                if (prop.PropertyType == typeof(bool))
                    return (bool)value! switch
                    {
                        true => "true",
                        false => string.Empty,
                    };

                throw new Exception("Unexpected property type.");
            });

        return string.Join('\t', items);
    }

    private SynchronizationContext? _uiSynchronizationContext;

    private enum BeginDownloadResult
    {
        NoDownload,
        Failed,
        Downloaded,
        HasUpdate,
        Started,
    }

    public static readonly string SystemDownloadFolder = KnownFolders.GetPath(KnownFolder.Downloads);

    private bool _hasCancelled;

    private static readonly List<string> ExecutableFileTypes = [".exe", ".msi"];
    private static readonly List<string> ArchiveFileTypes = [".zip", ".rar", ".7z"];

    public async Task<bool> Download(bool testOnly = false, int retryCount = 0)
    {
        if (!Enabled)
            return true;

        _hasCancelled = false;

        for (var i = 0; i < retryCount + 1; i++)
        {
            if (_hasCancelled)
                return false;

            await Cef.UIThreadTaskFactory.StartNew(() =>
            {
                var proxyDict = new Dictionary<string, object>();
                if (UseProxy && !string.IsNullOrEmpty(Settings.Proxy))
                {
                    proxyDict["mode"] = "fixed_servers";
                    proxyDict["server"] = Settings.Proxy;
                }

                if (!Browser.WebBrowser.GetBrowserHost().RequestContext.SetPreference(
                        "proxy", proxyDict, out var error))
                    Logger.Error("Set proxy error: {Error}", error!);
            });

            var success = await DownloadOnce(testOnly);
            if (success)
            {
                Logger.Information("Download {Name} successfully, retryCount={RetryCount}", Name, i);
                return true;
            }

            if (_hasCancelled)
                return false;

            await Task.Delay(Settings.DownloadRetryInterval * 1000);
        }

        Logger.Warning("Download {Name} failed, retryCount={RetryCount}, error={ErrorMessage}", Name, retryCount, ErrorMessage);
        return false;
    }

    // 1-based download id from browser
    private int _downloadingId;

    private async Task<bool> DownloadOnce(bool testOnly = false)
    {
        // Initialize
        _uiSynchronizationContext = SynchronizationContext.Current;

        Status = DownloadStatus.Preparing;
        ErrorMessage = string.Empty;

        if (string.IsNullOrEmpty(FinalDownloadDirectory))
            return Failed("Download directory is empty.");

        if (!Directory.Exists(FinalDownloadDirectory))
            try
            {
                Directory.CreateDirectory(FinalDownloadDirectory);
            }
            catch (Exception)
            {
                return Failed("Download directory does not exist, and failed to create.");
            }

        if (DownloadDirectory2 != "" && !Directory.Exists(DownloadDirectory2))
            try
            {
                Directory.CreateDirectory(DownloadDirectory2);
            }
            catch (Exception)
            {
                return Failed("Download directory 2 does not exist, and failed to create.");
            }

        var downloadFileName = string.Empty;
        var downloadFileSize = 0L;
        DateTime? downloadFileTime = null;
        var targetFilePath = string.Empty;
        var downloadingFilePath = string.Empty;
        var beginDownloadResult = BeginDownloadResult.NoDownload;

        Browser.BeginDownloadHandler += OnBeginDownloadHandler;
        Browser.DownloadProgressHandler += OnDownloadProgressHandler;

        // Download
        try
        {
            await Browser.Load("about:blank");

            // Access download page.
            await Browser.Load(WebPage);

            // Click links, last link is the download link.
            if (!await ClickAndTriggerDownload())
                return false;

            // Wait for download to start.
            Status = DownloadStatus.WaitingForDownload;
            var waitCounter = Settings.StartDownloadTimeout * 2;
            while (beginDownloadResult == BeginDownloadResult.NoDownload)
            {
                if (_hasCancelled)
                    return false;

                await Task.Delay(500);
                waitCounter--;
                if (waitCounter == 0)
                    return Failed("Failed to start download.");
            }

            // Wrong file name or already downloaded.
            switch (beginDownloadResult)
            {
                case BeginDownloadResult.Failed:
                    return false;
                case BeginDownloadResult.Downloaded:
                    return await Downloaded(DownloadStatus.SameFileAlreadyDownloaded);
                case BeginDownloadResult.HasUpdate:
                    return await Downloaded(DownloadStatus.HasUpdate);
            }

            // Wait for download to complete.
            Status = DownloadStatus.Downloading;
            if (!await Browser.WaitForDownloaded(TimeSpan.FromSeconds(Settings.DownloadTimeout)))
                return Failed("Failed to download file.");

            return await Downloaded(DownloadStatus.Downloaded);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Download {Name} failed", Name);
            return Failed(ex.Message);
        }
        finally
        {
            if (!testOnly && !string.IsNullOrEmpty(downloadingFilePath))
                while (true)
                {
                    try
                    {
                        if (File.Exists(downloadingFilePath))
                            File.Delete(downloadingFilePath);
                    }
                    catch
                    {
                        await Task.Delay(500);
                        continue;
                    }

                    break;
                }

            _uiSynchronizationContext = null;
            _downloadingId = -1;
            Browser.BeginDownloadHandler -= OnBeginDownloadHandler;
            Browser.DownloadProgressHandler -= OnDownloadProgressHandler;
        }

        async Task<bool> ClickAndTriggerDownload()
        {
            var xPathOrScripts = string.IsNullOrWhiteSpace(XPathOrScripts)
                ? []
                : XPathOrScripts.Replace("`n", "\n")
                    .Split('`')
                    .Select(x => x.Trim())
                    .ToList();
            var frameNames = string.IsNullOrWhiteSpace(Frames)
                ? []
                : Frames.Split('`')
                    .Select(x => x.Trim())
                    .ToList();

            for (var i = 0; i < xPathOrScripts.Count; i++)
            {
                Status = DownloadStatus.WaitingForLoadEnd;
                for (var seconds = 0; seconds < Settings.LoadPageEndTimeout; seconds++)
                {
                    if (_hasCancelled)
                        return false;
                    if (await Browser.WaitForMainFrameLoadEnd(TimeSpan.FromSeconds(1)))
                        goto MainFrameLoadEndBeforeClick;
                }

                return Failed("Failed to wait for page load end before click.");
            MainFrameLoadEndBeforeClick:;

                Browser.PrepareLoadEvents();

                var xpathOrScript = xPathOrScripts[i];
                var frameName = i < frameNames.Count ? frameNames[i] : string.Empty;

                // Is XPath
                if (xpathOrScript.StartsWith("//") && xpathOrScript.Length >= 3 && char.IsLetter(xpathOrScript[2]))
                {
                    Status = DownloadStatus.Clicking;

                    // Scroll to the element first
                    var scrollScript = $"""
                                        const element = document.evaluate("{xpathOrScript}", document, null, XPathResult.FIRST_ORDERED_NODE_TYPE, null).singleNodeValue;
                                        const elementRect = element.getBoundingClientRect();
                                        const absoluteElementTop = elementRect.top + window.pageYOffset;
                                        const middle = absoluteElementTop - (window.innerHeight / 2);
                                        window.scrollTo(0, middle);
                                        """;
                    if (!await Browser.TryEvaluateJavascript(scrollScript, frameName))
                        Logger.Error("Failed to scroll to, error: {LastJavascriptError}", Browser.LastJavascriptError);

                    // Then click
                    if (!await Browser.TryClick(xpathOrScript, frameName,
                            Settings.TryClickCount, Settings.TryClickInterval * 1000))
                        return Failed($"Failed to click, error: {Browser.LastJavascriptError}");
                }
                else // Is JavaScript
                {
                    Status = DownloadStatus.ExecutingScript;
                    if (!await Browser.TryEvaluateJavascript(xpathOrScript, frameName))
                        return Failed($"Failed to execute script: {Browser.LastJavascriptError}");
                }
            }

            return true;
        }

        // Called when download starts, decide whether to download.
        void OnBeginDownloadHandler(object? o, DownloadItem item)
        {
            // Download to system download folder first, then move to download directory.
            downloadingFilePath = Path.Combine(SystemDownloadFolder, item.SuggestedFileName);
            var ext = Path.GetExtension(item.SuggestedFileName).ToLower();

            // Download is in progress, a new download begins
            if (_downloadingId > 0)
            {
                // Use a new file to start the new download. The old file will be deleted by browser later.
                var fileNameNoExt = Path.GetFileNameWithoutExtension(item.SuggestedFileName);
                downloadingFilePath = Path.Combine(SystemDownloadFolder, $"{fileNameNoExt}_{_downloadingId}{ext}");
            }

            _downloadingId = item.Id;
            downloadFileName = item.SuggestedFileName;
            downloadFileSize = item.TotalBytes;
            downloadFileTime = item.EndTime;

            if (!ExecutableFileTypes.Contains(ext) && !ArchiveFileTypes.Contains(ext))
            {
                Failed($"Unexpected file name: {downloadFileName}");
                beginDownloadResult = BeginDownloadResult.Failed;
                item.IsCancelled = true;
                return;
            }

            targetFilePath = Path.Join(FinalDownloadDirectory, downloadFileName);

            // Compare file size to determine download or not.
            // Epic Launcher download page may change its file name for each download.
            // Find the old file and check the size.
            var oldFile = targetFilePath;
            if (!File.Exists(oldFile) && !string.IsNullOrWhiteSpace(FilePatternToDeleteBeforeDownload))
                oldFile = Directory.GetFiles(FinalDownloadDirectory, FilePatternToDeleteBeforeDownload).FirstOrDefault();
            if (File.Exists(oldFile))
            {
                var fileInfo = new FileInfo(oldFile);
                if (fileInfo.Length == downloadFileSize)
                {
                    beginDownloadResult = BeginDownloadResult.Downloaded;
                    targetFilePath = oldFile;
                    downloadFileName = Path.GetFileName(targetFilePath);
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
            item.FullPath = downloadingFilePath;
        }

        // Called when download progress changes.
        void OnDownloadProgressHandler(object? o, DownloadItem item)
        {
            // Download file name may change if same file exists.
            downloadingFilePath = item.FullPath;

            Progress = $"{item.SuggestedFileName} - {item.PercentComplete:00}% - {item.ReceivedBytes:#,###} / {item.TotalBytes:#,###} Bytes - {item.CurrentSpeed / 1024:#,###} KB/s";
        }

        // When download is complete, move file to target directory.
        async Task<bool> Downloaded(DownloadStatus finalStatus)
        {
            Status = finalStatus;

            Progress = $"{downloadFileName} - {(double)downloadFileSize:#,###} Bytes";

            if (testOnly)
                return true;

            if (File.Exists(downloadingFilePath))
            {
                DeleteOtherFilesInSameDirectory(targetFilePath);

                if (downloadFileTime.HasValue)
                    File.SetLastWriteTime(downloadingFilePath, downloadFileTime.Value);

                await CopyFileIfChanged(downloadingFilePath, targetFilePath, true);
            }

            if (File.Exists(targetFilePath))
            {
                if (downloadFileTime.HasValue)
                    File.SetLastWriteTime(targetFilePath, downloadFileTime.Value);
                await ExtractArchiveFile(targetFilePath);
            }

            if (!string.IsNullOrEmpty(DownloadDirectory2))
            {
                var targetFile2 = Path.Combine(DownloadDirectory2, downloadFileName);
                DeleteOtherFilesInSameDirectory(targetFile2);
                await CopyFileIfChanged(targetFilePath, targetFile2);
                await ExtractArchiveFile(targetFile2);
            }

            return true;
        }

        void DeleteOtherFilesInSameDirectory(string filePath)
        {
            if (testOnly || string.IsNullOrWhiteSpace(FilePatternToDeleteBeforeDownload))
                return;

            var dir = Path.GetDirectoryName(filePath)!;

            try
            {
                Directory.GetFiles(dir, FilePatternToDeleteBeforeDownload)
                    .Where(file => string.Compare(file, filePath, StringComparison.OrdinalIgnoreCase) != 0)
                    .ToList().ForEach(File.Delete);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to delete other files in {Directory}", dir);
            }
        }

        // When download fails, return error message.
        bool Failed(string errorMessage)
        {
            Status = DownloadStatus.Failed;
            ErrorMessage = errorMessage;
            Progress = string.Empty;

            return false;
        }
    }

    private static async Task CopyFileIfChanged(string sourceFile, string targetFile, bool move = false)
    {
        var sourceFileInfo = new FileInfo(sourceFile);
        var targetFileInfo = new FileInfo(targetFile);

        if (targetFileInfo.Exists)
            if (sourceFileInfo.Length == targetFileInfo.Length && sourceFileInfo.LastWriteTime == targetFileInfo.LastWriteTime)
                return;

        await using (var source = File.Open(sourceFile, FileMode.Open, FileAccess.Read))
        {
            await using (var target = File.Create(targetFile))
            {
                await source.CopyToAsync(target);
            }
        }

        if (move)
            File.Delete(sourceFile);

        File.SetLastWriteTime(targetFile, sourceFileInfo.LastWriteTime);
    }

    private static readonly string SevenZipPath = Path.Combine(AppDomain.CurrentDomain.SetupInformation.ApplicationBase!, "7z.exe");

    private async Task ExtractArchiveFile(string archiveFile)
    {
        if (!ExtractAfterDownload)
            return;

        if (!ArchiveFileTypes.Contains(Path.GetExtension(archiveFile).ToLower()))
            return;

        var archiveDir = Path.GetDirectoryName(archiveFile)!;
        if (FilePatternToDeleteBeforeExtractionAndExtractOnly != "")
            Directory.GetFiles(archiveDir, FilePatternToDeleteBeforeExtractionAndExtractOnly)
                .ToList().ForEach(File.Delete);

        // extract files to root directory.
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = SevenZipPath,
            Arguments = $@"e -y -o""{archiveDir}"" ""{archiveFile}"" {FilePatternToDeleteBeforeExtractionAndExtractOnly} -r",
            UseShellExecute = true,
        });

        if (process == null)
            return;

        await process.WaitForExitAsync();

        // Delete empty sub-directories in archiveDir
        foreach (var subDirectory in Directory.GetDirectories(archiveDir))
            if (Directory.GetFiles(subDirectory).Length == 0 && Directory.GetDirectories(subDirectory).Length == 0)
                Directory.Delete(subDirectory);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public void ResetStatus()
    {
        Status = DownloadStatus.Idle;
        Progress = string.Empty;
        ErrorMessage = string.Empty;
    }

    public void CancelDownload()
    {
        _hasCancelled = true;
        Browser.Cancel();

        Status = DownloadStatus.Cancelled;
    }
}

public enum DownloadStatus
{
    Idle,
    Preparing,
    WaitingForLoadEnd,
    Clicking,
    ExecutingScript,
    WaitingForDownload,
    Downloading,
    SameFileAlreadyDownloaded,
    Downloaded,
    HasUpdate,
    Failed,
    Cancelled,
}
