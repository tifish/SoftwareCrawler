using JeekTools;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace SoftwareCrawler;

/// <summary>
/// Making sure a download directory is there, without ever hanging on it.
///
/// Download directories often sit on a network share. When such a share goes
/// away, the Win32 calls behind <see cref="Directory.Exists"/> and
/// <see cref="Directory.CreateDirectory"/> do not fail - they stay inside the
/// SMB redirector for minutes on end. On the UI thread that freezes the window
/// and the tray icon with it, so every probe here runs on a thread pool thread
/// and is abandoned once <see cref="ProbeTimeout"/> is up.
///
/// Abandoning one probe is not enough by itself: every other item pointing at
/// the same dead share would pay the same wait, which is how a single offline
/// server stretched one scheduled run past 40 minutes. So a share that timed out
/// is remembered as unreachable for <see cref="UnreachableCooldown"/>, and the
/// items behind it fail at once.
/// </summary>
internal static class DownloadDirectoryAccess
{
    private static readonly ILogger Log = LogManager.CreateLogger(nameof(DownloadDirectoryAccess));

    /// <summary>How long one probe may take before its share counts as unreachable.</summary>
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// How long an unreachable share keeps failing fast. Kept below the gap
    /// between frequent-check runs so a share that comes back is picked up by a
    /// later run instead of staying written off.
    /// </summary>
    private static readonly TimeSpan UnreachableCooldown = TimeSpan.FromMinutes(5);

    /// <summary>Roots that timed out, and when each is worth trying again.</summary>
    private static readonly Dictionary<string, DateTime> RetryRootAfter = new(
        StringComparer.OrdinalIgnoreCase
    );

    /// <summary>The roots currently written off, and until when. For the debug MCP.</summary>
    public static IReadOnlyList<string> UnreachableRoots
    {
        get
        {
            lock (RetryRootAfter)
                return RetryRootAfter
                    .Where(pair => pair.Value > DateTime.Now)
                    .Select(pair => $"{pair.Key} until {pair.Value:HH:mm:ss}")
                    .ToList();
        }
    }

    /// <summary>Forget every written-off root, so a probe tries the share again.</summary>
    public static void ResetUnreachable()
    {
        lock (RetryRootAfter)
            RetryRootAfter.Clear();
    }

    /// <summary>
    /// Make sure <paramref name="directory"/> exists, creating it when it does not.
    /// </summary>
    /// <returns>
    /// null once the directory is usable, otherwise a fragment saying why it is
    /// not, for the item's error message.
    /// </returns>
    public static async Task<string?> EnsureExistsAsync(string directory)
    {
        var root = RootOf(directory);

        if (IsUnreachable(root, out var retryAfter))
            return $"{root} was unreachable, not tried again before {retryAfter:HH:mm:ss}.";

        var probe = Task.Run(() =>
        {
            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);
        });

        try
        {
            await probe.WaitAsync(ProbeTimeout);
        }
        catch (TimeoutException)
        {
            MarkUnreachable(root, probe, directory);
            return $"{root} did not answer within {ProbeTimeout.TotalSeconds:0}s.";
        }
        catch (Exception ex)
        {
            return $"does not exist, and failed to create: {ex.Message}";
        }

        ClearUnreachable(root);
        return null;
    }

    /// <summary>
    /// <see cref="File.Exists"/> off the UI thread, answering false instead of
    /// hanging once the share behind the path is gone.
    /// </summary>
    public static async Task<bool> FileExistsAsync(string path)
    {
        var root = RootOf(path);

        if (IsUnreachable(root, out _))
            return false;

        var probe = Task.Run(() => File.Exists(path));

        try
        {
            var exists = await probe.WaitAsync(ProbeTimeout);
            ClearUnreachable(root);
            return exists;
        }
        catch (TimeoutException)
        {
            MarkUnreachable(root, probe, path);
            return false;
        }
    }

    /// <summary>
    /// The part of a path whose reachability is one yes-or-no answer: the
    /// \\server\share of a UNC path, the drive of anything else.
    /// </summary>
    private static string RootOf(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);

            if (!full.StartsWith(@"\\", StringComparison.Ordinal))
                return Path.GetPathRoot(full) is { Length: > 0 } drive ? drive : full;

            var parts = full.TrimStart('\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 2 ? $@"\\{parts[0]}\{parts[1]}" : full;
        }
        catch (Exception)
        {
            // A path malformed enough to fail here is its own root; the probe
            // will then fail on the path's own merits rather than on a share's.
            return path;
        }
    }

    private static bool IsUnreachable(string root, out DateTime retryAfter)
    {
        lock (RetryRootAfter)
        {
            if (RetryRootAfter.TryGetValue(root, out retryAfter))
            {
                if (retryAfter > DateTime.Now)
                    return true;

                RetryRootAfter.Remove(root);
            }
        }

        return false;
    }

    /// <summary>
    /// Write the root off for a while, and keep an eye on the probe that is still
    /// stuck in the redirector: it holds a thread pool thread until the redirector
    /// lets go, and whatever it throws then has to be observed.
    /// </summary>
    private static void MarkUnreachable(string root, Task abandonedProbe, string path)
    {
        var retryAfter = DateTime.Now + UnreachableCooldown;

        lock (RetryRootAfter)
            RetryRootAfter[root] = retryAfter;

        Log.ZLogWarning(
            $"{path} did not answer within {ProbeTimeout.TotalSeconds:0}s; treating {root} as "
                + $"unreachable until {retryAfter:HH:mm:ss}"
        );

        _ = abandonedProbe.ContinueWith(
            task => Log.ZLogDebug($"Abandoned probe of {path} ended as {task.Status}"),
            TaskScheduler.Default
        );
    }

    private static void ClearUnreachable(string root)
    {
        lock (RetryRootAfter)
            RetryRootAfter.Remove(root);
    }
}
