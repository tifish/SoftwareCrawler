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
}

/// <summary>The two settings files merged into the single view the app reads.</summary>
public class AppSettings
{
    // Machine-local
    public StorageLocation StorageLocation { get; set; } = StorageLocation.UserDirectory;
    public string? CustomStoragePath { get; set; }
    public string Proxy { get; set; } = "";
    public string ExternalJavascriptEditor { get; set; } = "";
    public string DefaultDownloadDirectory { get; set; } = "";

    // Roaming
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
}
