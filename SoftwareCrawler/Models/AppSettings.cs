using System.Text.Json.Serialization;
using JeekTools;

namespace SoftwareCrawler.Models;

/// <summary>How often the app checks GitHub for a newer release.</summary>
public enum UpdateCheckFrequency
{
    Never,
    EverySixHours,
    Daily,
    Weekly,
}

/// <summary>
/// Settings bound to this machine: local paths and the local proxy endpoint.
/// Always stored in %LOCALAPPDATA%\SoftwareCrawler\Config, never roamed.
/// </summary>
public class MachineAppSettings
{
    public StorageLocation StorageLocation { get; set; } = StorageLocation.UserDirectory;
    public string? CustomStoragePath { get; set; }

    public string Proxy { get; set; } = "";
    public string ExternalJavascriptEditor { get; set; } = "";
    public string DefaultDownloadDirectory { get; set; } = "";

    /// <summary>
    /// Whether an HKCU Run entry launches the app into the tray at logon. Mirrors
    /// the registry rather than driving it — StartupService reads the actual entry,
    /// since Task Manager can turn it off behind our back.
    /// </summary>
    public bool RunAtStartup { get; set; }
}

/// <summary>
/// Machine-independent preferences. Stored in the Config folder of the active
/// storage location (AppData / portable / custom).
/// </summary>
public class RoamingAppSettings
{
    public int DownloadRetryCount { get; set; } = 5;
    public int DownloadRetryInterval { get; set; } = 3;
    public int LoadPageEndTimeout { get; set; } = 60;
    public int TryClickCount { get; set; } = 10;
    public int TryClickInterval { get; set; } = 1;
    public int StartDownloadTimeout { get; set; } = 60;
    public int DownloadTimeout { get; set; } = 7200;
    public SystemColorMode ColorMode { get; set; } = SystemColorMode.System;
    public bool CheckUpdateOnStartup { get; set; } = true;
    public UpdateCheckFrequency UpdateCheckFrequency { get; set; } = UpdateCheckFrequency.Daily;

    /// <summary>
    /// Times of day, "HH:mm", at which the resident scheduler downloads every
    /// enabled item. Empty means no scheduled full run. These replace the Windows
    /// scheduled task the app used to register: a resident instance holds the
    /// single-instance lock, so an external task would only ever be turned away.
    /// </summary>
    public List<string> ScheduledDownloadTimes { get; set; } =
        ["00:00", "08:00", "13:00", "18:30"];

    /// <summary>
    /// How often items marked <see cref="SoftwareItem.FrequentCheck"/> are checked,
    /// in minutes.
    /// </summary>
    public int FrequentCheckIntervalMinutes { get; set; } = 10;
}

/// <summary>
/// The two settings files as the single flat view the app reads. Each property
/// forwards to whichever file owns it rather than holding a copy, so there is
/// nothing to keep in step: adding a setting means adding it to one of the two
/// classes above and forwarding it here, and forgetting the second half is a
/// compile error at the call site rather than a value that silently vanishes.
/// </summary>
public class AppSettings
{
    public AppSettings()
        : this(new MachineAppSettings(), new RoamingAppSettings()) { }

    public AppSettings(MachineAppSettings machine, RoamingAppSettings roaming)
    {
        Machine = machine;
        Roaming = roaming;
    }

    [JsonIgnore]
    public MachineAppSettings Machine { get; }

    [JsonIgnore]
    public RoamingAppSettings Roaming { get; }

    // Machine-local
    public StorageLocation StorageLocation
    {
        get => Machine.StorageLocation;
        set => Machine.StorageLocation = value;
    }
    public string? CustomStoragePath
    {
        get => Machine.CustomStoragePath;
        set => Machine.CustomStoragePath = value;
    }
    public string Proxy
    {
        get => Machine.Proxy;
        set => Machine.Proxy = value;
    }
    public string ExternalJavascriptEditor
    {
        get => Machine.ExternalJavascriptEditor;
        set => Machine.ExternalJavascriptEditor = value;
    }
    public string DefaultDownloadDirectory
    {
        get => Machine.DefaultDownloadDirectory;
        set => Machine.DefaultDownloadDirectory = value;
    }
    public bool RunAtStartup
    {
        get => Machine.RunAtStartup;
        set => Machine.RunAtStartup = value;
    }

    // Roaming
    public int DownloadRetryCount
    {
        get => Roaming.DownloadRetryCount;
        set => Roaming.DownloadRetryCount = value;
    }
    public int DownloadRetryInterval
    {
        get => Roaming.DownloadRetryInterval;
        set => Roaming.DownloadRetryInterval = value;
    }
    public int LoadPageEndTimeout
    {
        get => Roaming.LoadPageEndTimeout;
        set => Roaming.LoadPageEndTimeout = value;
    }
    public int TryClickCount
    {
        get => Roaming.TryClickCount;
        set => Roaming.TryClickCount = value;
    }
    public int TryClickInterval
    {
        get => Roaming.TryClickInterval;
        set => Roaming.TryClickInterval = value;
    }
    public int StartDownloadTimeout
    {
        get => Roaming.StartDownloadTimeout;
        set => Roaming.StartDownloadTimeout = value;
    }
    public int DownloadTimeout
    {
        get => Roaming.DownloadTimeout;
        set => Roaming.DownloadTimeout = value;
    }
    public SystemColorMode ColorMode
    {
        get => Roaming.ColorMode;
        set => Roaming.ColorMode = value;
    }
    public bool CheckUpdateOnStartup
    {
        get => Roaming.CheckUpdateOnStartup;
        set => Roaming.CheckUpdateOnStartup = value;
    }
    public UpdateCheckFrequency UpdateCheckFrequency
    {
        get => Roaming.UpdateCheckFrequency;
        set => Roaming.UpdateCheckFrequency = value;
    }
    public List<string> ScheduledDownloadTimes
    {
        get => Roaming.ScheduledDownloadTimes;
        set => Roaming.ScheduledDownloadTimes = value;
    }
    public int FrequentCheckIntervalMinutes
    {
        get => Roaming.FrequentCheckIntervalMinutes;
        set => Roaming.FrequentCheckIntervalMinutes = value;
    }
}
