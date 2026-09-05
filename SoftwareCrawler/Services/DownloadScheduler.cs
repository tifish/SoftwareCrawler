using System.Text.Json;
using JeekTools;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace SoftwareCrawler.Services;

/// <summary>What the scheduler is waiting on right now; surfaced to the debug tools.</summary>
public sealed record ScheduleStatus(
    IReadOnlyList<string> ScheduledDownloadTimes,
    int FrequentCheckIntervalMinutes,
    int FrequentItemCount,
    DateTime? LastFullRun,
    DateTime? LastFrequentRun,
    DateTime? NextFullDue,
    DateTime? NextFrequentDue,
    bool IsRunning,
    string LastSkipReason
);

/// <summary>
/// The in-app replacement for the Windows scheduled task. Two schedules share one
/// timer and one <see cref="DownloadBatch"/>: the configured times of day run every
/// enabled item, and a short interval re-checks only the items marked
/// <see cref="SoftwareItem.FrequentCheck"/>.
///
/// This lives in the app rather than in Task Scheduler because a resident instance
/// holds the single-instance lock — an external task launching a second copy would
/// only ever be turned away (see <see cref="SingleInstanceGuard"/>).
///
/// The two schedules differ in what a bad moment means. A frequent sweep that lands
/// while the user is busy is *dropped*: another one is ten minutes away, and running
/// late is worse than not running. A full run is only *deferred* — it comes round a
/// handful of times a day, so it keeps asking every tick until it gets through.
/// </summary>
public sealed class DownloadScheduler
{
    private static readonly ILogger Log = LogManager.CreateLogger(nameof(DownloadScheduler));

    /// <summary>
    /// Coarse enough to cost nothing, fine enough that a one-minute frequent
    /// interval still behaves, and that a deferred full run starts soon after the
    /// user steps away.
    /// </summary>
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(30);

    private readonly Func<ScheduledRunKind, IReadOnlyList<SoftwareItem>, Task> _runAsync;
    private readonly Func<bool> _isBatchRunning;
    private readonly Func<bool> _isWindowVisible;

    private System.Windows.Forms.Timer? _timer;
    private bool _isRunning;
    private DateTime? _lastFullRun;
    private DateTime? _lastFrequentRun;
    private string _lastSkipReason = "";

    /// <param name="runAsync">Runs one batch. Supplied by the window, which owns the browser.</param>
    /// <param name="isBatchRunning">True while any batch — scheduled or hand-started — is in flight.</param>
    /// <param name="isWindowVisible">True while the main window is up, i.e. someone is working in it.</param>
    public DownloadScheduler(
        Func<ScheduledRunKind, IReadOnlyList<SoftwareItem>, Task> runAsync,
        Func<bool> isBatchRunning,
        Func<bool> isWindowVisible
    )
    {
        _runAsync = runAsync;
        _isBatchRunning = isBatchRunning;
        _isWindowVisible = isWindowVisible;
    }

    /// <summary>
    /// Starts ticking. A first-ever start does not fire a full run: the missed-run
    /// catch-up is anchored to the moment that most recently passed, so installing
    /// the app at 09:00 does not immediately kick off a 113-item crawl.
    /// </summary>
    public void Start()
    {
        var now = DateTime.Now;
        _lastFullRun = LoadLastFullRun();
        if (_lastFullRun is null)
        {
            _lastFullRun = DownloadSchedulePlanner.MostRecentDue(now, ScheduledTimes());
            SaveLastFullRun();
        }

        // Anchoring to now rather than null means the first sweep is one full
        // interval away, so startup and the first crawl do not pile up.
        _lastFrequentRun = now;

        _timer = new System.Windows.Forms.Timer { Interval = (int)TickInterval.TotalMilliseconds };
        _timer.Tick += (_, _) => _ = TickAsync();
        _timer.Start();

        Log.ZLogInformation(
            $"Scheduler started: full runs at [{string.Join(", ", Settings.ScheduledDownloadTimes)}], "
                + $"frequent check every {Settings.FrequentCheckIntervalMinutes} min, "
                + $"last full run {_lastFullRun?.ToString("yyyy-MM-dd HH:mm") ?? "never"}"
        );
    }

    public void Stop()
    {
        _timer?.Stop();
        _timer?.Dispose();
        _timer = null;
    }

    /// <summary>Runs the given schedule right now, ignoring the gate. For the debug tools.</summary>
    public async Task<bool> RunNowAsync(ScheduledRunKind kind)
    {
        if (_isRunning || _isBatchRunning())
            return false;

        await RunAsync(kind, DateTime.Now);
        return true;
    }

    public ScheduleStatus GetStatus()
    {
        var now = DateTime.Now;
        var times = ScheduledTimes();

        return new ScheduleStatus(
            Settings.ScheduledDownloadTimes.ToList(),
            Settings.FrequentCheckIntervalMinutes,
            FrequentItems().Count,
            _lastFullRun,
            _lastFrequentRun,
            DownloadSchedulePlanner.NextDue(now, times),
            _lastFrequentRun?.AddMinutes(Settings.FrequentCheckIntervalMinutes),
            _isRunning,
            _lastSkipReason
        );
    }

    private static List<TimeOnly> ScheduledTimes() =>
        DownloadSchedulePlanner.ParseTimes(Settings.ScheduledDownloadTimes);

    private static List<SoftwareItem> FrequentItems() =>
        SoftwareManager.Items.Where(item => item is { Enabled: true, FrequentCheck: true }).ToList();

    private async Task TickAsync()
    {
        // A run outlives many ticks; the timer keeps firing through the await.
        if (_isRunning)
            return;

        try
        {
            var now = DateTime.Now;

            var (fullDue, frequentDue) = DownloadSchedulePlanner.GetDueRuns(
                now,
                _lastFullRun,
                ScheduledTimes(),
                _lastFrequentRun,
                Settings.FrequentCheckIntervalMinutes
            );

            if (fullDue)
            {
                if (CanRunNow(ScheduledRunKind.Full, out var reason))
                {
                    _lastFullRun = now;
                    SaveLastFullRun();
                    await RunAsync(ScheduledRunKind.Full, now);
                    return;
                }

                // Deferred, not dropped: leave _lastFullRun alone so the next tick
                // asks again. Then fall through rather than returning — a full run
                // waiting for the user to step away must not take the frequent
                // sweep down with it. It waited hours once, and the sweep, which
                // has its own laxer conditions, did not run once in all that time.
                NoteSkip($"Full run deferred: {reason}");
            }

            if (!frequentDue)
                return;

            // Advance first: a sweep that cannot run is skipped outright, never
            // queued up to fire late on top of the next one.
            _lastFrequentRun = now;

            if (FrequentItems().Count == 0)
                return;

            if (!CanRunNow(ScheduledRunKind.Frequent, out var skipReason))
            {
                NoteSkip($"Frequent check skipped: {skipReason}");
                return;
            }

            await RunAsync(ScheduledRunKind.Frequent, now);
        }
        catch (Exception ex)
        {
            Log.ZLogError(ex, $"Scheduler tick failed");
        }
    }

    private async Task RunAsync(ScheduledRunKind kind, DateTime now)
    {
        _isRunning = true;
        _lastSkipReason = "";

        try
        {
            var items =
                kind == ScheduledRunKind.Full ? SoftwareManager.Items.ToList() : FrequentItems();

            Log.ZLogInformation($"Scheduled {kind} run starts with {items.Count} item(s)");
            await _runAsync(kind, items);
        }
        catch (Exception ex)
        {
            Log.ZLogError(ex, $"Scheduled {kind} run failed");
        }
        finally
        {
            _isRunning = false;

            // A full run can easily outlast the next frequent slot; restart that
            // clock so the sweep does not fire the instant the batch lets go.
            if (kind == ScheduledRunKind.Full)
                _lastFrequentRun = DateTime.Now;
        }
    }

    /// <summary>
    /// The whole "do not disturb" contract in one place: never run on top of
    /// another batch, and never run while Windows says the user is busy.
    ///
    /// An open main window only holds back the <em>full</em> run, which occupies the
    /// browser for something like ten minutes and would take the grid over while
    /// someone is working in it. The frequent sweep visits a handful of named items
    /// for a few seconds, and putting fresh status in that grid is the entire point
    /// of it — holding it back whenever the window is open meant it never ran while
    /// anyone could see it, which is how this was first noticed.
    /// </summary>
    private bool CanRunNow(ScheduledRunKind kind, out string reason)
    {
        if (_isBatchRunning())
        {
            reason = "a download batch is already running";
            return false;
        }

        if (kind == ScheduledRunKind.Full && _isWindowVisible())
        {
            reason = "the main window is open";
            return false;
        }

        if (UserPresence.IsBusy(out var busyReason))
        {
            reason = busyReason;
            return false;
        }

        reason = "";
        return true;
    }

    /// <summary>
    /// Records why a run did not happen, logging only when the reason changes: a
    /// deferred full run re-checks every 30 seconds and would otherwise fill the
    /// log with the same line.
    /// </summary>
    private void NoteSkip(string reason)
    {
        if (_lastSkipReason == reason)
            return;

        _lastSkipReason = reason;
        Log.ZLogInformation($"{reason}");
    }

    // Only the full run's timestamp survives a restart. The frequent sweep's
    // interval is short enough that resuming from "now" costs at most one cycle,
    // whereas forgetting the last full run would re-crawl everything on restart.
    private static string StateFilePath =>
        Path.Join(SettingsStore.ResolveConfigRoot(), "ScheduleState.json");

    private sealed class ScheduleState
    {
        public DateTime? LastFullRun { get; set; }
    }

    private static DateTime? LoadLastFullRun()
    {
        try
        {
            var path = StateFilePath;
            if (!File.Exists(path))
                return null;

            return JsonSerializer.Deserialize<ScheduleState>(File.ReadAllText(path))?.LastFullRun;
        }
        catch (Exception ex)
        {
            Log.ZLogWarning(ex, $"Failed to read the schedule state; treating it as a fresh start");
            return null;
        }
    }

    private void SaveLastFullRun()
    {
        try
        {
            var path = StateFilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            var json = JsonSerializer.Serialize(
                new ScheduleState { LastFullRun = _lastFullRun },
                new JsonSerializerOptions { WriteIndented = true }
            );

            var tempPath = path + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, path, overwrite: true);
        }
        catch (Exception ex)
        {
            // Losing this only costs one extra full run after a restart.
            Log.ZLogWarning(ex, $"Failed to save the schedule state");
        }
    }
}
