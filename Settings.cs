global using static SoftwareCrawler.SettingsSingletonContainer;
using System.Windows.Forms;
using Newtonsoft.Json;

namespace SoftwareCrawler;

public class SettingsObject
{
    public string Proxy { get; set; } = "";
    public int DownloadRetryCount { get; set; } = 5;
    public int DownloadRetryInterval { get; set; } = 3;
    public int LoadPageEndTimeout { get; set; } = 60;
    public int TryClickCount { get; set; } = 10;
    public int TryClickInterval { get; set; } = 1;
    public int StartDownloadTimeout { get; set; } = 60;
    public int DownloadTimeout { get; set; } = 7200;
    public string ExternalJavascriptEditor { get; set; } = "";
    public string DefaultDownloadDirectory { get; set; } = "";
    public SystemColorMode ColorMode { get; set; } = SystemColorMode.System;

    private static readonly string AppPath = AppDomain.CurrentDomain.SetupInformation.ApplicationBase ?? string.Empty;
    private static readonly string SettingsFile = Path.Combine(AppPath, "Settings.json");

    public void Load()
    {
        if (File.Exists(SettingsFile))
        {
            try
            {
                var json = File.ReadAllText(SettingsFile);
                Settings = JsonConvert.DeserializeObject<SettingsObject>(json) ?? new SettingsObject();
                return;
            }
            catch (JsonException ex)
            {
                Logger.Error(ex, "Failed to parse settings from {SettingsFile}", SettingsFile);
            }
            catch (IOException ex)
            {
                Logger.Error(ex, "Failed to read settings from {SettingsFile}", SettingsFile);
            }
        }

        Settings = new SettingsObject();
    }

    public async Task Save()
    {
        await Task.Run(() =>
        {
            File.WriteAllText(SettingsFile,
                JsonConvert.SerializeObject(Settings));
        });
    }
}

public static class SettingsSingletonContainer
{
    public static SettingsObject Settings { get; set; } = new();
}
