using System.Reflection;
using System.Text;
using JeekTools;
using Microsoft.Extensions.Logging;
using SoftwareCrawler.Services;
using ZLogger;

namespace SoftwareCrawler;

/// <summary>
/// Loads and saves the software list. Both files live in the active Config
/// folder; the per-machine rows are keyed by software name, so reordering
/// or inserting rows in one file can never shift the other out of alignment.
/// </summary>
public static class SoftwareManager
{
    private static readonly ILogger Log = LogManager.CreateLogger(nameof(SoftwareManager));

    private const string SoftwareFileName = "Software.tab";
    private const string LocalSettingsFileName = "LocalSettings.tab";
    private const string TemplateFolderName = "Templates";
    private const string NameColumn = "Name";

    /// <summary>
    /// The crawl definitions as shipped: version-controlled, packaged, and the
    /// file a Debug build works on directly so edits are ready to commit.
    /// </summary>
    private static string SoftwareTemplatePath =>
        Path.Join(SettingsService.ProgramRoot, TemplateFolderName, SoftwareFileName);

    /// <summary>
    /// The list the app reads and writes. A Debug build is the development copy
    /// of the template itself; a released build works on a copy in the config
    /// folder, seeded from the template the first time it is missing, so a user's
    /// own edits are never overwritten by an update.
    /// </summary>
    private static string SoftwarePath =>
        DebugInstanceContext.IsDebugBuild
            ? SoftwareTemplatePath
            : Path.Join(SettingsStore.ResolveConfigRoot(), SoftwareFileName);

    /// <summary>This machine's own choices: which items are enabled, and where each downloads to.</summary>
    private static string LocalSettingsPath =>
        Path.Join(SettingsStore.ResolveConfigRoot(), LocalSettingsFileName);

    public static List<SoftwareItem> Items { get; private set; } = [];

    /// <summary>The list actually being read and written. Diagnostics only.</summary>
    public static string ActiveSoftwarePath => SoftwarePath;

    /// <summary>The shipped template the config copy is seeded from. Diagnostics only.</summary>
    public static string TemplatePath => SoftwareTemplatePath;

    /// <summary>
    /// The folder to watch on top of the config folder, empty unless this is a
    /// Debug build. Only a Debug build edits the template in place, so only it
    /// needs to hear about a git checkout landing on the file underneath it.
    /// </summary>
    public static string WatchedTemplateFolder =>
        DebugInstanceContext.IsDebugBuild
            ? Path.GetDirectoryName(SoftwareTemplatePath) ?? string.Empty
            : string.Empty;

    /// <summary>
    /// Rows of LocalSettings.tab that no loaded item claims, kept in file
    /// order so a save writes them back untouched. A name goes missing whenever
    /// the list is temporarily shorter than the file - a half-written
    /// Software.tab, an outside edit, a row deleted and undone - and the row
    /// here is the only copy of those settings there is.
    /// </summary>
    private static List<(string Name, string Line)> _unclaimedLocalSettings = [];

    /// <summary>The names whose per-machine settings are being preserved.</summary>
    public static IReadOnlyList<string> UnclaimedLocalSettingNames =>
        _unclaimedLocalSettings.Select(entry => entry.Name).ToArray();

    /// <summary>
    /// Drops the preserved rows and writes the result, so the file holds nothing
    /// but the current list. Returns false when the write was refused because the
    /// file changed outside the app, in which case nothing is discarded.
    /// </summary>
    public static async Task<bool> RemoveUnclaimedLocalSettings()
    {
        var removed = _unclaimedLocalSettings;
        if (removed.Count == 0)
            return true;

        _unclaimedLocalSettings = [];
        if (await FlushAsync().ConfigureAwait(false))
        {
            Log.ZLogInformation(
                $"Removed {removed.Count} unclaimed local settings: "
                    + $"{string.Join(", ", removed.Select(entry => entry.Name))}"
            );
            return true;
        }

        _unclaimedLocalSettings = removed;
        return false;
    }

    public static async Task Load()
    {
        var softwarePath = SoftwarePath;
        Directory.CreateDirectory(Path.GetDirectoryName(softwarePath)!);

        SeedFromTemplate(softwarePath);

        if (!File.Exists(softwarePath))
        {
            Log.ZLogWarning($"No software list at {softwarePath}");
            return;
        }

        var localSettingsPath = LocalSettingsPath;

        // Read both files in parallel to reduce startup latency.
        var dataTask = File.ReadAllLinesAsync(softwarePath);
        var extraTask = File.Exists(localSettingsPath)
            ? File.ReadAllLinesAsync(localSettingsPath)
            : Task.FromResult<string[]>([]);
        await Task.WhenAll(dataTask, extraTask);

        // Enabled used to be the first column of the shared list. Read such a
        // file with the layout it was written in; the next save moves the flags
        // over to the per-machine file for good.
        var dataProperties = dataTask.Result.Length > 0 && IsLegacyDataHeader(dataTask.Result[0])
            ? SoftwareItem.LegacyDataProperties
            : SoftwareItem.DataProperties;
        if (dataProperties == SoftwareItem.LegacyDataProperties)
            Log.ZLogInformation(
                $"Reading {softwarePath} in the layout that still had the Enabled column"
            );

        var dataLines = dataTask.Result.Skip(1).ToArray();
        var names = dataLines.Select(line => GetNameColumn(line, dataProperties)).ToArray();
        var localSettings = ParseLocalSettings(
            extraTask.Result,
            names,
            out var extraProperties
        );

        Items.Clear();
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dataLine in dataLines)
        {
            // The parameterless constructor keeps the property defaults, which is
            // what an item with no row in the per-machine file should end up with.
            var item = new SoftwareItem();
            item.FromDataLine(dataLine, dataProperties);
            if (localSettings.TryGetValue(item.Name, out var extraLine))
            {
                item.FromDataLine(extraLine, extraProperties);
                claimed.Add(item.Name);
            }
            Items.Add(item);
        }

        // Re-serialize the rows nobody claimed: they may have been read in the old
        // layout, and they are written back under the current header.
        _unclaimedLocalSettings = localSettings
            .Where(entry => !claimed.Contains(entry.Key))
            .Select(entry =>
            {
                var orphan = new SoftwareItem();
                orphan.FromDataLine(entry.Value, extraProperties);
                return (
                    entry.Key,
                    entry.Key + '\t' + orphan.ToDataLine(SoftwareItem.ExtraProperties)
                );
            })
            .ToList();
        if (_unclaimedLocalSettings.Count > 0)
            Log.ZLogInformation(
                $"Keeping {_unclaimedLocalSettings.Count} local settings no item claims: "
                    + $"{string.Join(", ", UnclaimedLocalSettingNames)}"
            );

        // Remember what we just read: a later save compares against this to notice
        // that somebody else has edited the files in the meantime.
        ConfigChangeMonitor.MarkSelfWrite(softwarePath);
        ConfigChangeMonitor.MarkSelfWrite(localSettingsPath);

        Log.ZLogInformation($"Loaded {Items.Count} software items from {softwarePath}");
    }

    /// <summary>
    /// Puts the shipped list in place the first time a config folder has none.
    /// Only ever fills a gap: once the copy exists it belongs to the user, so an
    /// update refreshes the template without touching what they have edited.
    /// A Debug build resolves both paths to the same file and does nothing here.
    /// </summary>
    private static void SeedFromTemplate(string softwarePath)
    {
        try
        {
            var template = SoftwareTemplatePath;
            if (
                File.Exists(softwarePath)
                || !File.Exists(template)
                || string.Equals(template, softwarePath, StringComparison.OrdinalIgnoreCase)
            )
                return;

            File.Copy(template, softwarePath);
            Log.ZLogInformation($"Seeded {softwarePath} from the shipped template {template}");
        }
        catch (Exception ex)
        {
            Log.ZLogWarning($"Could not seed the software list from the template: {ex.Message}");
        }
    }

    /// <summary>
    /// True for a shared list still carrying the Enabled column, which the header
    /// names first. Everything written since starts with Name.
    /// </summary>
    private static bool IsLegacyDataHeader(string header) =>
        header.TrimStart('﻿').StartsWith(nameof(SoftwareItem.Enabled) + '\t', StringComparison.Ordinal);

    private static string GetNameColumn(string line, List<PropertyInfo> properties)
    {
        var index = properties.FindIndex(property => property.Name == NameColumn);
        var columns = line.Split('\t');
        return index >= 0 && index < columns.Length ? columns[index] : string.Empty;
    }

    /// <summary>
    /// Maps each software name to its per-machine columns, and reports which
    /// layout they are in. Three shapes exist: the current one, the one before
    /// Enabled joined it, and files so old they had no Name column and were
    /// aligned by row index. All three get the current shape on the next save.
    /// </summary>
    private static Dictionary<string, string> ParseLocalSettings(
        string[] lines,
        string[] names,
        out List<PropertyInfo> properties
    )
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        properties = SoftwareItem.ExtraProperties;
        if (lines.Length == 0)
            return result;

        var values = lines.Skip(1).ToArray();

        if (!lines[0].TrimStart('﻿').StartsWith(NameColumn + '\t', StringComparison.Ordinal))
        {
            properties = SoftwareItem.LegacyExtraProperties;
            for (var i = 0; i < values.Length && i < names.Length; i++)
                if (names[i].Length > 0)
                    result[names[i]] = values[i];

            Log.ZLogInformation($"Read {result.Count} per-machine rows by row index");
            return result;
        }

        // Keyed by name, but Enabled only joined these columns later.
        if (
            !lines[0].Contains('\t' + nameof(SoftwareItem.Enabled) + '\t', StringComparison.Ordinal)
        )
        {
            properties = SoftwareItem.LegacyExtraProperties;
            Log.ZLogInformation($"Reading the per-machine file in its pre-Enabled layout");
        }

        foreach (var line in values)
        {
            var separator = line.IndexOf('\t');
            if (separator > 0)
                result[line[..separator]] = line[(separator + 1)..];
        }

        return result;
    }

    /// <summary>Returns false when the write was skipped to protect an outside edit.</summary>
    private static async Task<bool> SaveCore()
    {
        var softwarePath = SoftwarePath;
        var localSettingsPath = LocalSettingsPath;
        Directory.CreateDirectory(Path.GetDirectoryName(softwarePath)!);

        // The two files are one document keyed by name, so an outside edit to
        // either makes the in-memory list stale. Writing it back would silently
        // undo that edit, which is worse than losing the app-side change - the
        // app can reload, the user's text editor cannot. The watcher reports the
        // change within its quiet period and the app reloads from disk.
        if (
            ConfigChangeMonitor.HasExternalChange(softwarePath)
            || ConfigChangeMonitor.HasExternalChange(localSettingsPath)
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

        // Carry the unclaimed rows over instead of dropping them. Re-check the
        // names: an item that came back since the load owns its row again, and
        // writing both copies would duplicate it.
        var itemNames = Items.Select(item => item.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        extraItems.AddRange(
            _unclaimedLocalSettings
                .Where(entry => !itemNames.Contains(entry.Name))
                .Select(entry => entry.Line)
        );

        // Keep the state we are about to replace; LocalSettings.tab is not in
        // git and this is the only copy of it there will ever be.
        ConfigBackupService.BackupDaily(softwarePath, localSettingsPath);

        // Write both files in parallel. The scope keeps the watcher from treating
        // our own write as an external edit, and records the new content on exit.
        var encoding = new UTF8Encoding(true);
        using (ConfigChangeMonitor.BeginSelfWrite(softwarePath, localSettingsPath))
        {
            await Task.WhenAll(
                    Task.Run(() => WriteLinesAtomic(softwarePath, dataItems, encoding)),
                    Task.Run(() => WriteLinesAtomic(localSettingsPath, extraItems, encoding))
                )
                .ConfigureAwait(false);
        }

        return true;
    }

    /// <summary>
    /// Writes through a temporary file and renames over the target, so an
    /// interrupted write leaves the previous content intact instead of a
    /// truncated file. Mirrors <see cref="SharedDataFile.WriteAllTextAtomic"/>,
    /// which cannot be used directly because these files carry a BOM.
    /// The ".tmp" name is the one <see cref="ConfigChangeMonitor"/> ignores.
    /// </summary>
    private static void WriteLinesAtomic(string path, IEnumerable<string> lines, Encoding encoding)
    {
        var temporary = path + $".{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllLines(temporary, lines, encoding);
            File.Move(temporary, path, overwrite: true);
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
