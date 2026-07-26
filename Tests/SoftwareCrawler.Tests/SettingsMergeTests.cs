using JeekTools;

namespace SoftwareCrawler.Tests;

/// <summary>
/// Settings files are written by whichever instances happen to be running, so a
/// save must keep what the others changed. <see cref="JsonSettingsFile"/> does
/// that with a three-way merge against the state the saver last read.
/// </summary>
public class SettingsMergeTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(),
        "SoftwareCrawler.Tests",
        Guid.NewGuid().ToString("N")
    );

    private string SettingsPath => Path.Combine(_folder, "settings.json");

    public SettingsMergeTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch
        {
            // A leftover temp folder is not worth failing a test over.
        }
        GC.SuppressFinalize(this);
    }

    private sealed class Sample
    {
        public string Mine { get; set; } = "";
        public string Theirs { get; set; } = "";
        public int Number { get; set; }
    }

    private static void NoNormalization(Sample sample) { }

    private void WriteDisk(Sample sample) =>
        File.WriteAllText(SettingsPath, JsonSettingsFile.Serialize(sample));

    private Sample ReadDisk() =>
        JsonSettingsFile.TryLoad(SettingsPath, out Sample sample) ? sample : new Sample();

    [Fact]
    public void AChangeFromAnotherInstanceSurvivesOurSave()
    {
        var baseline = new Sample { Mine = "original", Theirs = "original" };
        WriteDisk(baseline);

        // Another instance saved while we were holding the baseline.
        WriteDisk(new Sample { Mine = "original", Theirs = "changed elsewhere" });

        var local = new Sample { Mine = "changed here", Theirs = "original" };
        var written = JsonSettingsFile.TryMergeAndWrite(
            SettingsPath,
            baseline,
            local,
            NoNormalization,
            forceAllLocal: false,
            out var merged
        );

        Assert.True(written);
        Assert.Equal("changed here", merged.Mine);
        Assert.Equal("changed elsewhere", merged.Theirs);
        Assert.Equal("changed elsewhere", ReadDisk().Theirs);
    }

    [Fact]
    public void OurChangeWinsOnTheSameProperty()
    {
        var baseline = new Sample { Mine = "original" };
        WriteDisk(new Sample { Mine = "changed elsewhere" });

        JsonSettingsFile.TryMergeAndWrite(
            SettingsPath,
            baseline,
            new Sample { Mine = "changed here" },
            NoNormalization,
            forceAllLocal: false,
            out var merged
        );

        Assert.Equal("changed here", merged.Mine);
    }

    [Fact]
    public void ForceAllLocalIgnoresWhateverIsOnDisk()
    {
        var baseline = new Sample { Mine = "original", Theirs = "original" };
        WriteDisk(new Sample { Mine = "original", Theirs = "changed elsewhere" });

        JsonSettingsFile.TryMergeAndWrite(
            SettingsPath,
            baseline,
            new Sample { Mine = "changed here", Theirs = "original" },
            NoNormalization,
            forceAllLocal: true,
            out var merged
        );

        Assert.Equal("changed here", merged.Mine);
        Assert.Equal("original", merged.Theirs);
    }

    [Fact]
    public void NormalizationIsAppliedToWhatGetsWritten()
    {
        WriteDisk(new Sample());

        JsonSettingsFile.TryMergeAndWrite(
            SettingsPath,
            new Sample(),
            new Sample { Number = 9999 },
            sample => sample.Number = Math.Clamp(sample.Number, 0, 100),
            forceAllLocal: false,
            out var merged
        );

        Assert.Equal(100, merged.Number);
        Assert.Equal(100, ReadDisk().Number);
    }

    [Fact]
    public void AMissingFileIsCreatedFromTheLocalValues()
    {
        var written = JsonSettingsFile.TryMergeAndWrite(
            SettingsPath,
            new Sample(),
            new Sample { Mine = "first save" },
            NoNormalization,
            forceAllLocal: false,
            out var merged
        );

        Assert.True(written);
        Assert.True(File.Exists(SettingsPath));
        Assert.Equal("first save", merged.Mine);
        Assert.Equal("first save", ReadDisk().Mine);
    }

    /// <summary>A half-written or hand-mangled file must not take the app down.</summary>
    [Fact]
    public void UnreadableJsonIsReplacedRatherThanThrowing()
    {
        File.WriteAllText(SettingsPath, "{ this is not json");

        var written = JsonSettingsFile.TryMergeAndWrite(
            SettingsPath,
            new Sample(),
            new Sample { Mine = "recovered" },
            NoNormalization,
            forceAllLocal: false,
            out var merged
        );

        Assert.True(written);
        Assert.Equal("recovered", merged.Mine);
        Assert.Equal("recovered", ReadDisk().Mine);
    }
}
