using System.Text;

namespace SoftwareCrawler;

public static class SoftwareManager
{
    private static readonly string ConfigPath = Path.Join(
        AppDomain.CurrentDomain.SetupInformation.ApplicationBase ?? string.Empty, "Software.tab");

    private static readonly string DownloadDirectoryConfigPath = Path.Join(
        AppDomain.CurrentDomain.SetupInformation.ApplicationBase ?? string.Empty, "DownloadDirectory.tab");

    public static List<SoftwareItem> Items { get; private set; } = [];

    public static async Task Load()
    {
        if (!File.Exists(ConfigPath))
            return;

        var dataLines = (await File.ReadAllLinesAsync(ConfigPath)).ToList().Skip(1).ToList();
        var extraLines = (await File.ReadAllLinesAsync(DownloadDirectoryConfigPath)).ToList().Skip(1).ToList();

        Items.Clear();
        for (var i = 0; i < dataLines.Count; i++)
        {
            var dataLine = dataLines[i];
            var extraLine = extraLines.Count > i ? extraLines[i] : string.Empty;

            Items.Add(new SoftwareItem(dataLine, extraLine));
        }
    }

    public static async Task Save()
    {
        var dataItems = new List<string>(Items.Count + 1)
        {
            SoftwareItem.GetDataHeaderLine(SoftwareItem.DataProperties),
        };
        dataItems.AddRange(Items.Select(item => item.ToDataLine(SoftwareItem.DataProperties)));
        await File.WriteAllLinesAsync(ConfigPath, dataItems, new UTF8Encoding(true));

        var extraItems = new List<string>(Items.Count + 1)
        {
            SoftwareItem.GetDataHeaderLine(SoftwareItem.ExtraProperties),
        };
        extraItems.AddRange(Items.Select(item => item.ToDataLine(SoftwareItem.ExtraProperties)));
        await File.WriteAllLinesAsync(DownloadDirectoryConfigPath, extraItems, new UTF8Encoding(true));
    }
}
