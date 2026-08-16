global using static SoftwareCrawler.Services.SettingsSingletonContainer;
using JeekTools;
using SoftwareCrawler.Models;

namespace SoftwareCrawler.Services;

/// <summary>
/// Loads and saves app settings split by roaming behavior, on top of the
/// generic <see cref="SettingsStorage"/> path scheme and
/// <see cref="JsonSettingsFile"/> merge/write machinery from JeekTools.
///
/// Machine-local state always lives under %LOCALAPPDATA%\SoftwareCrawler\Config.
/// Roaming preferences live in the active storage Config folder, alongside the
/// software list.
/// </summary>
public class SettingsService
{
    public const string AppName = "SoftwareCrawler";

    private static readonly SettingsStorage Storage = new(AppName);

    /// <summary>The machine-local settings file.</summary>
    public static string DefaultMachineSettingsPath => Storage.MachineSettingsPath;

    /// <summary>True when startup will use the executable directory for roaming data.</summary>
    public static bool IsPortable => Storage.IsPortable;

    /// <summary>The Config folder holding machine-local data.</summary>
    public static string MachineConfigRoot => Storage.LocalConfigDir;

    /// <summary>
    /// The per-machine data folder, outside the program directory whatever the
    /// storage mode is. Things that must survive a wiped bin folder live here.
    /// </summary>
    public static string LocalDataRoot => Storage.LocalDir;

    /// <summary>The Config folder next to the executable; its existence forces portable mode.</summary>
    public static string ProgramConfigRoot => Storage.ProgramConfigDir;

    /// <summary>The folder the executable runs from, where shipped templates live.</summary>
    public static string ProgramRoot => Storage.ProgramDir;

    private string _lastSavedMachineJson;
    private string _lastSavedRoamingJson;
    private string _lastSavedRoamingPath;
    private MachineAppSettings _baseMachineSettings;
    private RoamingAppSettings _baseRoamingSettings;

    public SettingsService(string? machineSettingsPath = null)
    {
        MachineSettingsPath = machineSettingsPath ?? DefaultMachineSettingsPath;

        JsonSettingsFile.TryLoad(MachineSettingsPath, out MachineAppSettings machineSettings);
        NormalizeMachineSettings(machineSettings);

        RoamingSettingsPath = ResolveSettingsPath(
            Storage.ResolveEffectiveLocation(machineSettings.StorageLocation),
            machineSettings.CustomStoragePath
        );
        JsonSettingsFile.TryLoad(RoamingSettingsPath, out RoamingAppSettings roamingSettings);
        NormalizeRoamingSettings(roamingSettings);

        Settings = new AppSettings(machineSettings, roamingSettings);
        NormalizeSettings(Settings);

        // Baselines are cloned so they never share references with Settings:
        // changes must diff against them, and an aliased baseline would mutate
        // along and make every change look unchanged.
        _baseMachineSettings = JsonSettingsFile.Clone(Settings.Machine);
        _baseRoamingSettings = JsonSettingsFile.Clone(Settings.Roaming);
        _lastSavedMachineJson = JsonSettingsFile.Serialize(_baseMachineSettings);
        _lastSavedRoamingPath = CurrentRoamingSettingsPath();
        _lastSavedRoamingJson = JsonSettingsFile.Serialize(_baseRoamingSettings);
    }

    public string MachineSettingsPath { get; }

    public string RoamingSettingsPath { get; private set; }

    public AppSettings Settings { get; private set; }

    public StorageLocation CurrentStorageLocation =>
        Storage.ResolveEffectiveLocation(Settings.StorageLocation);

    /// <summary>
    /// Brings both halves within their allowed ranges. The flat view forwards to
    /// them, so there is nothing to copy back.
    /// </summary>
    private static void NormalizeSettings(AppSettings settings)
    {
        NormalizeMachineSettings(settings.Machine);
        NormalizeRoamingSettings(settings.Roaming);
    }

    private static void NormalizeMachineSettings(MachineAppSettings settings)
    {
        settings.StorageLocation = Storage.NormalizeLocation(settings.StorageLocation);
        if (string.IsNullOrWhiteSpace(settings.CustomStoragePath))
            settings.CustomStoragePath = null;
        if (
            settings.StorageLocation == StorageLocation.CustomDirectory
            && settings.CustomStoragePath is null
        )
            settings.StorageLocation = StorageLocation.UserDirectory;
        if (
            settings.StorageLocation == StorageLocation.ProgramDirectory
            && !Storage.ProgramConfigRootExists()
        )
            settings.StorageLocation = StorageLocation.UserDirectory;

        settings.Proxy = settings.Proxy.Trim();
        settings.ExternalJavascriptEditor = settings.ExternalJavascriptEditor.Trim();
        settings.DefaultDownloadDirectory = settings.DefaultDownloadDirectory.Trim();
    }

    private static void NormalizeRoamingSettings(RoamingAppSettings settings)
    {
        settings.DownloadRetryCount = Math.Clamp(settings.DownloadRetryCount, 0, 100);
        settings.DownloadRetryInterval = Math.Clamp(settings.DownloadRetryInterval, 0, 3600);
        settings.LoadPageEndTimeout = Math.Clamp(settings.LoadPageEndTimeout, 1, 3600);
        settings.TryClickCount = Math.Clamp(settings.TryClickCount, 1, 100);
        settings.TryClickInterval = Math.Clamp(settings.TryClickInterval, 0, 3600);
        settings.StartDownloadTimeout = Math.Clamp(settings.StartDownloadTimeout, 1, 3600);
        settings.DownloadTimeout = Math.Clamp(settings.DownloadTimeout, 1, 86400);
        if (!Enum.IsDefined(settings.ColorMode))
            settings.ColorMode = SystemColorMode.System;
        if (!Enum.IsDefined(settings.UpdateCheckFrequency))
            settings.UpdateCheckFrequency = UpdateCheckFrequency.Daily;
    }

    private string CurrentRoamingSettingsPath() =>
        ResolveSettingsPath(CurrentStorageLocation, Settings.CustomStoragePath);

    /// <summary>Reloads the roaming settings file from the active storage Config folder.</summary>
    public void ReloadRoamingSettings()
    {
        NormalizeSettings(Settings);
        var machineSettings = Settings.Machine;
        var path = CurrentRoamingSettingsPath();
        if (!JsonSettingsFile.TryLoad(path, out RoamingAppSettings roamingSettings))
            roamingSettings = Settings.Roaming;
        NormalizeRoamingSettings(roamingSettings);

        Settings = new AppSettings(machineSettings, roamingSettings);
        NormalizeSettings(Settings);
        RoamingSettingsPath = path;
        _lastSavedRoamingPath = path;
        _baseRoamingSettings = JsonSettingsFile.Clone(Settings.Roaming);
        _lastSavedRoamingJson = JsonSettingsFile.Serialize(_baseRoamingSettings);
    }

    /// <summary>Persists changed settings to their machine-local and roaming files.</summary>
    public bool SaveIfChanged()
    {
        NormalizeSettings(Settings);

        var localMachine = Settings.Machine;
        var localRoaming = Settings.Roaming;
        var machineJson = JsonSettingsFile.Serialize(localMachine);
        var roamingPath = CurrentRoamingSettingsPath();
        var roamingJson = JsonSettingsFile.Serialize(localRoaming);

        var saved = true;
        var mergedMachine = localMachine;
        var mergedRoaming = localRoaming;

        // Brackets both writes so the watcher can date them; TryMergeAndWrite already
        // merges with whatever is on disk, so an outside edit is absorbed, not lost.
        using var selfWrite = ConfigChangeMonitor.BeginSelfWrite(MachineSettingsPath, roamingPath);

        // settings.json is user data that nothing else can restore either.
        ConfigBackupService.BackupDaily(MachineSettingsPath, roamingPath);

        if (!string.Equals(machineJson, _lastSavedMachineJson, StringComparison.Ordinal))
        {
            var machineSaved = JsonSettingsFile.TryMergeAndWrite<MachineAppSettings>(
                MachineSettingsPath,
                _baseMachineSettings,
                localMachine,
                NormalizeMachineSettings,
                forceAllLocal: false,
                out mergedMachine
            );
            saved &= machineSaved;
            if (machineSaved)
            {
                _baseMachineSettings = JsonSettingsFile.Clone<MachineAppSettings>(mergedMachine);
                _lastSavedMachineJson = JsonSettingsFile.Serialize(mergedMachine);
            }
        }

        var roamingPathChanged = !string.Equals(
            roamingPath,
            _lastSavedRoamingPath,
            StringComparison.OrdinalIgnoreCase
        );
        if (
            roamingPathChanged
            || !string.Equals(roamingJson, _lastSavedRoamingJson, StringComparison.Ordinal)
        )
        {
            var roamingSaved = JsonSettingsFile.TryMergeAndWrite<RoamingAppSettings>(
                roamingPath,
                _baseRoamingSettings,
                localRoaming,
                NormalizeRoamingSettings,
                forceAllLocal: roamingPathChanged && !File.Exists(roamingPath),
                out mergedRoaming
            );
            saved &= roamingSaved;
            if (roamingSaved)
            {
                RoamingSettingsPath = roamingPath;
                _lastSavedRoamingPath = roamingPath;
                _baseRoamingSettings = JsonSettingsFile.Clone<RoamingAppSettings>(mergedRoaming);
                _lastSavedRoamingJson = JsonSettingsFile.Serialize(mergedRoaming);
            }
        }

        if (saved)
        {
            Settings = new AppSettings(mergedMachine, mergedRoaming);
            NormalizeSettings(Settings);
        }

        // Every save path goes through here, so this is the one place that has
        // to tell the watcher the new content came from us.
        ConfigChangeMonitor.MarkSelfWrite(MachineSettingsPath);
        ConfigChangeMonitor.MarkSelfWrite(RoamingSettingsPath);

        return saved;
    }

    /// <summary>Resolves the active Config folder for roaming settings and user data.</summary>
    public string ResolveConfigRoot() =>
        ResolveConfigRoot(CurrentStorageLocation, Settings.CustomStoragePath);

    /// <summary>Resolves the Config folder for a given storage location.</summary>
    public static string ResolveConfigRoot(StorageLocation location, string? customPath = null) =>
        Storage.ResolveConfigRoot(location, customPath);

    /// <summary>Resolves settings.json under the Config folder for a given storage location.</summary>
    public static string ResolveSettingsPath(StorageLocation location, string? customPath = null) =>
        Storage.ResolveSettingsPath(location, customPath);

    /// <summary>
    /// Moves the whole roaming Config folder to a new root. Existing destination
    /// files with the same relative paths are replaced by the current Config.
    /// </summary>
    public static void MoveConfigRoot(string sourceRoot, string destRoot) =>
        SettingsStorage.MoveConfigRoot(sourceRoot, destRoot);

    /// <summary>Deletes the executable-side Config folder after leaving portable mode.</summary>
    public static bool TryDeleteProgramConfig(out string? error) =>
        Storage.TryDeleteProgramConfig(out error);
}

/// <summary>Ambient access to the settings, imported through a global using.</summary>
public static class SettingsSingletonContainer
{
    // Not named SettingsService: that would collide with the type of the same
    // name at every call site the global using reaches.
    public static SettingsService SettingsStore { get; } = new();

    public static AppSettings Settings => SettingsStore.Settings;
}
