using SoftwareCrawler.Services;

namespace SoftwareCrawler.Tests;

/// <summary>
/// "Is a run due yet" is the part of the resident scheduler that is easy to get
/// wrong: it has to survive midnight, a machine that was switched off through
/// several slots, and an empty schedule.
/// </summary>
public class DownloadScheduleTests
{
    private static List<TimeOnly> Times(params string[] times) =>
        DownloadSchedulePlanner.ParseTimes(times);

    private static DateTime At(string moment) => DateTime.Parse(moment);

    [Fact]
    public void ParseTimesDropsWhatItCannotRead()
    {
        var parsed = DownloadSchedulePlanner.ParseTimes(
            ["08:00", "not a time", "", "13:00", "25:00", "8:00 PM"]
        );

        Assert.Equal([new TimeOnly(8, 0), new TimeOnly(13, 0)], parsed);
    }

    [Fact]
    public void ParseTimesSortsSoTheOrderInTheFileDoesNotMatter()
    {
        var parsed = DownloadSchedulePlanner.ParseTimes(["18:30", "00:00", "13:00"]);

        Assert.Equal([new TimeOnly(0, 0), new TimeOnly(13, 0), new TimeOnly(18, 30)], parsed);
    }

    [Fact]
    public void MostRecentDuePicksTheLatestSlotAlreadyPassedToday()
    {
        var due = DownloadSchedulePlanner.MostRecentDue(
            At("2026-09-05 14:20"),
            Times("00:00", "08:00", "13:00", "18:30")
        );

        Assert.Equal(At("2026-09-05 13:00"), due);
    }

    /// <summary>
    /// Just after midnight the only slots behind us are yesterday's, so the search
    /// has to look back a day or the first hours of every day would never run.
    /// </summary>
    [Fact]
    public void MostRecentDueReachesBackToYesterday()
    {
        var due = DownloadSchedulePlanner.MostRecentDue(
            At("2026-09-05 00:30"),
            Times("08:00", "13:00", "18:30")
        );

        Assert.Equal(At("2026-09-04 18:30"), due);
    }

    [Fact]
    public void NothingIsDueWithoutASchedule()
    {
        Assert.Null(DownloadSchedulePlanner.MostRecentDue(At("2026-09-05 14:20"), Times()));
        Assert.False(DownloadSchedulePlanner.IsFullRunDue(At("2026-09-05 14:20"), null, Times()));
    }

    [Fact]
    public void AFullRunIsDueWhenASlotPassedSinceTheLastOne()
    {
        Assert.True(
            DownloadSchedulePlanner.IsFullRunDue(
                At("2026-09-05 13:00"),
                At("2026-09-05 08:00"),
                Times("08:00", "13:00")
            )
        );
    }

    [Fact]
    public void AFullRunIsNotDueTwiceForTheSameSlot()
    {
        Assert.False(
            DownloadSchedulePlanner.IsFullRunDue(
                At("2026-09-05 12:59"),
                At("2026-09-05 08:00"),
                Times("08:00", "13:00")
            )
        );
    }

    /// <summary>
    /// A machine off from Friday evening to Monday morning missed several slots.
    /// It should catch up once — the point is being current, not replaying the
    /// calendar — which falls out of comparing against the most recent slot only.
    /// </summary>
    [Fact]
    public void MissedSlotsCollapseIntoASingleCatchUpRun()
    {
        var times = Times("00:00", "08:00", "13:00", "18:30");

        Assert.True(
            DownloadSchedulePlanner.IsFullRunDue(
                At("2026-09-07 09:00"),
                At("2026-09-04 18:30"),
                times
            )
        );

        // ...and once that catch-up has run, nothing more is owed until 13:00.
        Assert.False(
            DownloadSchedulePlanner.IsFullRunDue(
                At("2026-09-07 09:01"),
                At("2026-09-07 09:00"),
                times
            )
        );
    }

    [Fact]
    public void NextDueLooksForwardAcrossMidnight()
    {
        Assert.Equal(
            At("2026-09-05 13:00"),
            DownloadSchedulePlanner.NextDue(At("2026-09-05 08:30"), Times("08:00", "13:00"))
        );

        Assert.Equal(
            At("2026-09-06 08:00"),
            DownloadSchedulePlanner.NextDue(At("2026-09-05 19:00"), Times("08:00", "13:00"))
        );
    }

    [Fact]
    public void TheFrequentSweepWaitsOutItsInterval()
    {
        Assert.False(
            DownloadSchedulePlanner.IsFrequentRunDue(
                At("2026-09-05 14:09"),
                At("2026-09-05 14:00"),
                10
            )
        );

        Assert.True(
            DownloadSchedulePlanner.IsFrequentRunDue(
                At("2026-09-05 14:10"),
                At("2026-09-05 14:00"),
                10
            )
        );
    }

    [Fact]
    public void AZeroIntervalTurnsTheFrequentSweepOff()
    {
        Assert.False(DownloadSchedulePlanner.IsFrequentRunDue(At("2026-09-05 14:10"), null, 0));
    }
}
