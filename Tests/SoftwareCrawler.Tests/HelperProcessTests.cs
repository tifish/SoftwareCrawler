using System.Diagnostics;
using SoftwareCrawler;

namespace SoftwareCrawler.Tests;

/// <summary>
/// Event scripts and 7-Zip run unattended; one that never exits must not hold the
/// batch forever, and must not leave its children running either.
/// </summary>
public class HelperProcessTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(),
        "SoftwareCrawler.Tests",
        Guid.NewGuid().ToString("N")
    );

    private readonly string _script;

    public HelperProcessTests()
    {
        Directory.CreateDirectory(_folder);
        _script = Path.Combine(_folder, "Hang.cmd");
        File.WriteAllText(_script, "@echo off\r\n:loop\r\nping -n 2 127.0.0.1 >nul\r\ngoto loop\r\n");
    }

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

    private Task<int> RunHangingScript(TimeSpan timeout, CancellationToken cancellationToken) =>
        DownloadPipeline.RunProcessAsync(
            "cmd.exe",
            $"/c \"\"{_script}\"\"",
            _folder,
            "hang test",
            timeout,
            cancellationToken
        );

    [Fact]
    public async Task AScriptThatNeverExitsIsKilledAtTheTimeout()
    {
        var watch = Stopwatch.StartNew();

        var exitCode = await RunHangingScript(TimeSpan.FromSeconds(2), CancellationToken.None);

        Assert.Equal(-1, exitCode);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task CancellingKillsTheScript()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var watch = Stopwatch.StartNew();

        var exitCode = await RunHangingScript(TimeSpan.FromMinutes(10), cts.Token);

        Assert.Equal(-1, exitCode);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task AScriptThatFinishesReportsItsExitCode()
    {
        var exitCode = await DownloadPipeline.RunProcessAsync(
            "cmd.exe",
            "/c exit 3",
            _folder,
            "exit test",
            TimeSpan.FromMinutes(1)
        );

        Assert.Equal(3, exitCode);
    }
}
