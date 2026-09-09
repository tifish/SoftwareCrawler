using SoftwareCrawler;

namespace SoftwareCrawler.Tests;

/// <summary>
/// The history file is regenerable, but it is still a .tab file the user may
/// open: it has to round-trip, tolerate a row written by an older build, and
/// survive a name carrying a tab.
/// </summary>
public class DownloadHistoryStoreTests
{
    [Fact]
    public void EntriesRoundTrip()
    {
        var entries = new Dictionary<string, DownloadHistoryStore.Entry>(
            StringComparer.OrdinalIgnoreCase
        )
        {
            ["Zed"] = new(new DateTime(2026, 9, 8, 21, 30, 15), null),
            ["NaiveProxy"] = new(
                new DateTime(2026, 9, 9, 3, 0, 0),
                new DateTime(2026, 7, 1, 12, 34, 56)
            ),
        };

        var lines = DownloadHistoryStore.BuildLines(entries);
        var restored = DownloadHistoryStore.ParseLines(lines);

        Assert.Equal(entries["Zed"], restored["Zed"]);
        Assert.Equal(entries["NaiveProxy"], restored["naiveproxy"]);
        // Sorted by name, under the header, so a diff of the file stays readable.
        Assert.Equal("Name\tLastChecked\tLastDownloadedFileTime", lines[0]);
        Assert.Equal(["NaiveProxy", "Zed"], lines.Skip(1).Select(line => line.Split('\t')[0]));
    }

    [Fact]
    public void ANameCarryingATabSurvives()
    {
        var name = "Odd\tName";
        var lines = DownloadHistoryStore.BuildLines(
            new Dictionary<string, DownloadHistoryStore.Entry>(StringComparer.OrdinalIgnoreCase)
            {
                [name] = new(new DateTime(2026, 9, 9, 8, 0, 0), null),
            }
        );

        Assert.Equal(3, lines[1].Split('\t').Length);
        Assert.True(DownloadHistoryStore.ParseLines(lines).ContainsKey(name));
    }

    /// <summary>
    /// A row written before a column existed keeps what it does say, exactly like
    /// a short row of the software list.
    /// </summary>
    [Fact]
    public void ShortAndUnreadableRowsKeepWhatTheyCan()
    {
        var parsed = DownloadHistoryStore.ParseLines(
            [
                "Name\tLastChecked\tLastDownloadedFileTime",
                "OnlyName",
                "OnlyChecked\t2026-09-09 08:00:00",
                "Garbage\tnot a date\talso not a date",
                "",
                "\t2026-09-09 08:00:00\t2026-09-09 08:00:00",
                "Complete\t2026-09-09 08:00:00\t2026-01-02 03:04:05",
            ]
        );

        Assert.Equal(new DownloadHistoryStore.Entry(null, null), parsed["OnlyName"]);
        Assert.Equal(new DateTime(2026, 9, 9, 8, 0, 0), parsed["OnlyChecked"].LastChecked);
        Assert.Null(parsed["OnlyChecked"].LastDownloadedFileTime);
        Assert.Equal(new DownloadHistoryStore.Entry(null, null), parsed["Garbage"]);
        Assert.Equal(new DateTime(2026, 1, 2, 3, 4, 5), parsed["Complete"].LastDownloadedFileTime);
        // The blank line and the nameless row are not entries.
        Assert.Equal(4, parsed.Count);
    }
}
