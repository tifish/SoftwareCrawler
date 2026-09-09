using SoftwareCrawler.Services;

namespace SoftwareCrawler.Tests;

/// <summary>
/// The upgrade that moved this machine's data out of the roaming folder. It runs
/// on every start, so "already migrated" and "nothing to migrate" have to be as
/// safe as the move itself - LocalSettings.tab has no second copy.
/// </summary>
public class MachineDataMigrationTests
{
    private static (DirectoryInfo Source, DirectoryInfo Destination) CreateRoots()
    {
        var root = Directory.CreateTempSubdirectory("SoftwareCrawlerMigration");
        return (
            root.CreateSubdirectory("RoamingConfig"),
            root.CreateSubdirectory("MachineConfig")
        );
    }

    [Fact]
    public void MachineLocalFilesMoveOutOfTheRoamingFolder()
    {
        var (source, destination) = CreateRoots();
        try
        {
            File.WriteAllText(Path.Join(source.FullName, "LocalSettings.tab"), "mine");
            File.WriteAllText(Path.Join(source.FullName, "ScheduleState.json"), "{}");
            // Shared, and staying put.
            File.WriteAllText(Path.Join(source.FullName, "Software.tab"), "recipes");

            var moved = MachineDataMigration.Run(source.FullName, destination.FullName, _ => { });

            Assert.Equal(["LocalSettings.tab", "ScheduleState.json"], moved);
            Assert.Equal("mine", File.ReadAllText(Path.Join(destination.FullName, "LocalSettings.tab")));
            Assert.False(File.Exists(Path.Join(source.FullName, "LocalSettings.tab")));
            Assert.True(File.Exists(Path.Join(source.FullName, "Software.tab")));
            Assert.False(File.Exists(Path.Join(destination.FullName, "Software.tab")));
        }
        finally
        {
            source.Parent!.Delete(true);
        }
    }

    /// <summary>
    /// The machine-local copy is the one in use, so a leftover in the old folder
    /// must never overwrite it - not on this start, and not on any later one.
    /// </summary>
    [Fact]
    public void AnExistingMachineLocalFileWins()
    {
        var (source, destination) = CreateRoots();
        try
        {
            File.WriteAllText(Path.Join(source.FullName, "LocalSettings.tab"), "stale");
            File.WriteAllText(Path.Join(destination.FullName, "LocalSettings.tab"), "current");

            var moved = MachineDataMigration.Run(source.FullName, destination.FullName, _ => { });

            Assert.Empty(moved);
            Assert.Equal(
                "current",
                File.ReadAllText(Path.Join(destination.FullName, "LocalSettings.tab"))
            );
            Assert.Equal("stale", File.ReadAllText(Path.Join(source.FullName, "LocalSettings.tab")));
        }
        finally
        {
            source.Parent!.Delete(true);
        }
    }

    /// <summary>
    /// Both roots resolve to the same folder when the machine-local location is
    /// also the active one; moving a file onto itself would delete it.
    /// </summary>
    [Fact]
    public void TheSameFolderTwiceIsLeftAlone()
    {
        var (source, _) = CreateRoots();
        try
        {
            var path = Path.Join(source.FullName, "DownloadHistory.tab");
            File.WriteAllText(path, "history");

            Assert.Empty(MachineDataMigration.Run(source.FullName, source.FullName, _ => { }));
            Assert.Equal("history", File.ReadAllText(path));
        }
        finally
        {
            source.Parent!.Delete(true);
        }
    }

    [Fact]
    public void NothingToMigrateIsNotAnError()
    {
        var (source, destination) = CreateRoots();
        try
        {
            Assert.Empty(MachineDataMigration.Run(source.FullName, destination.FullName, _ => { }));
        }
        finally
        {
            source.Parent!.Delete(true);
        }
    }
}
