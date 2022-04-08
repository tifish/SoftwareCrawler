global using static SoftwareCrawler.SettingsSingletonContainer;
using Newtonsoft.Json;

namespace SoftwareCrawler;

public class SettingsObject
{
    public BrowserType BrowserType { get; set; }

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
