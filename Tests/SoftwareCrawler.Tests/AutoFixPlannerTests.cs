using SoftwareCrawler.Models;
using SoftwareCrawler.Services;

namespace SoftwareCrawler.Tests;

/// <summary>
/// The agent that fixes failed rounds runs with every permission and ends in a push,
/// so what decides whether it starts, how it is started and what it is told is
/// pinned down here rather than discovered on a live session.
/// </summary>
public class AutoFixPlannerTests
{
    private static readonly DateTime Now = new(2026, 10, 14, 8, 0, 0);

    [Fact]
    public void AFailureNeverHandedOffIsHandedOff()
    {
        var selected = AutoFixPlanner.SelectForHandOff(["WPS", "TIM"], new Dictionary<string, DateTime>(), Now);

        Assert.Equal(["WPS", "TIM"], selected);
    }

    /// <summary>
    /// The frequent sweep comes round every ten minutes. A site the agent could not
    /// fix must not start a fresh session every time it fails again.
    /// </summary>
    [Fact]
    public void AFailureHandedOffWithinTheCooldownIsNotHandedOffAgain()
    {
        var handedOff = new Dictionary<string, DateTime> { ["WPS"] = Now.AddMinutes(-10) };

        var selected = AutoFixPlanner.SelectForHandOff(["WPS", "TIM"], handedOff, Now);

        Assert.Equal(["TIM"], selected);
    }

    [Fact]
    public void AFailureIsHandedOffAgainOnceTheCooldownHasPassed()
    {
        var handedOff = new Dictionary<string, DateTime>
        {
            ["WPS"] = Now - AutoFixPlanner.HandOffCooldown,
        };

        Assert.Equal(["WPS"], AutoFixPlanner.SelectForHandOff(["WPS"], handedOff, Now));
    }

    [Fact]
    public void AnItemListedTwiceIsHandedOffOnce()
    {
        var selected = AutoFixPlanner.SelectForHandOff(["WPS", "WPS"], new Dictionary<string, DateTime>(), Now);

        Assert.Equal(["WPS"], selected);
    }

    /// <summary>
    /// Nobody is there to approve a step, so each CLI runs non-interactively with its
    /// approvals bypassed, reads the brief from the file, and can reach the Debug MCP.
    /// </summary>
    [Theory]
    [InlineData(AutoFixAgent.ClaudeCode, "claude -p --dangerously-skip-permissions", @"--mcp-config ""D:\Repo\.mcp.json""", @"< ""D:\Logs\p.md""")]
    [InlineData(AutoFixAgent.Codex, "codex exec --dangerously-bypass-approvals-and-sandbox", @"mcp_servers.sc-debug.command='D:\Repo\bin\SoftwareCrawlerMcp.exe'", @"- < ""D:\Logs\p.md""")]
    [InlineData(AutoFixAgent.GrokBuild, "grok --always-approve", @"--cwd ""D:\Repo""", @"--prompt-file ""D:\Logs\p.md""")]
    public void EachAgentRunsUnattendedOnTheBrief(
        AutoFixAgent agent,
        string start,
        string mcpOrFolder,
        string prompt
    )
    {
        var command = AutoFixPlanner.BuildAgentCommand(agent, @"D:\Logs\p.md", @"D:\Repo");

        Assert.StartsWith(start, command);
        Assert.Contains(mcpOrFolder, command);
        Assert.Contains(prompt, command);
    }

    [Fact]
    public void NoAgentHasNoCommand()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AutoFixPlanner.BuildAgentCommand(AutoFixAgent.None, "p.md", "repo")
        );
    }

    /// <summary>
    /// The agent starts cold: the brief has to name every failure with its error, the
    /// client it came from, and the exact command that restarts that client.
    /// </summary>
    [Fact]
    public void TheBriefCarriesTheFailuresTheClientAndTheRestartCommand()
    {
        var context = new AutoFixContext(
            "scheduled full run",
            Now,
            [
                new AutoFixFailure("WPS", "https://www.wps.cn/", "Failed", "Timeout waiting for download", Now.AddDays(-3)),
                new AutoFixFailure("TIM", "https://office.qq.com/", "Failed", "", null),
            ],
            @"D:\Repo",
            @"D:\Repo\bin\SoftwareCrawler.exe",
            1234,
            true,
            @"D:\Repo\bin\Templates\Software.tab",
            @"D:\Repo\bin\Logs\SoftwareCrawler.log",
            @"powershell -File ""D:\Repo\bin\RestartClient.ps1"" -Configuration Debug"
        );

        var prompt = AutoFixPlanner.BuildPrompt(context);

        Assert.Contains("**WPS**", prompt);
        Assert.Contains("Timeout waiting for download", prompt);
        Assert.Contains("**TIM**", prompt);
        Assert.Contains("(none recorded)", prompt);
        Assert.Contains("never recorded", prompt);
        Assert.Contains("pid 1234", prompt);
        Assert.Contains(@"D:\Repo\bin\Templates\Software.tab", prompt);
        Assert.Contains(context.RestartCommand, prompt);
        Assert.Contains("git push", prompt);
        Assert.Contains("--tray", prompt);
    }

    [Fact]
    public void ABriefForAReleaseClientSaysTheDebugMcpIsNotThere()
    {
        var context = new AutoFixContext(
            "frequent check",
            Now,
            [new AutoFixFailure("WPS", "https://www.wps.cn/", "Failed", "x", null)],
            @"D:\Repo",
            @"C:\Programs\SoftwareCrawler\SoftwareCrawler.exe",
            1,
            false,
            @"C:\Programs\SoftwareCrawler\Config\Software.tab",
            @"C:\Programs\SoftwareCrawler\Logs\SoftwareCrawler.log",
            "restart"
        );

        Assert.Contains("Release build and does not listen", AutoFixPlanner.BuildPrompt(context));
    }
}
