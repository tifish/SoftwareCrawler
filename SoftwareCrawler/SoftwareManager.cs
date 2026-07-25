using System.Text;
using JeekTools;
using Microsoft.Extensions.Logging;
using SoftwareCrawler.Services;
using ZLogger;

namespace SoftwareCrawler;

/// <summary>
/// Loads and saves the software list. Both files live in the active Config
/// folder; the download directories are keyed by software name, so reordering
/// or inserting rows in one file can never shift the other out of alignment.
/// </summary>
public static class SoftwareManager
{
    private static readonly ILogger Log = LogManager.CreateLogger(nameof(SoftwareManager));

    private const string SoftwareFileName = "Software.tab";
    private const string DownloadDirectoryFileName = "DownloadDirectory.tab";
    private const string NameColumn = "Name";

    /// <summary>The crawl definitions, version-controlled and shipped with the app.</summary>
    private static string SoftwarePath =>
        Path.Join(SettingsStore.ResolveConfigRoot(), SoftwareFileName);

    /// <summary>Per-item download directories, next to the list they belong to.</summary>
    private static string DownloadDirectoryPath =>
        Path.Join(SettingsStore.ResolveConfigRoot(), DownloadDirectoryFileName);

    public static List<SoftwareItem> Items { get; private set; } = [];

    public static async Task Load()
    {
        var softwarePath = SoftwarePath;
        Directory.CreateDirectory(Path.GetDirectoryName(softwarePath)!);

        if (!File.Exists(softwarePath))
        {
            Log.ZLogWarning($"No software list at {softwarePath}");
            return;
        }

        var downloadDirectoryPath = DownloadDirectoryPath;

        // Read both files in parallel to reduce startup latency.
        var dataTask = File.ReadAllLinesAsync(softwarePath);
        var extraTask = File.Exists(downloadDirectoryPath)
            ? File.ReadAllLinesAsync(downloadDirectoryPath)
            : Task.FromResult<string[]>([]);
        await Task.WhenAll(dataTask, extraTask);

        var dataLines = dataTask.Result.Skip(1).ToArray();
        var downloadDirectories = ParseDownloadDirectories(extraTask.Result, dataLines);

        Items.Clear();
        foreach (var dataLine in dataLines)
        {
            var item = new SoftwareItem(dataLine, string.Empty);
            if (downloadDirectories.TryGetValue(item.Name, out var extraLine))
                item.FromDataLine(extraLine, SoftwareItem.ExtraProperties);
            Items.Add(item);
        }

        // Remember what we just read: a later save compares against this to notice
        // that somebody else has edited the files in the meantime.
        ConfigChangeMonitor.MarkSelfWrite(softwarePath);
        ConfigChangeMonitor.MarkSelfWrite(downloadDirectoryPath);

        Log.ZLogInformation($"Loaded {Items.Count} software items from {softwarePath}");
    }

    /// <summary>
    /// Maps each software name to its download directory columns. Files written
    /// before the Name column existed were aligned by row index; they are read
    /// that way once and get the key on the next save.
    /// </summary>
    private static Dictionary<string, string> ParseDownloadDirectories(
        string[] lines,
        string[] dataLines
    )
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (lines.Length == 0)
            return result;

        var values = lines.Skip(1).ToArray();

        if (!lines[0].StartsWith(NameColumn + '\t', StringComparison.Ordinal))
        {
            for (var i = 0; i < values.Length && i < dataLines.Length; i++)
            {
                var name = GetNameColumn(dataLines[i]);
                if (name.Length > 0)
                    result[name] = values[i];
            }

            Log.ZLogInformation($"Read {result.Count} download directories by row index");
            return result;
        }

        foreach (var line in values)
        {
            var separator = line.IndexOf('\t');
            if (separator > 0)
                result[line[..separator]] = line[(separator + 1)..];
        }

        return result;
    }

    // Name is the second column of a data line; see SoftwareItem.DataProperties.
    private static string GetNameColumn(string line)
    {
        var columns = line.Split('\t');
        return columns.Length > 1 ? columns[1] : string.Empty;
    }

    /// <summary>Returns false when the write was skipped to protect an outside edit.</summary>
    private static async Task<bool> SaveCore()
    {
        var softwarePath = SoftwarePath;
        var downloadDirectoryPath = DownloadDirectoryPath;
        Directory.CreateDirectory(Path.GetDirectoryName(softwarePath)!);

        // The two files are one document keyed by name, so an outside edit to
        // either makes the in-memory list stale. Writing it back would silently
        // undo that edit, which is worse than losing the app-side change - the
        // app can reload, the user's text editor cannot. The watcher reports the
        // change within its quiet period and the app reloads from disk.
        if (
            ConfigChangeMonitor.HasExternalChange(softwarePath)
            || ConfigChangeMonitor.HasExternalChange(downloadDirectoryPath)
        )
        {
            Log.ZLogWarning(
                $"Skipped saving the software list: {softwarePath} changed outside the app since it was last read"
            );
            return false;
        }

        var dataItems = new List<string>(Items.Count + 1)
        {
            SoftwareItem.GetDataHeaderLine(SoftwareItem.DataProperties),
        };
        dataItems.AddRange(Items.Select(item => item.ToDataLine(SoftwareItem.DataProperties)));

        var extraItems = new List<string>(Items.Count + 1)
        {
            NameColumn + '\t' + SoftwareItem.GetDataHeaderLine(SoftwareItem.ExtraProperties),
        };
        extraItems.AddRange(
            Items.Select(item => item.Name + '\t' + item.ToDataLine(SoftwareItem.ExtraProperties))
        );

        // Write both files in parallel. The scope keeps the watcher from treating
        // our own write as an external edit, and records the new content on exit.
        var encoding = new UTF8Encoding(true);
        using (ConfigChangeMonitor.BeginSelfWrite(softwarePath, downloadDirectoryPath))
        {
            await Task.WhenAll(
                    File.WriteAllLinesAsync(softwarePath, dataItems, encoding),
                    File.WriteAllLinesAsync(downloadDirectoryPath, extraItems, encoding)
                )
                .ConfigureAwait(false);
        }

        return true;
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
            await Task.Delay(SaveDebounceMs, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

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

    // Forces an immediate save, bypassing the debounce. Use on shutdown to guarantee
    // the latest changes are flushed to disk. False means the write was skipped
    // because the files changed outside the app.
    public static async Task<bool> FlushAsync()
    {
        _debounceCts?.Cancel();
        await _saveGate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await SaveCore().ConfigureAwait(false);
        }
        finally
        {
            _saveGate.Release();
        }
    }
}
