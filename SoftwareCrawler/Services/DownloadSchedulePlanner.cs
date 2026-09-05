namespace SoftwareCrawler.Services;

/// <summary>Which of the two schedules asked for a run.</summary>
public enum ScheduledRunKind
{
    /// <summary>The short-interval sweep over items marked FrequentCheck.</summary>
    Frequent,

    /// <summary>Every enabled item, at one of the configured times of day.</summary>
    Full,
}

/// <summary>
/// Decides whether a run is due. Pure date arithmetic — no timer, no settings, no
/// UI — because "is it time yet" is the part that is easy to get wrong across
/// midnight and after the machine was off, and the only part worth unit tests.
/// </summary>
public static class DownloadSchedulePlanner
{
    /// <summary>
    /// Parses the stored "HH:mm" strings, dropping anything unparseable. Settings
    /// normalization already does this; parsing again here keeps the planner usable
    /// on raw input.
    /// </summary>
    public static List<TimeOnly> ParseTimes(IEnumerable<string>? times)
    {
        if (times is null)
            return [];

        var parsed = new List<TimeOnly>();
        foreach (var time in times)
        {
            if (TimeOnly.TryParseExact(time?.Trim(), "HH:mm", out var value))
                parsed.Add(value);
        }

        parsed.Sort();
        return parsed;
    }

    /// <summary>
    /// The most recent scheduled moment that has already passed, or null when
    /// nothing is scheduled. Yesterday is searched too, so a run configured for
    /// 18:30 is still recognised as missed when the machine wakes up at 00:30.
    /// </summary>
    public static DateTime? MostRecentDue(DateTime now, IReadOnlyList<TimeOnly> times)
    {
        if (times.Count == 0)
            return null;

        DateTime? latest = null;
        foreach (var day in (ReadOnlySpan<DateTime>)[now.Date, now.Date.AddDays(-1)])
        {
            foreach (var time in times)
            {
                var moment = day + time.ToTimeSpan();
                if (moment <= now && (latest is null || moment > latest))
                    latest = moment;
            }
        }

        return latest;
    }

    /// <summary>
    /// The next scheduled moment strictly after <paramref name="now"/>, for display.
    /// </summary>
    public static DateTime? NextDue(DateTime now, IReadOnlyList<TimeOnly> times)
    {
        if (times.Count == 0)
            return null;

        DateTime? earliest = null;
        foreach (var day in (ReadOnlySpan<DateTime>)[now.Date, now.Date.AddDays(1)])
        {
            foreach (var time in times)
            {
                var moment = day + time.ToTimeSpan();
                if (moment > now && (earliest is null || moment < earliest))
                    earliest = moment;
            }
        }

        return earliest;
    }

    /// <summary>
    /// True when a scheduled moment has passed that the last full run predates. A
    /// machine that was off through several of them still runs once, not once per
    /// missed slot — the point is to be up to date, not to replay the calendar.
    /// </summary>
    public static bool IsFullRunDue(
        DateTime now,
        DateTime? lastFullRun,
        IReadOnlyList<TimeOnly> times
    )
    {
        var due = MostRecentDue(now, times);
        if (due is null)
            return false;

        return lastFullRun is null || lastFullRun < due;
    }

    /// <summary>
    /// Which schedules are due this tick, judged independently.
    ///
    /// Both can come back true, and the caller must treat them separately: a full
    /// run that is due but cannot start has to leave the frequent sweep alone.
    /// Bundling them once meant a blocked full run silently held the sweep back for
    /// hours.
    /// </summary>
    public static (bool Full, bool Frequent) GetDueRuns(
        DateTime now,
        DateTime? lastFullRun,
        IReadOnlyList<TimeOnly> times,
        DateTime? lastFrequentRun,
        int frequentIntervalMinutes
    ) =>
        (
            IsFullRunDue(now, lastFullRun, times),
            IsFrequentRunDue(now, lastFrequentRun, frequentIntervalMinutes)
        );

    /// <summary>True once <paramref name="intervalMinutes"/> has elapsed since the last sweep.</summary>
    public static bool IsFrequentRunDue(
        DateTime now,
        DateTime? lastFrequentRun,
        int intervalMinutes
    )
    {
        if (intervalMinutes <= 0)
            return false;

        return lastFrequentRun is null
            || now - lastFrequentRun.Value >= TimeSpan.FromMinutes(intervalMinutes);
    }
}
