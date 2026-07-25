using System.Text;
using Microsoft.Extensions.Logging;
using JeekTools;
using SoftwareCrawler.Services;
using ZLogger;

namespace SoftwareCrawler;

/// <summary>
/// Loads and saves the software list. The crawl definitions roam with the
/// active Config folder, while the per-item download directories are local
/// paths and therefore stay in the machine-local Config folder, keyed by name
/// so the two files can never drift out of alignment.
/// </summary>
public static class SoftwareManager
{
    private static readonly ILogger Log = LogManager.CreateLogger(nameof(SoftwareManager));

    private const string SoftwareFileName = "Software.tab";
    private const string DownloadDirectoryFileName = "DownloadDirectory.tab";

    /// <summary>The curated list shipped with the app, used to seed a fresh install.</summary>
    private static string BuiltInSoftwarePath =>
        Path.Join(SettingsService.BuiltInDataRoot, SoftwareFileName);

    /// <summary>The user's working copy of the list, in the active Config folder.</summary>
    private static string SoftwarePath =>
        Path.Join(SettingsStore.ResolveConfigRoot(), SoftwareFileName);

    /// <summary>Per-item download directories: local paths, so never roamed.</summary>
    private static string DownloadDirectoryPath =>
        Path.Join(SettingsService.MachineConfigRoot, DownloadDirectoryFileName);

    public static List<SoftwareItem> Items { get; private set; } = [];

    public static async Task Load()
    {
        var softwarePath = SoftwarePath;
        Directory.CreateDirectory(Path.GetDirectoryName(softwarePath)!);
        Directory.CreateDirectory(SettingsService.MachineConfigRoot);

        SeedFromBuiltInList(softwarePath);

        if (!File.Exists(softwarePath))
            return;

        var dataLines = (await File.ReadAllLinesAsync(softwarePath)).Skip(1).ToArray();
        var downloadDirectories = await LoadDownloadDirectories(
            dataLines.Select(GetNameColumn).ToArray()
        );

        Items.Clear();
        foreach (var dataLine in dataLines)
        {
            var item = new SoftwareItem(dataLine, string.Empty);
            if (downloadDirectories.TryGetValue(item.Name, out var extraLine))
                item.FromDataLine(extraLine, SoftwareItem.ExtraProperties);
            Items.Add(item);
        }
    }

    /// <summary>
    /// Copies the shipped list on first run, and afterwards only appends entries
    /// added by a newer release, so a user's own edits are never overwritten.
    /// </summary>
    private static void SeedFromBuiltInList(string softwarePath)
    {
        try
        {
            var builtInPath = BuiltInSoftwarePath;
            if (!File.Exists(builtInPath))
                return;

            if (!File.Exists(softwarePath))
            {
                File.Copy(builtInPath, softwarePath);
                Log.ZLogInformation($"Seeded the software list from {builtInPath}");
                return;
            }

            var existingLines = File.ReadAllLines(softwarePath);
            var existingNames = existingLines
                .Skip(1)
                .Select(GetNameColumn)
                .Where(name => name.Length > 0)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var newLines = File.ReadAllLines(builtInPath)
                .Skip(1)
                .Where(line => line.Trim().Length > 0)
                .Where(line => !existingNames.Contains(GetNameColumn(line)))
                .ToArray();
            if (newLines.Length == 0)
                return;

            File.AppendAllLines(softwarePath, newLines, new UTF8Encoding(true));
            Log.ZLogInformation($"Added {newLines.Length} new entries from the shipped list");
        }
        catch (Exception ex)
        {
            Log.ZLogWarning(ex, $"Could not merge the shipped software list");
        }
    }

    // Name is the second column of a data line; see SoftwareItem.DataProperties.
    private static string GetNameColumn(string line)
    {
        var columns = line.Split('\t');
        return columns.Length > 1 ? columns[1] : string.Empty;
    }

    private static async Task<Dictionary<string, string>> LoadDownloadDirectories(
        string[] orderedNames
    )
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var path = DownloadDirectoryPath;

        if (!File.Exists(path))
        {
            var legacy = await MigrateLegacyDownloadDirectories(orderedNames);
            return legacy ?? result;
        }

        var lines = await File.ReadAllLinesAsync(path);
        foreach (var line in lines.Skip(1))
        {
            var separator = line.IndexOf('\t');
            if (separator <= 0)
                continue;

            result[line[..separator]] = line[(separator + 1)..];
        }

        return result;
    }

    /// <summary>
    /// Adopts a pre-split DownloadDirectory.tab: it lived next to Software.tab
    /// and was aligned by row index. The values are local paths, so they move to
    /// the machine-local Config folder and get keyed by name on the way.
    /// </summary>
    private static async Task<Dictionary<string, string>?> MigrateLegacyDownloadDirectories(
        string[] orderedNames
    )
    {
        var candidates = new[]
        {
            Path.Join(SettingsStore.ResolveConfigRoot(), DownloadDirectoryFileName),
            Path.Join(SettingsService.ProgramConfigRoot, DownloadDirectoryFileName),
        };
        var legacyPath = candidates.FirstOrDefault(File.Exists);
        if (legacyPath is null)
            return null;

        try
        {
            var lines = await File.ReadAllLinesAsync(legacyPath);
            if (lines.Length == 0)
                return null;

            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var values = lines.Skip(1).ToArray();
            for (var i = 0; i < values.Length && i < orderedNames.Length; i++)
                if (orderedNames[i].Length > 0)
                    result[orderedNames[i]] = values[i];

            await WriteDownloadDirectories(result, orderedNames);
            File.Delete(legacyPath);
            Log.ZLogInformation(
                $"Moved {result.Count} download directories from {legacyPath} to {DownloadDirectoryPath}"
            );
            return result;
        }
        catch (Exception ex)
        {
            Log.ZLogWarning(ex, $"Could not migrate {legacyPath}");
            return null;
        }
    }

    private static async Task WriteDownloadDirectories(
        Dictionary<string, string> byName,
        string[] orderedNames
    )
    {
        var path = DownloadDirectoryPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var lines = new List<string>(orderedNames.Length + 1) { DownloadDirectoryHeader };
        lines.AddRange(
            orderedNames.Select(name =>
                name + '\t' + (byName.TryGetValue(name, out var value) ? value : "")
            )
        );
        await File.WriteAllLinesAsync(path, lines, new UTF8Encoding(true));
    }

    private static string DownloadDirectoryHeader =>
        "Name\t" + SoftwareItem.GetDataHeaderLine(SoftwareItem.ExtraProperties);

    private static async Task SaveCore()
    {
        var softwarePath = SoftwarePath;
        var downloadDirectoryPath = DownloadDirectoryPath;
        Directory.CreateDirectory(Path.GetDirectoryName(softwarePath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(downloadDirectoryPath)!);

        var dataItems = new List<string>(Items.Count + 1)
        {
            SoftwareItem.GetDataHeaderLine(SoftwareItem.DataProperties),
        };
        dataItems.AddRange(Items.Select(item => item.ToDataLine(SoftwareItem.DataProperties)));

        var extraItems = new List<string>(Items.Count + 1) { DownloadDirectoryHeader };
        extraItems.AddRange(
            Items.Select(item =>
                item.Name + '\t' + item.ToDataLine(SoftwareItem.ExtraProperties)
            )
        );

        // Write both files in parallel.
        var encoding = new UTF8Encoding(true);
        await Task.WhenAll(
                File.WriteAllLinesAsync(softwarePath, dataItems, encoding),
                File.WriteAllLinesAsync(downloadDirectoryPath, extraItems, encoding)
            )
            .ConfigureAwait(false);

        // Keep the watcher from treating our own write as an external edit.
        ConfigChangeMonitor.MarkSelfWrite(softwarePath);
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
