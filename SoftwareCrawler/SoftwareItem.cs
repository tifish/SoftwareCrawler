using System.ComponentModel;
using System.Diagnostics;
using System.IO.Enumeration;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using JeekTools;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace SoftwareCrawler;

public sealed class SoftwareItem : INotifyPropertyChanged
{
    private static readonly ILogger Log = LogManager.CreateLogger(nameof(SoftwareItem));

    private class NonSerializedAttribute : Attribute { }

    private DownloadingStatus _status;

    [NonSerialized]
    public DownloadingStatus Status
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
                    _uiSynchronizationContext?.Post(
                        _ =>
                        {
                            OnPropertyChanged();
                        },
                        null
                    );
            }
        }
    }

    private string _progress = string.Empty;

    [NonSerialized]
    public string Progress
    {
        get => _progress;
        internal set
        {
            if (_progress != value)
            {
                _progress = value;

                if (SynchronizationContext.Current == _uiSynchronizationContext)
                    OnPropertyChanged();
                else
                    _uiSynchronizationContext?.Post(
                        _ =>
                        {
                            OnPropertyChanged();
                        },
                        null
                    );
            }
        }
    }

    public bool Enabled { get; set; } = true;
    public string Name { get; set; } = string.Empty;
    public string WebPage { get; set; } = string.Empty;

    /// <summary>
    /// A row is one line of tab-separated columns, so a value carrying either
    /// character would break the file: a newline splits the row in two, a tab
    /// shifts every column after it. Both travel as backtick escapes, which is
    /// also what the grid shows and what the file has always held.
    /// </summary>
    internal static string EncodeField(string value) =>
        value
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Replace("\t", "`t")
            .Replace("\n", "`n");

    /// <inheritdoc cref="EncodeField"/>
    internal static string DecodeField(string value) =>
        value.Replace("`n", "\n").Replace("`t", "\t");

    [NonSerialized]
    public List<string> GetXPathOrScripts()
    {
        // XPathOrScript1/2/3/4/5 -> XPathOrScripts, as editable text
        var xpathOrScripts = new List<string>();
        foreach (var property in XPathOrScriptProperties)
        {
            var value = (string)property.GetValue(this)!;
            if (!string.IsNullOrEmpty(value))
            {
                xpathOrScripts.Add(DecodeField(value));
            }
        }

        return xpathOrScripts;
    }

    public void SetXPathOrScripts(List<string> xpathOrScripts)
    {
        // XPathOrScripts -> XPathOrScript1/2/3/4/5, back to the escaped form.
        // An external editor indenting with tabs is what makes this necessary.
        for (var i = 0; i < XPathOrScriptProperties.Count; i++)
        {
            XPathOrScriptProperties[i]
                .SetValue(this, i < xpathOrScripts.Count ? EncodeField(xpathOrScripts[i]) : "");
        }
    }

    public string XPathOrScript1 { get; set; } = string.Empty;
    public string XPathOrScript2 { get; set; } = string.Empty;
    public string XPathOrScript3 { get; set; } = string.Empty;
    public string XPathOrScript4 { get; set; } = string.Empty;
    public string XPathOrScript5 { get; set; } = string.Empty;
    public string Frames { get; set; } = string.Empty;
    public int WaitSecondsBeforeClick { get; set; }
    public int StartDownloadTimeout { get; set; }
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

    /// <summary>
    /// Extract archive entries directly into the download directory instead of
    /// preserving the directory structure stored in the archive.
    /// </summary>
    public bool ExtractToRoot { get; set; }

    public string FilePatternToDeleteBeforeExtractionAndExtractOnly { get; set; } = string.Empty;

    /// <summary>
    /// Download WebPage directly over HTTP instead of navigating the embedded browser.
    /// Some sites (e.g. SourceForge) serve Cloudflare challenges to automated browsers
    /// but allow plain HTTP clients.
    /// </summary>
    public bool DirectDownload { get; set; }

    private string _errorMessage = string.Empty;

    [NonSerialized]
    public string ErrorMessage
    {
        get => _errorMessage;
        internal set
        {
            if (_errorMessage != value)
            {
                _errorMessage = value;

                if (SynchronizationContext.Current == _uiSynchronizationContext)
                    OnPropertyChanged();
                else
                    _uiSynchronizationContext?.Post(
                        _ =>
                        {
                            OnPropertyChanged();
                        },
                        null
                    );
            }
        }
    }

    public SoftwareItem() { }

    private readonly List<PropertyInfo> XPathOrScriptProperties =
    [
        typeof(SoftwareItem).GetProperty(nameof(XPathOrScript1))!,
        typeof(SoftwareItem).GetProperty(nameof(XPathOrScript2))!,
        typeof(SoftwareItem).GetProperty(nameof(XPathOrScript3))!,
        typeof(SoftwareItem).GetProperty(nameof(XPathOrScript4))!,
        typeof(SoftwareItem).GetProperty(nameof(XPathOrScript5))!,
    ];

    public SoftwareItem(string dataLine, string extraLine)
    {
        FromDataLine(dataLine, DataProperties);
        FromDataLine(extraLine, ExtraProperties);
    }

    /// <summary>
    /// The crawl recipe: what every machine shares, and the only thing that
    /// belongs in version control. Whether this machine wants the item is
    /// <see cref="Enabled"/>, which lives with the download directories instead.
    /// </summary>
    public static readonly List<PropertyInfo> DataProperties =
    [
        .. new[]
        {
            nameof(Name),
            nameof(WebPage),
            nameof(XPathOrScript1),
            nameof(XPathOrScript2),
            nameof(XPathOrScript3),
            nameof(XPathOrScript4),
            nameof(XPathOrScript5),
            nameof(Frames),
            nameof(WaitSecondsBeforeClick),
            nameof(StartDownloadTimeout),
            nameof(FilePatternToDeleteBeforeDownload),
            nameof(ExtractAfterDownload),
            nameof(ExtractToRoot),
            nameof(FilePatternToDeleteBeforeExtractionAndExtractOnly),
            nameof(DirectDownload),
        }
            .Select(name => typeof(SoftwareItem).GetProperty(name)!)
            .ToList(),
    ];

    /// <summary>Everything that is this machine's business alone.</summary>
    public static readonly List<PropertyInfo> ExtraProperties =
    [
        .. new[] { nameof(Enabled), nameof(DownloadDirectory), nameof(DownloadDirectory2) }
            .Select(name => typeof(SoftwareItem).GetProperty(name)!)
            .ToList(),
    ];

    /// <summary>
    /// The shared-column order while ExtractToRoot was still the last field.
    /// Files written then (and files written before that column existed) are
    /// read with this list; the next save rewrites them in the current order.
    /// </summary>
    public static readonly List<PropertyInfo> ExtractToRootLastDataProperties =
    [
        .. new[]
        {
            nameof(Name),
            nameof(WebPage),
            nameof(XPathOrScript1),
            nameof(XPathOrScript2),
            nameof(XPathOrScript3),
            nameof(XPathOrScript4),
            nameof(XPathOrScript5),
            nameof(Frames),
            nameof(WaitSecondsBeforeClick),
            nameof(StartDownloadTimeout),
            nameof(FilePatternToDeleteBeforeDownload),
            nameof(ExtractAfterDownload),
            nameof(FilePatternToDeleteBeforeExtractionAndExtractOnly),
            nameof(DirectDownload),
            nameof(ExtractToRoot),
        }
            .Select(name => typeof(SoftwareItem).GetProperty(name)!)
            .ToList(),
    ];

    /// <summary>
    /// The layout of both files before Enabled moved out of the shared list.
    /// Kept so existing files still read correctly; the next save rewrites them
    /// in the current layout, after which these are only of historical interest.
    /// </summary>
    public static readonly List<PropertyInfo> LegacyDataProperties =
    [
        typeof(SoftwareItem).GetProperty(nameof(Enabled))!,
        .. ExtractToRootLastDataProperties,
    ];

    /// <inheritdoc cref="LegacyDataProperties"/>
    public static readonly List<PropertyInfo> LegacyExtraProperties =
    [
        .. new[] { nameof(DownloadDirectory), nameof(DownloadDirectory2) }
            .Select(name => typeof(SoftwareItem).GetProperty(name)!)
            .ToList(),
    ];

    public static string GetDataHeaderLine(List<PropertyInfo> properties)
    {
        return string.Join('\t', properties.Select(prop => prop.Name));
    }

    /// <summary>
    /// Picks the shared-column layout that matches how this file was written.
    /// The next save always rewrites the current <see cref="DataProperties"/> order.
    /// </summary>
    public static List<PropertyInfo> ResolveDataProperties(string header)
    {
        if (string.IsNullOrEmpty(header))
            return DataProperties;

        var columns = header.TrimStart('\uFEFF').Split('\t');
        if (columns[0] == nameof(Enabled))
            return LegacyDataProperties;

        var extractAfter = Array.IndexOf(columns, nameof(ExtractAfterDownload));
        var extractToRoot = Array.IndexOf(columns, nameof(ExtractToRoot));
        if (extractAfter >= 0 && extractToRoot == extractAfter + 1)
            return DataProperties;

        return ExtractToRootLastDataProperties;
    }

    public void FromDataLine(string line, List<PropertyInfo> properties)
    {
        var items = line.Split('\t');
        if (items.Length > properties.Count)
            throw new Exception("items.Length > properties.Count");

        // Fewer columns than properties is allowed: files saved by older versions
        // lack newly added trailing columns, which then keep their default values.

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
                prop.SetValue(
                    this,
                    item.ToLower() switch
                    {
                        "true" or "1" => true,
                        _ => false,
                    }
                );
            }
        }
    }

    public string ToDataLine(List<PropertyInfo> properties)
    {
        var items = properties.Select(prop =>
        {
            var value = prop.GetValue(this);

            // Every column goes through the escape, not just the script ones: a
            // path or a name pasted into the grid can carry a tab just as easily.
            if (prop.PropertyType == typeof(string))
                return EncodeField((string)(value ?? string.Empty));

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

    /// <summary>
    /// Creates a copy of the current SoftwareItem with all serializable properties copied.
    /// Non-serializable properties (Status, Progress, ErrorMessage) are reset to their default values.
    /// </summary>
    public SoftwareItem Clone()
    {
        var cloned = new SoftwareItem();

        // Copy DataProperties
        foreach (var property in DataProperties)
        {
            var value = property.GetValue(this);
            property.SetValue(cloned, value);
        }

        // Copy ExtraProperties
        foreach (var property in ExtraProperties)
        {
            var value = property.GetValue(this);
            property.SetValue(cloned, value);
        }

        return cloned;
    }

    private SynchronizationContext? _uiSynchronizationContext;

    /// <summary>
    /// The context the status properties push their change notifications to. The
    /// pipeline sets it for the duration of an attempt, since it is the one that
    /// knows which thread started it.
    /// </summary>
    internal SynchronizationContext? UiSynchronizationContext
    {
        get => _uiSynchronizationContext;
        set => _uiSynchronizationContext = value;
    }

    /// <summary>True once the user asked this item to stop.</summary>
    internal bool HasCancelled => _hasCancelled;

    public static readonly string SystemDownloadFolder = KnownFolders.GetPath(
        KnownFolder.Downloads
    );

    private bool _hasCancelled;


    /// <summary>
    /// There is one browser and one set of download callbacks, so two downloads
    /// running at once would answer each other's events and file each other's
    /// results. The menu drives items one at a time; this is what keeps a
    /// download started from anywhere else - the debug tools, a second menu
    /// action - from overlapping with it.
    /// </summary>
    private static readonly SemaphoreSlim DownloadGate = new(1, 1);

    /// <summary>True while an item holds the download gate. Diagnostics only.</summary>
    internal static bool IsDownloading => DownloadGate.CurrentCount == 0;

    public async Task<bool> Download(bool testOnly = false, int retryCount = 0)
    {
        if (!Enabled)
            return true;

        Progress = "";

        _hasCancelled = false;

        await DownloadGate.WaitAsync().ConfigureAwait(true);
        try
        {
            for (var i = 0; i < retryCount + 1; i++)
            {
                if (_hasCancelled)
                    return false;

                var downloadResult = await new DownloadPipeline(this, testOnly).RunAsync();
                switch (downloadResult)
                {
                    case DownloadPipeline.DownloadOnceResult.Succeeded:
                        Log.ZLogInformation(
                            $"Download {Name} successfully, retryCount={i}"
                        );
                        return true;

                    case DownloadPipeline.DownloadOnceResult.FailedAndRetry:
                        // Retry
                        await Task.Delay(Settings.DownloadRetryInterval * 1000);
                        break;

                    case DownloadPipeline.DownloadOnceResult.FailedAndNoRetry:
                        // No retry
                        return false;
                }
            }
        }
        finally
        {
            DownloadGate.Release();
        }

        Log.ZLogWarning(
            $"Download {Name} failed, retryCount={retryCount}, error={ErrorMessage}"
        );
        return false;
    }













    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public void ResetStatus()
    {
        Status = DownloadingStatus.Idle;
        Progress = string.Empty;
        ErrorMessage = string.Empty;
    }

    public void CancelDownload()
    {
        _hasCancelled = true;
        Browser.Cancel();

        Status = DownloadingStatus.Cancelled;
    }
}

public enum DownloadingStatus
{
    Idle,
    CheckingDownloadDirectory,
    WaitingForLoadEnd,
    Clicking,
    ExecutingScript,
    WaitingForDownload,
    Downloading,
    SameFileAlreadyDownloaded,
    Downloaded,
    HasUpdate,
    CopyingFile,
    Extracting,
    RunningEventScript,
    Failed,
    Cancelled,
}
