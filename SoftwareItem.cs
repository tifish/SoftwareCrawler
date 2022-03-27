using System.ComponentModel;
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

    public string Name { get; set; } = string.Empty;
    public string WebPage { get; set; } = string.Empty;
    public string XPathOrScripts { get; set; } = string.Empty;
    public string Frames { get; set; } = string.Empty;
    public bool ClickAfterLoaded { get; set; }
    public string DownloadDirectory { get; set; } = string.Empty;
    public string DownloadDirectory2 { get; set; } = string.Empty;

    public string FilePatternToDelete { get; set; } = string.Empty;

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
        Started,
    }

    public static readonly string SystemDownloadFolder = KnownFolders.GetPath(KnownFolder.Downloads);

    public async Task<bool> Download(bool testOnly = false, int retryCount = 0)
    {
        for (var i = 0; i < retryCount + 1; i++)
        {
            var success = await DownloadOnce(testOnly);
            if (success)
                return true;
        }

        return false;
    }

    private async Task<bool> DownloadOnce(bool testOnly = false)
    {
        // Initialize
        _uiSynchronizationContext = SynchronizationContext.Current;

        Status = DownloadStatus.Downloading;
        ErrorMessage = string.Empty;

        var fileName = string.Empty;
        var ext = string.Empty;
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
                    await Browser.WaitForLoadEnd();
                }
                else
                {
                    Status = DownloadStatus.WaitingForLoadStart;
                    await Browser.WaitForLoadStart();
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
                    return Downloaded(DownloadStatus.SameFileAlreadyDownloaded);
            }

            // Wait for download to complete.
            Status = DownloadStatus.Downloading;
            if (!await Browser.WaitForDownloaded())
                return Failed("Failed to download file");

            return Downloaded(testOnly ? DownloadStatus.Tested : DownloadStatus.Downloaded);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Download {Name} failed.", Name);
            return Failed(ex.Message);
        }
        finally
        {
            _uiSynchronizationContext = null;
            Browser.BeginDownloadHandler -= OnBeginDownloadHandler;
            Browser.DownloadProgressHandler -= OnDownloadProgressHandler;
        }

        // Called when download starts, decide whether to download.
        void OnBeginDownloadHandler(object? o, DownloadItem item)
        {
            Browser.BeginDownloadHandler -= OnBeginDownloadHandler;

            fileName = item.SuggestedFileName;
            fileSize = item.TotalBytes;
            fileTime = item.EndTime;

            ext = Path.GetExtension(fileName).ToLower();
            if (ext is not (".exe" or ".msi" or ".zip"))
            {
                Failed($"Unexpected file name: {fileName}");
                beginDownloadResult = BeginDownloadResult.Failed;
                item.IsCancelled = true;
                return;
            }

            targetFile = Path.Combine(DownloadDirectory, fileName);
            if (File.Exists(targetFile))
            {
                var fileInfo = new FileInfo(targetFile);
                if (fileInfo.Length == fileSize)
                {
                    beginDownloadResult = BeginDownloadResult.Downloaded;
                    item.IsCancelled = true;
                    return;
                }
            }

            if (testOnly)
            {
                beginDownloadResult = BeginDownloadResult.Downloaded;
                item.IsCancelled = true;
                return;
            }

            beginDownloadResult = BeginDownloadResult.Started;
            item.FullPath = downloadFile = Path.Combine(SystemDownloadFolder, item.SuggestedFileName);
        }

        // Called when download progress changes.
        void OnDownloadProgressHandler(object? o, DownloadItem item)
        {
            Progress = $"{item.SuggestedFileName} - {item.PercentComplete:00}% - {item.ReceivedBytes:#,###} / {item.TotalBytes:#,###} Bytes - {item.CurrentSpeed / 1024:#,###} KB/s";
            if (item.IsComplete)
                Browser.DownloadProgressHandler -= OnDownloadProgressHandler;
        }

        // When download is complete, move file to target directory.
        bool Downloaded(DownloadStatus finalStatus)
        {
            Status = finalStatus;

            try
            {
                Progress = $"{fileName} - {(double) fileSize:#,###} Bytes";


                if (!testOnly)
                {
                    DeleteOldFile(targetFile);
                    if (File.Exists(downloadFile))
                    {
                        if (fileTime.HasValue)
                            File.SetLastWriteTime(downloadFile, fileTime.Value);
                        CopyFileIfChanged(downloadFile, targetFile, true);
                    }
                    else if (File.Exists(targetFile))
                    {
                        if(fileTime.HasValue)
                            File.SetLastWriteTime(targetFile, fileTime.Value);
                    }
                }

                if (string.IsNullOrEmpty(DownloadDirectory2))
                    return true;

                if (!testOnly)
                {
                    var targetFile2 = Path.Combine(DownloadDirectory2, fileName);
                    DeleteOldFile(targetFile2);
                    CopyFileIfChanged(targetFile, targetFile2);
                }
            }
            finally
            {
                if (!testOnly && File.Exists(downloadFile))
                    File.Delete(downloadFile);
            }

            return true;
        }

        void DeleteOldFile(string targetFilePath)
        {
            if (testOnly)
                return;

            var pattern = string.IsNullOrEmpty(FilePatternToDelete) ? $"*{ext}" : FilePatternToDelete;
            Directory.GetFiles(Path.GetDirectoryName(targetFilePath)!, pattern)
                .Where(file => string.Compare(file, targetFilePath, StringComparison.OrdinalIgnoreCase) != 0)
                .ToList().ForEach(File.Delete);
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

    private static void CopyFileIfChanged(string sourceFile, string targetFile2, bool move = false)
    {
        var fileInfo = new FileInfo(sourceFile);
        var fileInfo2 = new FileInfo(targetFile2);

        if (fileInfo2.Exists)
            if (fileInfo.Length == fileInfo2.Length && fileInfo.LastWriteTime == fileInfo2.LastWriteTime)
                return;

        if (move)
            File.Move(sourceFile, targetFile2, true);
        else
            File.Copy(sourceFile, targetFile2, true);
        File.SetLastWriteTime(targetFile2, fileInfo.LastWriteTime);
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
    Tested,
    Failed,
}
