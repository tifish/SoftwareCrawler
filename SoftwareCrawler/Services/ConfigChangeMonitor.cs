using System.Collections.Concurrent;
using System.Security.Cryptography;
using JeekTools;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace SoftwareCrawler.Services;

/// <summary>
/// Watches the active Config folder so edits made outside the app (a synced
/// folder, a text editor, another instance) are picked up. Changes are batched
/// until the folder has been quiet for <see cref="QuietPeriod"/>, and only the
/// files that actually changed are reported. Writes the app made itself are
/// filtered out by content hash.
/// </summary>
public static class ConfigChangeMonitor
{
    private static readonly ILogger Log = LogManager.CreateLogger(nameof(ConfigChangeMonitor));

    private static readonly TimeSpan QuietPeriod = TimeSpan.FromSeconds(10);

    private static readonly ConcurrentDictionary<string, string> SelfWriteHashes = new(
        StringComparer.OrdinalIgnoreCase
    );
    private static readonly HashSet<string> Pending = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lock Gate = new();

    private static FileSystemWatcher? _watcher;
    private static CancellationTokenSource? _debounceCts;

    /// <summary>Raised with the full paths of the files that changed on disk.</summary>
    public static event Action<IReadOnlyList<string>>? ConfigChanged;

    public static string Root { get; private set; } = "";

    /// <summary>Starts watching <paramref name="configRoot"/>, replacing any previous watch.</summary>
    public static void Watch(string configRoot)
    {
        Stop();

        Root = configRoot;
        try
        {
            Directory.CreateDirectory(configRoot);
            var watcher = new FileSystemWatcher(configRoot)
            {
                NotifyFilter =
                    NotifyFilters.LastWrite
                    | NotifyFilters.FileName
                    | NotifyFilters.Size
                    | NotifyFilters.CreationTime,
                IncludeSubdirectories = false,
            };
            watcher.Changed += OnChanged;
            watcher.Created += OnChanged;
            watcher.Deleted += OnChanged;
            watcher.Renamed += OnRenamed;
            watcher.Error += (_, e) =>
                Log.ZLogWarning($"Config watcher error: {e.GetException().Message}");
            watcher.EnableRaisingEvents = true;
            _watcher = watcher;
        }
        catch (Exception ex)
        {
            Log.ZLogWarning($"Could not watch config folder {configRoot}: {ex.Message}");
        }
    }

    public static void Stop()
    {
        _debounceCts?.Cancel();
        _debounceCts = null;

        if (_watcher is null)
            return;

        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
        _watcher = null;
    }

    /// <summary>
    /// Records the current content of a file the app just wrote, so the change
    /// event it triggers does not cause a pointless reload.
    /// </summary>
    public static void MarkSelfWrite(string path)
    {
        var hash = TryHash(path);
        if (hash is null)
            SelfWriteHashes.TryRemove(path, out _);
        else
            SelfWriteHashes[path] = hash;
    }

    private static void OnChanged(object sender, FileSystemEventArgs e) => Queue(e.FullPath);

    private static void OnRenamed(object sender, RenamedEventArgs e)
    {
        Queue(e.OldFullPath);
        Queue(e.FullPath);
    }

    private static void Queue(string fullPath)
    {
        // Atomic writes stage through "<name>.<pid>.<guid>.tmp"; those never
        // interest anyone and are gone by the time the batch is flushed.
        if (fullPath.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            return;

        lock (Gate)
        {
            Pending.Add(fullPath);
        }

        _debounceCts?.Cancel();
        var cts = new CancellationTokenSource();
        _debounceCts = cts;
        _ = FlushAfterQuietPeriodAsync(cts.Token);
    }

    private static async Task FlushAfterQuietPeriodAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(QuietPeriod, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Another change arrived; the newer timer takes over.
            return;
        }

        string[] changed;
        lock (Gate)
        {
            changed = Pending.ToArray();
            Pending.Clear();
        }

        // Drop the files whose current content is exactly what the app wrote.
        var external = changed.Where(IsExternalChange).ToArray();
        if (external.Length == 0)
            return;

        Log.ZLogInformation($"Config changed outside the app: {string.Join(", ", external)}");
        ConfigChanged?.Invoke(external);
    }

    private static bool IsExternalChange(string path)
    {
        if (!SelfWriteHashes.TryGetValue(path, out var knownHash))
            return true;

        return TryHash(path) != knownHash;
    }

    private static string? TryHash(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;

            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return Convert.ToHexString(SHA256.HashData(stream));
        }
        catch
        {
            return null;
        }
    }
}
