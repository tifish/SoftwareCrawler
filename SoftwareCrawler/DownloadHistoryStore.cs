using System.Globalization;
using System.Text;
using JeekTools;
using Microsoft.Extensions.Logging;
using SoftwareCrawler.Services;
using ZLogger;

namespace SoftwareCrawler;

/// <summary>
/// When each item was last checked, and how old the file this machine ended up
/// with is. Both are answers to "is this still up to date?", which neither the
/// recipe nor the per-machine settings can give: the recipe is shared, and the
/// settings file is precious user data that must not churn on every crawl.
///
/// So this is its own file, kept under %LOCALAPPDATA% beside the other
/// machine-local data - it describes crawls this computer ran and means nothing
/// on another one - and it is regenerable: losing it costs the history, nothing
/// else. Rows are keyed by name and rows no loaded item claims are written back
/// untouched, so a rename that is undone finds its history still there.
///
/// The whole file is small, so it is read once into memory and written back
/// debounced - a batch of fifty items would otherwise rewrite it a hundred times.
/// </summary>
internal static class DownloadHistoryStore
{
    internal const string FileName = "DownloadHistory.tab";

    private static readonly ILogger Log = LogManager.CreateLogger(nameof(DownloadHistoryStore));

    /// <summary>Local time, written the way a person reading the file would want it.</summary>
    private const string TimeFormat = "yyyy-MM-dd HH:mm:ss";

    private const string HeaderLine = "Name\tLastChecked\tLastDownloadedFileTime";
    private const int SaveDebounceMs = 500;

    internal sealed record Entry(DateTime? LastChecked, DateTime? LastDownloadedFileTime);

    // Everything below is guarded by this: the download pipeline updates from a
    // worker thread while the grid reads from the UI thread.
    private static readonly object Gate = new();
    private static Dictionary<string, Entry>? _entries;
    private static string? _loadedFrom;
    private static bool _dirty;
    private static CancellationTokenSource? _debounceCts;

    private static string PathName => Path.Join(SettingsService.MachineConfigRoot, FileName);

    internal static Entry? Get(string name)
    {
        lock (Gate)
        {
            EnsureLoaded();
            return _entries!.GetValueOrDefault(name);
        }
    }

    /// <summary>
    /// Records the times given and leaves the others as they are. A null means
    /// "nothing new to say about this one", not "clear it".
    /// </summary>
    internal static void Update(
        string name,
        DateTime? lastChecked = null,
        DateTime? lastDownloadedFileTime = null
    )
    {
        if (string.IsNullOrEmpty(name))
            return;

        lock (Gate)
        {
            EnsureLoaded();
            var previous = _entries!.GetValueOrDefault(name);
            var updated = new Entry(
                lastChecked ?? previous?.LastChecked,
                lastDownloadedFileTime ?? previous?.LastDownloadedFileTime
            );
            if (updated == previous)
                return;

            _entries![name] = updated;
            _dirty = true;
            ScheduleSave();
        }
    }

    /// <summary>Writes a pending change now. Called before the process exits.</summary>
    internal static void Flush()
    {
        lock (Gate)
        {
            _debounceCts?.Cancel();
            _debounceCts = null;
            if (!_dirty || _entries is null || _loadedFrom is null)
                return;

            Write(_loadedFrom, _entries);
        }
    }

    private static void ScheduleSave()
    {
        _debounceCts?.Cancel();
        var cancellation = new CancellationTokenSource();
        _debounceCts = cancellation;
        _ = SaveAfterDelay(cancellation.Token);
    }

    private static async Task SaveAfterDelay(CancellationToken token)
    {
        try
        {
            await Task.Delay(SaveDebounceMs, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        Flush();
    }

    /// <summary>
    /// Reads the file once. The machine-local folder is fixed for the life of the
    /// process, so unlike the roaming one it cannot move underneath us.
    /// </summary>
    private static void EnsureLoaded()
    {
        if (_entries is not null)
            return;

        _loadedFrom = PathName;
        _entries = Read(_loadedFrom);
        _dirty = false;
    }

    private static Dictionary<string, Entry> Read(string path)
    {
        try
        {
            return ParseLines(File.Exists(path) ? File.ReadAllLines(path) : []);
        }
        catch (Exception ex)
        {
            // History is a convenience. Failing to read it must never stop a crawl.
            Log.ZLogWarning($"Could not read the download history: {ex.Message}");
            return new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static void Write(string path, Dictionary<string, Entry> entries)
    {
        var temporary = path + $".{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            // Through a temporary file and a rename, so an interrupted write leaves
            // the previous history in place instead of a truncated file. ".tmp" is
            // the extension ConfigChangeMonitor ignores.
            File.WriteAllLines(temporary, BuildLines(entries), new UTF8Encoding(true));
            File.Move(temporary, path, overwrite: true);
            _dirty = false;
        }
        catch (Exception ex)
        {
            // Keep the change in memory; the next update will try again.
            Log.ZLogWarning($"Could not write the download history: {ex.Message}");
        }
        finally
        {
            try
            {
                File.Delete(temporary);
            }
            catch
            {
                // Best-effort cleanup; the rename normally consumed it already.
            }
        }
    }

    internal static IReadOnlyList<string> BuildLines(Dictionary<string, Entry> entries)
    {
        var lines = new List<string>(entries.Count + 1) { HeaderLine };
        lines.AddRange(
            entries
                .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                .Select(entry =>
                    $"{SoftwareItem.EncodeField(entry.Key)}"
                    + $"\t{Format(entry.Value.LastChecked)}"
                    + $"\t{Format(entry.Value.LastDownloadedFileTime)}"
                )
        );
        return lines;
    }

    /// <summary>
    /// Reads the rows below the header. Like the software list, a short row is
    /// one written before a column existed, so its missing values stay empty
    /// rather than costing the whole row.
    /// </summary>
    internal static Dictionary<string, Entry> ParseLines(IReadOnlyList<string> lines)
    {
        var result = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var cells = line.Split('\t');
            var name = SoftwareItem.DecodeField(cells[0]);
            if (name.Length == 0)
                continue;

            result[name] = new Entry(
                cells.Length > 1 ? Parse(cells[1]) : null,
                cells.Length > 2 ? Parse(cells[2]) : null
            );
        }

        return result;
    }

    private static string Format(DateTime? value) =>
        value?.ToString(TimeFormat, CultureInfo.InvariantCulture) ?? string.Empty;

    private static DateTime? Parse(string value) =>
        DateTime.TryParseExact(
            value.Trim(),
            TimeFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var exact
        )
            ? exact
        // Anything else a hand edit or an older build may have left behind.
        : DateTime.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed
        )
            ? parsed
        : null;
}
