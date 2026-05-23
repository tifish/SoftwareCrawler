using System.Text;

namespace SoftwareCrawler;

public static class SoftwareManager
{
    private static readonly string ConfigPath = Path.Join(
        AppDomain.CurrentDomain.SetupInformation.ApplicationBase ?? string.Empty,
        "Software.tab"
    );

    private static readonly string DownloadDirectoryConfigPath = Path.Join(
        AppDomain.CurrentDomain.SetupInformation.ApplicationBase ?? string.Empty,
        "DownloadDirectory.tab"
    );

    public static List<SoftwareItem> Items { get; private set; } = [];

    public static async Task Load()
    {
        if (!File.Exists(ConfigPath))
            return;

        // Read both files in parallel to reduce startup latency.
        var dataTask = File.ReadAllLinesAsync(ConfigPath);
        var extraTask = File.ReadAllLinesAsync(DownloadDirectoryConfigPath);
        await Task.WhenAll(dataTask, extraTask);

        var dataLines = dataTask.Result.Skip(1).ToArray();
        var extraLines = extraTask.Result.Skip(1).ToArray();

        Items.Clear();
        for (var i = 0; i < dataLines.Length; i++)
        {
            var dataLine = dataLines[i];
            var extraLine = extraLines.Length > i ? extraLines[i] : string.Empty;

            Items.Add(new SoftwareItem(dataLine, extraLine));
        }
    }

    private static async Task SaveCore()
    {
        var dataItems = new List<string>(Items.Count + 1)
        {
            SoftwareItem.GetDataHeaderLine(SoftwareItem.DataProperties),
        };
        dataItems.AddRange(Items.Select(item => item.ToDataLine(SoftwareItem.DataProperties)));

        var extraItems = new List<string>(Items.Count + 1)
        {
            SoftwareItem.GetDataHeaderLine(SoftwareItem.ExtraProperties),
        };
        extraItems.AddRange(Items.Select(item => item.ToDataLine(SoftwareItem.ExtraProperties)));

        // Write both files in parallel.
        var encoding = new UTF8Encoding(true);
        await Task.WhenAll(
            File.WriteAllLinesAsync(ConfigPath, dataItems, encoding),
            File.WriteAllLinesAsync(DownloadDirectoryConfigPath, extraItems, encoding)
        );
    }

    // Debounced save: coalesces bursts of edits (e.g. typing in a cell, multiple row
    // operations) into a single write after a short quiet period.
    private const int SaveDebounceMs = 500;
    private static CancellationTokenSource? _debounceCts;
    private static readonly SemaphoreSlim _saveGate = new(1, 1);

    public static async Task Save()
    {
        _debounceCts?.Cancel();
        _debounceCts = new CancellationTokenSource();
        var token = _debounceCts.Token;

        try
        {
            await Task.Delay(SaveDebounceMs, token);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        await _saveGate.WaitAsync();
        try
        {
            await SaveCore();
        }
        finally
        {
            _saveGate.Release();
        }
    }

    // Forces an immediate save, bypassing the debounce. Use on shutdown to guarantee
    // the latest changes are flushed to disk.
    public static async Task FlushAsync()
    {
        _debounceCts?.Cancel();
        await _saveGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await SaveCore().ConfigureAwait(false);
        }
        finally
        {
            _saveGate.Release();
        }
    }
}
