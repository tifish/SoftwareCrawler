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

    // A folder that keeps changing must not postpone the report forever: once the
    // oldest pending event is this old the batch is flushed even if events are
    // still arriving.
    private static readonly TimeSpan MaxBatchDelay = TimeSpan.FromSeconds(30);

    /// <summary>What the app believes is on disk, and when it started putting it there.</summary>
    private readonly record struct SelfWrite(string Hash, DateTime StartedUtc);

    private static readonly ConcurrentDictionary<string, SelfWrite> SelfWrites = new(
        StringComparer.OrdinalIgnoreCase
    );

    // Path -> the moment the first still-unreported event for it arrived. The
    // timestamp is what makes an external edit distinguishable from our own write
    // after the app has overwritten the file (see IsExternalChange).
    private static readonly Dictionary<string, DateTime> Pending = new(
        StringComparer.OrdinalIgnoreCase
    );
    private static readonly Lock Gate = new();

    private static FileSystemWatcher? _watcher;
    private static CancellationTokenSource? _debounceCts;
    private static DateTime _batchStartedUtc;

    /// <summary>Raised with the full paths of the files that changed on disk.</summary>
    public static event Action<IReadOnlyList<string>>? ConfigChanged;

    public static string Root { get; private set; } = "";

    /// <summary>True while a watcher is attached; a silent watcher failure shows up here.</summary>
    public static bool IsWatching => _watcher is { EnableRaisingEvents: true };

    /// <summary>The files the app has a known-content baseline for. Diagnostics only.</summary>
    public static IReadOnlyList<string> TrackedPaths => SelfWrites.Keys.Order().ToArray();

    /// <summary>Describes the baseline recorded for a file. Diagnostics only.</summary>
    public static string DescribeKnown(string path) =>
        SelfWrites.TryGetValue(path, out var self)
            ? $"{Short(self.Hash)}@{self.StartedUtc.ToLocalTime():HH:mm:ss.fff}"
            : "(none)";

    /// <summary>Describes the events waiting for the quiet period. Diagnostics only.</summary>
    public static string DescribePending()
    {
        lock (Gate)
        {
            return Pending.Count == 0
                ? "(none)"
                : string.Join(
                    ", ",
                    Pending.Select(p =>
                        $"{Path.GetFileName(p.Key)}@{p.Value.ToLocalTime():HH:mm:ss.fff}"
                    )
                );
        }
    }

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
        lock (Gate)
        {
            _debounceCts?.Cancel();
            _debounceCts = null;
            Pending.Clear();
        }

        if (_watcher is null)
            return;

        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
        _watcher = null;
    }

    /// <summary>
    /// Brackets a write the app is about to make. The start time is what lets the
    /// watcher tell "the app wrote this" from "somebody else wrote this and the app
    /// then overwrote it": an event that predates the write cannot have been caused
    /// by it, however well the final content matches. Disposing records the content
    /// that ended up on disk.
    /// </summary>
    public static IDisposable BeginSelfWrite(params string[] paths) => new SelfWriteScope(paths);

    private sealed class SelfWriteScope(string[] paths) : IDisposable
    {
        private readonly DateTime _startedUtc = DateTime.UtcNow;

        public void Dispose()
        {
            foreach (var path in paths)
                MarkSelfWrite(path, _startedUtc);
        }
    }

    /// <summary>
    /// Records the current content of a file the app just wrote or read, so the
    /// change event it triggers does not cause a pointless reload, and so
    /// <see cref="HasExternalChange"/> has a baseline to compare against.
    /// </summary>
    public static void MarkSelfWrite(string path) => MarkSelfWrite(path, DateTime.UtcNow);

    private static void MarkSelfWrite(string path, DateTime startedUtc)
    {
        var hash = TryHash(path);
        Log.ZLogDebug($"Config watcher: MarkSelfWrite {path} -> {Short(hash)}");
        if (hash is null)
            SelfWrites.TryRemove(path, out _);
        else
            SelfWrites[path] = new SelfWrite(hash, startedUtc);
    }

    /// <summary>
    /// True when the file on disk no longer holds what the app last read or wrote,
    /// i.e. overwriting it now would throw away somebody else's edit. False when
    /// there is no baseline to compare against, so a first write still goes ahead.
    /// </summary>
    public static bool HasExternalChange(string path)
    {
        if (!SelfWrites.TryGetValue(path, out var self))
            return false;

        return TryHash(path) != self.Hash;
    }

    private static void OnChanged(object sender, FileSystemEventArgs e)
    {
        Log.ZLogDebug($"Config watcher raw event: {e.ChangeType} {e.FullPath}");
        Queue(e.FullPath);
    }

    private static void OnRenamed(object sender, RenamedEventArgs e)
    {
        Log.ZLogDebug($"Config watcher raw event: Renamed {e.OldFullPath} -> {e.FullPath}");
        Queue(e.OldFullPath);
        Queue(e.FullPath);
    }

    private static void Queue(string fullPath)
    {
        // Atomic writes stage through "<name>.<pid>.<guid>.tmp"; those never
        // interest anyone and are gone by the time the batch is flushed.
        if (fullPath.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            return;

        var now = DateTime.UtcNow;
        bool flushNow;
        lock (Gate)
        {
            // Keep the earliest sighting: the batch is judged by when the file was
            // first touched, not by the last event of a burst.
            if (!Pending.TryAdd(fullPath, now))
                now = Pending[fullPath];

            if (Pending.Count == 1 && _debounceCts is null)
                _batchStartedUtc = now;

            flushNow = DateTime.UtcNow - _batchStartedUtc >= MaxBatchDelay;

            _debounceCts?.Cancel();
            if (flushNow)
            {
                _debounceCts = null;
            }
            else
            {
                var cts = new CancellationTokenSource();
                _debounceCts = cts;
                _ = FlushAfterQuietPeriodAsync(cts.Token);
            }
        }

        if (flushNow)
            Flush();
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

        Flush();
    }

    private static void Flush()
    {
        KeyValuePair<string, DateTime>[] changed;
        lock (Gate)
        {
            changed = Pending.ToArray();
            Pending.Clear();
            _debounceCts = null;
        }

        if (changed.Length == 0)
            return;

        Log.ZLogDebug(
            $"Config watcher flush: {changed.Length} pending [{string.Join(", ", changed.Select(c => c.Key))}]"
        );

        // Drop the files whose current content is exactly what the app wrote.
        var external = changed
            .Where(c => IsExternalChange(c.Key, c.Value))
            .Select(c => c.Key)
            .ToArray();
        if (external.Length == 0)
            return;

        Log.ZLogInformation($"Config changed outside the app: {string.Join(", ", external)}");
        ConfigChanged?.Invoke(external);
    }

    private static bool IsExternalChange(string path, DateTime firstSeenUtc)
    {
        if (!SelfWrites.TryGetValue(path, out var self))
        {
            Log.ZLogDebug($"Config watcher: {path} has no self-write hash -> external");
            return true;
        }

        var current = TryHash(path);
        if (current != self.Hash)
        {
            Log.ZLogDebug($"Config watcher: {path} self={Short(self.Hash)} disk={Short(current)} -> external");
            return true;
        }

        // The content matches ours, but an event that arrived before the write
        // started was not caused by it: somebody else changed the file first and
        // the app has since overwritten it. Report it so the app reloads.
        if (firstSeenUtc < self.StartedUtc)
        {
            Log.ZLogDebug(
                $"Config watcher: {path} changed at {firstSeenUtc:HH:mm:ss.fff}Z before our write at {self.StartedUtc:HH:mm:ss.fff}Z -> external"
            );
            return true;
        }

        Log.ZLogDebug($"Config watcher: {path} self={Short(self.Hash)} disk={Short(current)} -> self");
        return false;
    }

    private static string Short(string? hash) => hash is null ? "(none)" : hash[..8];

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
