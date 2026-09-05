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

    /// <summary>
    /// Writing out a list of times invites spaces, commas and semicolons in equal
    /// measure, so all of them are accepted and none of them survives into what the
    /// app keeps.
    /// </summary>
    [Theory]
    [InlineData("00:00 08:00 13:00 18:30")]
    [InlineData("00:00,08:00,13:00,18:30")]
    [InlineData("00:00, 08:00, 13:00, 18:30")]
    [InlineData("00:00;08:00;13:00;18:30")]
    [InlineData("00:00; 08:00 , 13:00;18:30")]
    [InlineData("  00:00   08:00\t13:00,,18:30  ")]
    public void AnySeparatorMixReadsAsTheSameSchedule(string text)
    {
        Assert.True(DownloadSchedulePlanner.TryParseTimeList(text, out var times, out _));

        Assert.Equal(["00:00", "08:00", "13:00", "18:30"], times);
        Assert.Equal("00:00 08:00 13:00 18:30", DownloadSchedulePlanner.FormatTimeList(times));
    }

    [Fact]
    public void ATypedListIsSortedAndDeduplicated()
    {
        Assert.True(
            DownloadSchedulePlanner.TryParseTimeList("18:30 08:00 18:30", out var times, out _)
        );

        Assert.Equal(["08:00", "18:30"], times);
    }

    /// <summary>A missing leading zero is a typo worth accepting, not worth rejecting.</summary>
    [Fact]
    public void ASingleDigitHourIsAcceptedAndPaddedOut()
    {
        Assert.True(DownloadSchedulePlanner.TryParseTimeList("9:00 8:05", out var times, out _));

        Assert.Equal(["08:05", "09:00"], times);
    }

    /// <summary>
    /// Anything unreadable is reported rather than dropped: a typo that silently
    /// becomes "no run at that time" is the failure nobody would notice.
    /// </summary>
    [Theory]
    [InlineData("08:00 25:00", "25:00")]
    [InlineData("08:00 tea time", "tea")]
    [InlineData("08:00 8pm", "8pm")]
    public void AnUnreadableEntryIsReportedNotDropped(string text, string expectedBadPart)
    {
        Assert.False(DownloadSchedulePlanner.TryParseTimeList(text, out _, out var badPart));

        Assert.Equal(expectedBadPart, badPart);
    }

    [Fact]
    public void AnEmptyBoxMeansNoScheduledRun()
    {
        Assert.True(DownloadSchedulePlanner.TryParseTimeList("   ", out var times, out _));

        Assert.Empty(times);
        Assert.Equal("", DownloadSchedulePlanner.FormatTimeList(times));
    }

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

    /// <summary>
    /// The two schedules are judged independently. Deciding them together once let
    /// a full run that was due but blocked swallow the tick, so the frequent sweep
    /// went hours without running while the user had the main window open.
    /// </summary>
    [Fact]
    public void BothSchedulesCanBeDueOnTheSameTick()
    {
        var due = DownloadSchedulePlanner.GetDueRuns(
            At("2026-09-05 13:00"),
            At("2026-09-05 08:00"),
            Times("08:00", "13:00"),
            At("2026-09-05 12:45"),
            10
        );

        Assert.True(due.Full);
        Assert.True(due.Frequent);
    }

    [Fact]
    public void ADueFullRunDoesNotMakeTheFrequentSweepLookDue()
    {
        var due = DownloadSchedulePlanner.GetDueRuns(
            At("2026-09-05 13:00"),
            At("2026-09-05 08:00"),
            Times("08:00", "13:00"),
            At("2026-09-05 12:59"),
            10
        );

        Assert.True(due.Full);
        Assert.False(due.Frequent);
    }

    [Fact]
    public void AZeroIntervalTurnsTheFrequentSweepOff()
    {
        Assert.False(DownloadSchedulePlanner.IsFrequentRunDue(At("2026-09-05 14:10"), null, 0));
    }
}
