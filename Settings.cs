global using static SoftwareCrawler.SettingsSingletonContainer;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using Newtonsoft.Json;

namespace SoftwareCrawler;

public class SettingsObject
{
    public BrowserType BrowserType { get; set; }
    public string Proxy { get; set; } = "";
    public int DownloadRetryCount { get; set; } = 5;
    public int StartDownloadTimeout { get; set; } = 60;
    public int DownloadTimeout { get; set; } = 7200;

    private static readonly string AppPath = AppDomain.CurrentDomain.SetupInformation.ApplicationBase ?? string.Empty;
    private static readonly string SettingsFile = Path.Combine(AppPath, "Settings.json");

    public async Task Load()
    {
        if (!File.Exists(SettingsFile))
            return;

        SettingsObject? settings = null;
        await Task.Run(() =>
        {
            settings = JsonConvert.DeserializeObject<SettingsObject>(
                File.ReadAllText(SettingsFile)
            );
        });

        if (settings == null)
        {
            Settings = new SettingsObject();
            return;
        }

        Settings = settings;
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
    public static SettingsObject Settings = new();
}
