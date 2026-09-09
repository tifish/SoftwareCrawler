using JeekTools;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace SoftwareCrawler.Services;

/// <summary>
/// Moves the files that describe *this machine* out of the roaming config folder,
/// where earlier builds kept them, and into the machine-local one.
///
/// None of them mean anything on another computer: the enabled flags point at
/// download directories that exist here, and both the schedule state and the
/// download history describe crawls this machine ran. Left in a roaming or
/// portable folder they would follow the user to a second machine and claim to
/// describe it.
///
/// Runs once at startup, before anything reads them. A file only moves when the
/// machine-local copy does not exist yet, so the destination always wins and the
/// migration is safe to run again.
/// </summary>
public static class MachineDataMigration
{
    private static readonly ILogger Log = LogManager.CreateLogger(nameof(MachineDataMigration));

    /// <summary>
    /// The owners are <see cref="SoftwareManager"/> (LocalSettings.tab),
    /// <see cref="DownloadScheduler"/> (ScheduleState.json) and
    /// DownloadHistoryStore (DownloadHistory.tab). Adding a machine-local file
    /// means adding it here too, or an upgrade silently starts from empty.
    /// </summary>
    internal static readonly string[] FileNames =
    [
        "LocalSettings.tab",
        "ScheduleState.json",
        "DownloadHistory.tab",
    ];

    /// <summary>Moves what is left in the active config folder, if anything.</summary>
    public static void Run() =>
        Run(
            SettingsStore.ResolveConfigRoot(),
            SettingsService.MachineConfigRoot,
            path => ConfigBackupService.BackupDaily(path)
        );

    /// <summary>
    /// Returns the names actually moved. Best effort per file: one that cannot be
    /// moved is left where it is and read from nowhere, which costs the history
    /// or one extra crawl - never the file itself. The backup step is a parameter
    /// so a test cannot overwrite today's real backup with a fixture.
    /// </summary>
    internal static IReadOnlyList<string> Run(
        string sourceRoot,
        string destinationRoot,
        Action<string> backup
    )
    {
        var moved = new List<string>();
        if (string.Equals(sourceRoot, destinationRoot, StringComparison.OrdinalIgnoreCase))
            return moved;

        foreach (var fileName in FileNames)
        {
            var source = Path.Join(sourceRoot, fileName);
            var destination = Path.Join(destinationRoot, fileName);
            try
            {
                if (!File.Exists(source))
                    continue;

                // The machine-local copy is the one in use. Say so rather than
                // leaving somebody to wonder why edits to the old file do
                // nothing - a second worktree on this machine lands here.
                if (File.Exists(destination))
                {
                    Log.ZLogInformation(
                        $"{fileName} is already machine-local; the copy in {sourceRoot} is ignored"
                    );
                    continue;
                }

                // LocalSettings.tab has no version control and no second copy, so
                // the move leaves a dated one behind before touching anything.
                backup(source);

                Directory.CreateDirectory(destinationRoot);
                File.Move(source, destination);
                moved.Add(fileName);
            }
            catch (Exception ex)
            {
                Log.ZLogWarning($"Could not move {fileName} to {destinationRoot}: {ex.Message}");
            }
        }

        if (moved.Count > 0)
            Log.ZLogInformation(
                $"Moved machine-local data from {sourceRoot} to {destinationRoot}: "
                    + $"{string.Join(", ", moved)}"
            );

        return moved;
    }
}
