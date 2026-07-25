using JeekTools;
using Microsoft.Extensions.Logging;
using SoftwareCrawler.Models;
using ZLogger;

namespace SoftwareCrawler.Services;

/// <summary>
/// App-specific configuration over the generic <see cref="AutoUpdater"/> in
/// JeekTools. See that class for how checking, staging, and installing work.
/// </summary>
public static class AutoUpdateService
{
    private static readonly ILogger Log = LogManager.CreateLogger(nameof(AutoUpdateService));

    private const string ReleaseBase =
        "https://github.com/tifish/SoftwareCrawler/releases/download/latest_release";

    private static readonly AutoUpdater Updater = new(
        new AutoUpdaterOptions
        {
            AppExeName = "SoftwareCrawler.exe",
            ReleaseZipUrl = $"{ReleaseBase}/SoftwareCrawler.zip",
            VersionTxtUrl = $"{ReleaseBase}/version.txt",
            UserAgent = "SoftwareCrawler-Updater/1.0",
            // Debug instances never self-update, and parallel worktree instances
            // stage into isolated temp roots so they never fight over files.
            Disabled = DebugInstanceContext.IsDebugBuild,
            TempRoot = DebugInstanceContext.IsDebugBuild
                ? DebugInstanceContext.RuntimeTempRoot
                : null,
        }
    );

    public static string DownloadUrl => Updater.DownloadUrl;
    public static IReadOnlyList<string> DownloadUrls => Updater.DownloadUrls;
    public static int LocalCommitCount => Updater.LocalVersion;
    public static int RemoteCommitCount => Updater.RemoteVersion;
    public static string FailureReason => Updater.FailureReason;

    public static int GetLocalCommitCount() => Updater.GetLocalVersion();

    /// <summary>The version shown in the UI; dev builds report themselves as such.</summary>
    public static string GetDisplayVersion()
    {
        var version = GetLocalCommitCount();
        return version <= 0 ? "dev build" : version.ToString();
    }

    public static Task<UpdateCheckOutcome> HasUpdateAsync() => Updater.HasUpdateAsync();

    public static Task<string?> DownloadAndStageAsync(
        IReadOnlyList<string>? urls = null,
        IProgress<UpdateDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default
    ) => Updater.DownloadAndStageAsync(urls, progress, cancellationToken);

    public static bool LaunchInstall(string stagedPackageDir) =>
        Updater.LaunchInstall(stagedPackageDir);

    public static TimeSpan? GetCheckInterval(UpdateCheckFrequency frequency) =>
        frequency switch
        {
            UpdateCheckFrequency.EverySixHours => TimeSpan.FromHours(6),
            UpdateCheckFrequency.Daily => TimeSpan.FromDays(1),
            UpdateCheckFrequency.Weekly => TimeSpan.FromDays(7),
            _ => null,
        };

    /// <summary>
    /// Checks for an update, downloads it, and hands it to the updater script.
    /// Returns true when the install was launched (the app is about to exit).
    /// </summary>
    public static async Task<bool> CheckAndInstallAsync(CancellationToken cancellationToken = default)
    {
        var outcome = await HasUpdateAsync().ConfigureAwait(false);
        if (outcome != UpdateCheckOutcome.Available)
            return false;

        var staged = await DownloadAndStageAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (staged is null)
        {
            Log.ZLogWarning($"Update staging failed: {FailureReason}");
            return false;
        }

        return LaunchInstall(staged);
    }
}
