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
                _uiSynchronizationContext?.Post(_ => { OnPropertyChanged(nameof(DownloadStatus)); }, null);
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

                _uiSynchronizationContext?.Post(_ => { OnPropertyChanged(); }, null);
            }
        }
    }

    public bool Enabled { get; set; } = true;
    public string Name { get; set; } = string.Empty;
    public string WebPage { get; set; } = string.Empty;
    public string XPathOrScripts { get; set; } = string.Empty;
    public string Frames { get; set; } = string.Empty;
    public bool ClickAfterLoaded { get; set; } = false;
    public bool UseProxy { get; set; } = false;
    public string DownloadDirectory { get; set; } = string.Empty;
    public string DownloadDirectory2 { get; set; } = string.Empty;
    public string FilePatternToDelete { get; set; } = string.Empty;
    public bool ExtractAfterDownload { get; set; } = false;
    public string FilePatternToDeleteBeforeExtraction { get; set; } = string.Empty;

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
                _uiSynchronizationContext?.Post(_ => { OnPropertyChanged(); }, null);
            }
        }
    }

    public SoftwareItem()
    {
    }

    public SoftwareItem(string line)
    {
        FromTabSplitLine(line);
    }

    private static List<PropertyInfo>? _serializableProperties;

    private static List<PropertyInfo> SerializableProperties
    {
        get
        {
            if (_serializableProperties == null)
            {
                var type = typeof(SoftwareItem);
                _serializableProperties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .Where(prop => prop.CanWrite && prop.CanRead)
                    .Where(prop => prop.GetCustomAttribute<NonSerializedAttribute>() == null)
                    .ToList();
            }

            return _serializableProperties;
        }
    }

    public static string GetHeaderLine()
    {
        return string.Join('\t', SerializableProperties.Select(prop => prop.Name));
    }

    public void FromTabSplitLine(string line)
    {
        var items = line.Split('\t');
        if (items.Length != SerializableProperties.Count)
            throw new Exception("items.Length != SerializableProperties.Count");

        for (var i = 0; i < items.Length; i++)
        {
            var prop = SerializableProperties[i];
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

    public string ToTabSplitLine()
    {
        var items = SerializableProperties.Select(prop =>
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

    private static readonly List<string> ExecutableFileTypes = new() { ".exe", ".msi" };
    private static readonly List<string> ArchiveFileTypes = new() { ".zip", ".rar", ".7z" };

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

        Status = DownloadStatus.Downloading;
        ErrorMessage = string.Empty;

        if (DownloadDirectory == "")
            return Failed("Download directory is empty.");
        if (!Directory.Exists(DownloadDirectory))
            try
            {
                Directory.CreateDirectory(DownloadDirectory);
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
                ? new List<string>()
                : XPathOrScripts.Split('`')
                    .Select(x => x.Trim())
                    .ToList();
            var frameNames = string.IsNullOrWhiteSpace(Frames)
                ? new List<string>()
                : Frames.Split('`')
                    .Select(x => x.Trim())
                    .ToList();

            var url = "about:blank";

            for (var i = 0; i < xPathOrScripts.Count; i++)
            {
                // Wait for URL change
                while (Browser.WebBrowser.Address == url)
                {
                    await Task.Delay(100);
                    if (_hasCancelled)
                        return false;
                }

                url = Browser.WebBrowser.Address;

                if (ClickAfterLoaded)
                {
                    Status = DownloadStatus.WaitingForLoadEnd;
                    for (var seconds = 0; seconds < Settings.LoadPageEndTimeout; seconds++)
                    {
                        if (_hasCancelled)
                            return false;
                        if (await Browser.WaitForLoadEnd(TimeSpan.FromSeconds(1)))
                            goto LoadPageEnd;
                    }

                    return Failed("Failed to wait for page load end.");
                    LoadPageEnd: ;
                }
                else
                {
                    Status = DownloadStatus.WaitingForLoadStart;
                    for (var seconds = 0; seconds < Settings.LoadPageStartTimeout; seconds++)
                    {
                        if (_hasCancelled)
                            return false;
                        if (await Browser.WaitForLoadStart(TimeSpan.FromSeconds(1)))
                            goto LoadStart;
                    }

                    return Failed("Failed to wait for page load start.");
                    LoadStart: ;
                }

                Browser.PrepareLoadEvents();

                var xpathOrScript = xPathOrScripts[i];
                var frameName = i < frameNames.Count ? frameNames[i] : string.Empty;

                if (xpathOrScript.StartsWith('/'))
                {
                    Status = DownloadStatus.Clicking;
                    if (!await Browser.TryClick(xpathOrScript, frameName,
                            Settings.TryClickCount, Settings.TryClickInterval * 1000))
                        return Failed($"Failed to click {xpathOrScript}");
                }
                else
                {
                    Status = DownloadStatus.ExecutingScript;
                    if (!await Browser.TryEvaluateJavascript(xpathOrScript, frameName))
                        return Failed($"Failed to execute {xpathOrScript}");
                }
            }

            return true;
        }

        // Called when download starts, decide whether to download.
        void OnBeginDownloadHandler(object? o, DownloadItem item)
        {
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

            targetFilePath = Path.Combine(DownloadDirectory, downloadFileName);

            // Compare file size to determine download or not.
            // Epic Launcher download page may change its file name for each download.
            // Find the old file and check the size.
            var oldFile = targetFilePath;
            if (!File.Exists(oldFile) && !string.IsNullOrWhiteSpace(FilePatternToDelete))
                oldFile = Directory.GetFiles(DownloadDirectory, FilePatternToDelete).FirstOrDefault();
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

        void DeleteOtherFilesInSameDirectory(string targetFilePath)
        {
            if (testOnly || string.IsNullOrWhiteSpace(FilePatternToDelete))
                return;

            var dir = Path.GetDirectoryName(targetFilePath)!;

            try
            {
                Directory.GetFiles(dir, FilePatternToDelete)
                    .Where(file => string.Compare(file, targetFilePath, StringComparison.OrdinalIgnoreCase) != 0)
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

    private static readonly string sevenZipPath = Path.Combine(AppDomain.CurrentDomain.SetupInformation.ApplicationBase!, "7z.exe");

    private async Task ExtractArchiveFile(string archiveFile)
    {
        if (!ExtractAfterDownload)
            return;

        if (!ArchiveFileTypes.Contains(Path.GetExtension(archiveFile).ToLower()))
            return;

        var archiveDir = Path.GetDirectoryName(archiveFile)!;
        if (FilePatternToDeleteBeforeExtraction != "")
            Directory.GetFiles(archiveDir, FilePatternToDeleteBeforeExtraction)
                .ToList().ForEach(File.Delete);

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = sevenZipPath,
            Arguments = $@"x -y -o""{archiveDir}"" ""{archiveFile}""",
            UseShellExecute = true,
        });

        if (process == null)
            return;

        await process.WaitForExitAsync();
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
    WaitingForLoadStart,
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
