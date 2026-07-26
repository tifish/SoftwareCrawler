using System.Globalization;
using JeekTools;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace SoftwareCrawler.Services;

/// <summary>
/// Keeps a daily copy of the config files, taken just before the app overwrites
/// them. Software.tab is version-controlled and settings.json can be rebuilt from
/// defaults, but LocalSettings.tab exists in exactly one place - a bad write,
/// a mistaken edit or a stray "git clean -xfd" used to leave nothing to restore
/// from.
///
/// The backups deliberately live outside the program folder: bin/Config is
/// git-ignored wholesale, so copies kept next to the originals would be swept
/// away by the very command they are meant to survive.
/// </summary>
public static class ConfigBackupService
{
    private static readonly ILogger Log = LogManager.CreateLogger(nameof(ConfigBackupService));

    private const string DayFormat = "yyyy-MM-dd";

    private static readonly TimeSpan Retention = TimeSpan.FromDays(30);

    /// <summary>One folder per day, each holding that day's first pre-write copy.</summary>
    public static string Root => Path.Join(SettingsService.LocalDataRoot, "Backups");

    /// <summary>
    /// Copies every file that has no backup for today yet, then drops the days
    /// that aged out. One copy a day is the useful granularity: it preserves the
    /// state as the app found it, which is what a mistake made today destroys.
    /// Best effort throughout - saving must never fail over a backup.
    /// </summary>
    public static void BackupDaily(params string[] paths)
    {
        var folder = Path.Join(Root, DateTime.Now.ToString(DayFormat, CultureInfo.InvariantCulture));

        foreach (var path in paths)
        {
            try
            {
                if (!File.Exists(path))
                    continue;

                var destination = Path.Join(folder, Path.GetFileName(path));
                if (File.Exists(destination))
                    continue;

                Directory.CreateDirectory(folder);
                File.Copy(path, destination);
                Log.ZLogInformation($"Backed up {Path.GetFileName(path)} to {destination}");
            }
            catch (Exception ex)
            {
                Log.ZLogWarning($"Could not back up {path}: {ex.Message}");
            }
        }

        Prune();
    }

    private static void Prune()
    {
        try
        {
            if (!Directory.Exists(Root))
                return;

            var oldest = DateTime.Today - Retention;
            foreach (var folder in Directory.EnumerateDirectories(Root))
            {
                if (
                    !DateTime.TryParseExact(
                        Path.GetFileName(folder),
                        DayFormat,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out var day
                    )
                )
                    continue;

                if (day >= oldest)
                    continue;

                Directory.Delete(folder, recursive: true);
                Log.ZLogInformation($"Removed config backup older than {Retention.Days} days: {folder}");
            }
        }
        catch (Exception ex)
        {
            Log.ZLogWarning($"Could not prune config backups: {ex.Message}");
        }
    }

    /// <summary>The days that currently have a backup, newest first. Diagnostics only.</summary>
    public static IReadOnlyList<string> Describe()
    {
        try
        {
            if (!Directory.Exists(Root))
                return [];

            return Directory
                .EnumerateDirectories(Root)
                .OrderByDescending(folder => folder, StringComparer.Ordinal)
                .Select(folder =>
                    $"{Path.GetFileName(folder)}: {string.Join(", ", Directory.EnumerateFiles(folder).Select(Path.GetFileName))}"
                )
                .ToArray();
        }
        catch (Exception ex)
        {
            return [$"(unreadable: {ex.Message})"];
        }
    }
}
