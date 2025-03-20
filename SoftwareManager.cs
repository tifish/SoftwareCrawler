using System.Text;

namespace SoftwareCrawler;

public static class SoftwareManager
{
    private static readonly string ConfigPath = Path.Combine(
        AppDomain.CurrentDomain.SetupInformation.ApplicationBase ?? string.Empty, "Software.tab");

    public static List<SoftwareItem> Items { get; private set; } = new();

    public static async Task Load()
    {
        if (!File.Exists(ConfigPath))
            return;

        Items = (await File.ReadAllLinesAsync(ConfigPath))
            .Skip(1)
            .Select(line => new SoftwareItem(line))
            .ToList();
    }

    public static async Task Save()
    {
        var items = new List<string>(Items.Count + 1)
        {
            SoftwareItem.GetDataHeaderLine(),
        };
        items.AddRange(Items.Select(item => item.ToDataLine()));
        await File.WriteAllLinesAsync(ConfigPath, items, new UTF8Encoding(true));
    }
}
