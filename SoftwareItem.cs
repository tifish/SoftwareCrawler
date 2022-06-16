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

                _uiSynchronizationContext?.Post(_ => { OnPropertyChanged(nameof(Progress)); }, null);
            }
        }
    }

    public bool Enabled { get; set; } = true;
    public string Name { get; set; } = string.Empty;
    public string WebPage { get; set; } = string.Empty;
    public string XPathOrScripts { get; set; } = string.Empty;
    public string Frames { get; set; } = string.Empty;
    public bool ClickAfterLoaded { get; set; } = false;
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
                _uiSynchronizationContext?.Post(_ => { OnPropertyChanged(nameof(ErrorMessage)); }, null);
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
                return (string) (value ?? string.Empty);

            if (prop.PropertyType == typeof(int))
            {
                var intValue = (int) value!;
                return intValue switch
                {
                    0 => string.Empty,
                    _ => intValue.ToString(),
                };
            }

            if (prop.PropertyType == typeof(bool))
                return (bool) value! switch
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

    private static readonly List<string> _executableFileTypes = new() {".exe", ".msi"};
    private static readonly List<string> _archiveFileTypes = new() {".zip", ".rar", ".7z"};

    public async Task<bool> Download(bool testOnly = false, int retryCount = 0)
    {
        if (!Enabled)
            return true;

        _hasCancelled = false;

        for (var i = 0; i < retryCount + 1; i++)
        {
            if (_hasCancelled)
                return false;

            var success = await DownloadOnce(testOnly);
            if (success)
            {
                Logger.Information($"Download {Name} successfully, retryCount={i}");
                return true;
            }

            await Task.Delay(3000);
        }

        Logger.Warning($"Download {Name} failed, retryCount={retryCount}, error={ErrorMessage}");
        return false;
    }

    private async Task<bool> DownloadOnce(bool testOnly = false)
    {
        // Initialize
        _uiSynchronizationContext = SynchronizationContext.Current;

        Status = DownloadStatus.Downloading;
        ErrorMessage = string.Empty;

        if (DownloadDirectory == "")
            return Failed("Download directory is empty.");
        if (!Directory.Exists(DownloadDirectory))
            return Failed("Download directory does not exist.");
        if (DownloadDirectory2 != "" && !Directory.Exists(DownloadDirectory2))
            return Failed("Download directory 2 does not exist.");

        var fileName = string.Empty;
        var fileSize = 0L;
        DateTime? fileTime = null;
        var targetFile = string.Empty;
        var downloadFile = string.Empty;
        var beginDownloadResult = BeginDownloadResult.NoDownload;

        Browser.BeginDownloadHandler += OnBeginDownloadHandler;
        Browser.DownloadProgressHandler += OnDownloadProgressHandler;

        // Download
        try
        {
            // Access download page.
            Browser.Load(WebPage);

            // Click links, last link is the download link.
            if (!await ClickAndTriggerDownload())
                return false;

            // Wait for download to start.
            Status = DownloadStatus.WaitingForDownload;
            var waitCounter = 20;
            while (beginDownloadResult == BeginDownloadResult.NoDownload)
            {
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
            if (!await Browser.WaitForDownloaded(TimeSpan.FromHours(2)))
                return Failed("Failed to download file.");

            return await Downloaded(DownloadStatus.Downloaded);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Download {Name} failed.", Name);
            return Failed(ex.Message);
        }
        finally
        {
            if (!testOnly && !string.IsNullOrEmpty(downloadFile))
                while (true)
                {
                    try
                    {
                        if (File.Exists(downloadFile))
                            File.Delete(downloadFile);
                    }
                    catch
                    {
                        await Task.Delay(1000);
                        continue;
                    }

                    break;
                }

            _uiSynchronizationContext = null;
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

            for (var i = 0; i < xPathOrScripts.Count; i++)
            {
                if (ClickAfterLoaded)
                {
                    Status = DownloadStatus.WaitingForLoadEnd;
                    if (!await Browser.WaitForLoadEnd(TimeSpan.FromMinutes(1)))
                        return Failed("Failed to wait for page load end.");
                }
                else
                {
                    Status = DownloadStatus.WaitingForLoadStart;
                    if (!await Browser.WaitForLoadStart(TimeSpan.FromMinutes(1)))
                        return Failed("Failed to wait for page load start.");
                }

                Browser.PrepareLoadEvents();

                var xpathOrScript = xPathOrScripts[i];
                var frameName = i < frameNames.Count ? frameNames[i] : string.Empty;

                if (xpathOrScript.StartsWith('/'))
                {
                    Status = DownloadStatus.Clicking;
                    if (!await Browser.TryClick(xpathOrScript, frameName))
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
            Browser.BeginDownloadHandler -= OnBeginDownloadHandler;

            fileName = item.SuggestedFileName;
            fileSize = item.TotalBytes;
            fileTime = item.EndTime;

            var ext = Path.GetExtension(fileName).ToLower();
            if (!_executableFileTypes.Contains(ext) && !_archiveFileTypes.Contains(ext))
            {
                Failed($"Unexpected file name: {fileName}");
                beginDownloadResult = BeginDownloadResult.Failed;
                item.IsCancelled = true;
                return;
            }

            targetFile = Path.Combine(DownloadDirectory, fileName);

            // Compare file size to determine download or not.
            // Epic Launcher download page may change its file name for each download.
            // Find the old file and check the size.
            var oldFile = targetFile;
            if (!File.Exists(oldFile) && !string.IsNullOrWhiteSpace(FilePatternToDelete))
                oldFile = Directory.GetFiles(DownloadDirectory, FilePatternToDelete).FirstOrDefault();
            if (File.Exists(oldFile))
            {
                var fileInfo = new FileInfo(oldFile);
                if (fileInfo.Length == fileSize)
                {
                    beginDownloadResult = BeginDownloadResult.Downloaded;
                    targetFile = oldFile;
                    fileName = Path.GetFileName(targetFile);
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
            item.FullPath = downloadFile = Path.Combine(SystemDownloadFolder, item.SuggestedFileName);
        }

        // Called when download progress changes.
        void OnDownloadProgressHandler(object? o, DownloadItem item)
        {
            // Download file name may change if same file exists.
            downloadFile = item.FullPath;

            Progress = $"{item.SuggestedFileName} - {item.PercentComplete:00}% - {item.ReceivedBytes:#,###} / {item.TotalBytes:#,###} Bytes - {item.CurrentSpeed / 1024:#,###} KB/s";
        }

        // When download is complete, move file to target directory.
        async Task<bool> Downloaded(DownloadStatus finalStatus)
        {
            Status = finalStatus;

            Progress = $"{fileName} - {(double) fileSize:#,###} Bytes";

            if (testOnly)
                return true;

            if (File.Exists(downloadFile))
            {
                DeleteOldFile(targetFile);

                if (fileTime.HasValue)
                    File.SetLastWriteTime(downloadFile, fileTime.Value);

                await CopyFileIfChanged(downloadFile, targetFile, true);
                await ExtractArchiveFile(targetFile);
            }
            else if (File.Exists(targetFile))
            {
                if (fileTime.HasValue)
                    File.SetLastWriteTime(targetFile, fileTime.Value);
            }

            if (string.IsNullOrEmpty(DownloadDirectory2))
                return true;

            var targetFile2 = Path.Combine(DownloadDirectory2, fileName);
            DeleteOldFile(targetFile2);
            await CopyFileIfChanged(targetFile, targetFile2);
            await ExtractArchiveFile(targetFile2);

            return true;
        }

        void DeleteOldFile(string targetFilePath)
        {
            if (testOnly || string.IsNullOrWhiteSpace(FilePatternToDelete))
                return;

            try
            {
                Directory.GetFiles(Path.GetDirectoryName(targetFilePath)!, FilePatternToDelete)
                    .Where(file => string.Compare(file, targetFilePath, StringComparison.OrdinalIgnoreCase) != 0)
                    .ToList().ForEach(File.Delete);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to delete old file.");
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

        if (!_archiveFileTypes.Contains(Path.GetExtension(archiveFile).ToLower()))
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
}
