using System.Text;
using SoftwareCrawler.Models;

namespace SoftwareCrawler.Services;

/// <summary>One item an automatic round could not download, as the agent is told about it.</summary>
public sealed record AutoFixFailure(
    string Name,
    string WebPage,
    string Status,
    string ErrorMessage,
    DateTime? LastChecked
);

/// <summary>Everything the agent's brief is written from.</summary>
public sealed record AutoFixContext(
    string Trigger,
    DateTime FinishedAt,
    IReadOnlyList<AutoFixFailure> Failures,
    string RepositoryRoot,
    string ClientExecutable,
    int ClientProcessId,
    bool ClientIsDebugBuild,
    string SoftwareListPath,
    string LogPath,
    string RestartCommand
);

/// <summary>
/// The decisions behind <see cref="AutoFixService"/> that need no process and no
/// window: which failures are worth an agent's time, how each CLI is started, and
/// what it is told. Kept pure so the tests can pin them down.
/// </summary>
public static class AutoFixPlanner
{
    /// <summary>
    /// How long an item handed to an agent stays off the list while it keeps failing.
    /// The frequent sweep comes round every few minutes; without this, a site the
    /// agent could not fix would start a fresh session every time it failed again.
    /// A success clears the item early, so a new breakage is reported at once.
    /// </summary>
    public static readonly TimeSpan HandOffCooldown = TimeSpan.FromHours(24);

    /// <summary>
    /// The failures not already given to an agent within the cooldown, in the order
    /// they failed.
    /// </summary>
    public static List<string> SelectForHandOff(
        IEnumerable<string> failedNames,
        IReadOnlyDictionary<string, DateTime> handedOff,
        DateTime now
    ) =>
        failedNames
            .Distinct(StringComparer.Ordinal)
            .Where(name =>
                !handedOff.TryGetValue(name, out var at) || now - at >= HandOffCooldown
            )
            .ToList();

    /// <summary>
    /// The command line, for cmd.exe, that runs the agent unattended on the brief in
    /// <paramref name="promptPath"/>. Each one runs with every permission granted:
    /// nobody is there to approve a step, and the job ends in a push.
    /// </summary>
    public static string BuildAgentCommand(
        AutoFixAgent agent,
        string promptPath,
        string repositoryRoot
    )
    {
        var mcpAdapter = Path.Join(repositoryRoot, "bin", "SoftwareCrawlerMcp.exe");

        return agent switch
        {
            // The project's .mcp.json is passed explicitly: a print-mode session does
            // not stop to ask whether a project server may be trusted.
            AutoFixAgent.ClaudeCode =>
                $"claude -p --dangerously-skip-permissions --mcp-config \"{Path.Join(repositoryRoot, ".mcp.json")}\" < \"{promptPath}\"",
            // Codex reads only its own config.toml, so the Debug MCP adapter is
            // registered for this session on the command line. '-' reads the brief
            // from stdin.
            AutoFixAgent.Codex =>
                $"codex exec --dangerously-bypass-approvals-and-sandbox -C \"{repositoryRoot}\" "
                    + $"-c \"mcp_servers.sc-debug.command='{mcpAdapter}'\" - < \"{promptPath}\"",
            // Grok Build picks up the project's .mcp.json on its own.
            AutoFixAgent.GrokBuild =>
                $"grok --always-approve --cwd \"{repositoryRoot}\" --prompt-file \"{promptPath}\"",
            _ => throw new ArgumentOutOfRangeException(nameof(agent), agent, "No agent selected"),
        };
    }

    public static string DisplayName(AutoFixAgent agent) =>
        agent switch
        {
            AutoFixAgent.ClaudeCode => "Claude Code",
            AutoFixAgent.Codex => "Codex CLI",
            AutoFixAgent.GrokBuild => "Grok Build",
            _ => "None",
        };

    /// <summary>
    /// The brief. The agent starts cold and nobody answers its questions, so this
    /// carries what it cannot find out alone — which round, which items, which copy
    /// of the client — and leaves how to debug to AGENTS.md, which it reads anyway.
    /// </summary>
    public static string BuildPrompt(AutoFixContext context)
    {
        var sb = new StringBuilder();

        sb.AppendLine(
            "You were launched by SoftwareCrawler because an automatic download round had failures. "
                + "You run unattended: nobody will answer questions or approve anything, so make your "
                + "own decisions and finish on your own."
        );
        sb.AppendLine();

        sb.AppendLine("## What failed");
        sb.AppendLine();
        sb.AppendLine(
            $"Round: {context.Trigger}, finished {context.FinishedAt:yyyy-MM-dd HH:mm}. "
                + $"{context.Failures.Count} item(s) failed:"
        );
        sb.AppendLine();
        foreach (var failure in context.Failures)
        {
            sb.AppendLine($"- **{failure.Name}**");
            sb.AppendLine($"  - Web page: {failure.WebPage}");
            sb.AppendLine($"  - Status: {failure.Status}");
            sb.AppendLine(
                $"  - Error: {(string.IsNullOrWhiteSpace(failure.ErrorMessage) ? "(none recorded)" : failure.ErrorMessage)}"
            );
            sb.AppendLine(
                $"  - Last successful check: {failure.LastChecked?.ToString("yyyy-MM-dd HH:mm") ?? "never recorded"}"
            );
        }
        sb.AppendLine();

        sb.AppendLine("## The client that ran it");
        sb.AppendLine();
        sb.AppendLine($"- Repository: {context.RepositoryRoot}");
        sb.AppendLine(
            $"- Executable: {context.ClientExecutable} "
                + $"({(context.ClientIsDebugBuild ? "Debug" : "Release")} build, pid {context.ClientProcessId}, "
                + "resident in the notification area)"
        );
        sb.AppendLine($"- Software list it reads: {context.SoftwareListPath}");
        sb.AppendLine($"- Its log: {context.LogPath} (search it for the item names)");
        sb.AppendLine(
            context.ClientIsDebugBuild
                ? "- Debug MCP: available as the `sc-debug` server (bin\\SoftwareCrawlerMcp.exe), connected to that client."
                : "- Debug MCP: this client is a Release build and does not listen; build and run the Debug "
                    + "copy in the repository to verify."
        );
        sb.AppendLine();

        sb.AppendLine("## What to do");
        sb.AppendLine();
        sb.AppendLine(
            "1. Read AGENTS.md first and follow its rules; docs/Requirements.md and docs/Architecture.md "
                + "explain the recipe fields and the download pipeline."
        );
        sb.AppendLine(
            "2. Find the cause of each failure from the log and the Debug MCP (`download_probe` checks an "
                + "item without keeping the file, `page_state` tells a slow page from a wrong XPath). A failure "
                + "is not necessarily a broken recipe: rule out a site that is down, the network, the proxy and "
                + "an unreachable download directory first, and do not change a recipe for a transient failure."
        );
        sb.AppendLine(
            "3. Fix what is broken: usually the item's recipe in the software list, sometimes the code. Verify "
                + "every fix with `download_probe` until it reports HasUpdate or SameFileAlreadyDownloaded. "
                + "Whenever you rebuild and relaunch the client to test, use `Run.cmd Debug --tray`, so no window "
                + "appears on the user's desktop."
        );
        sb.AppendLine("4. If you changed code, run the tests (stop the client first, as AGENTS.md says).");
        sb.AppendLine(
            "5. The working tree may hold the user's own uncommitted work. Never revert, stash or commit it: "
                + "stage only your own changes, and if a file you fixed also has their edits, stage only your "
                + "hunks. Commit with a brief English message as AGENTS.md says, then `git pull --rebase` and "
                + "`git push`. If nothing needed fixing, commit nothing."
        );
        sb.AppendLine(
            "6. Last, always — even if you changed nothing — restart the client so the latest build is the one "
                + "running, detached from your session:"
        );
        sb.AppendLine();
        sb.AppendLine($"   {context.RestartCommand}");
        sb.AppendLine();
        sb.AppendLine(
            "7. Finish with a short summary: for each item, the cause, what you changed, and whether the fix "
                + "is verified."
        );

        return sb.ToString();
    }
}
