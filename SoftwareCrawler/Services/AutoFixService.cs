using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using JeekTools;
using Microsoft.Extensions.Logging;
using SoftwareCrawler.Models;
using ZLogger;

namespace SoftwareCrawler.Services;

/// <summary>What a request to start an agent came to.</summary>
public sealed record AutoFixOutcome(bool Launched, string Message);

/// <summary>
/// Hands the failures of an automatic round to a coding agent CLI (Claude Code,
/// Codex, Grok Build — <see cref="AppSettings.AutoFixAgent"/>), which diagnoses
/// them, fixes the recipe or the code, pushes, and restarts the client.
///
/// The agent outlives this process on purpose. Its test loop rebuilds the client,
/// which means killing the very instance that launched it, and its last step
/// restarts the client again. So it is started through cmd.exe with its output
/// going to a file rather than to a pipe this process would have to keep reading,
/// and what is running is remembered in <c>AutoFixState.json</c> — the instance
/// that comes back up has to know an agent is still at work.
///
/// While a session runs, scheduled rounds stand down (<see cref="IsBusy"/>): the
/// agent is driving the same browser through the Debug MCP and is about to restart
/// the client anyway.
/// </summary>
public static class AutoFixService
{
    private static readonly ILogger Log = LogManager.CreateLogger(nameof(AutoFixService));

    /// <summary>
    /// How long a session may hold the scheduler back. Fixing a handful of recipes,
    /// probing each and rebuilding fits well inside; an agent still going after this
    /// is stuck, and the schedule is worth more than whatever it is doing.
    /// </summary>
    public static readonly TimeSpan SessionTimeout = TimeSpan.FromHours(2);

    private static readonly object Sync = new();
    private static State? _state;

    /// <summary>Where briefs and transcripts go, beside the app's own log.</summary>
    public static string SessionFolder => Path.Join(AppContext.BaseDirectory, "Logs", "AutoFix");

    /// <summary>
    /// Machine-local, since it records what runs on this computer right now, and
    /// named after the instance, since every worktree's copy shares that folder and
    /// each has its own agent to keep track of. The client the agent restarts comes
    /// back from the same folder with the same id, so it finds the session again.
    /// Nothing to migrate: no older build kept it anywhere else.
    /// </summary>
    private static string StatePath =>
        Path.Join(SettingsService.MachineConfigRoot, $"AutoFixState.{DebugInstanceContext.InstanceId}.json");

    public sealed class Session
    {
        public int ProcessId { get; set; }
        public DateTime ProcessStartTime { get; set; }
        public DateTime LaunchedAt { get; set; }
        public AutoFixAgent Agent { get; set; }
        public string Trigger { get; set; } = "";
        public List<string> Items { get; set; } = [];
        public string PromptPath { get; set; } = "";
        public string LogPath { get; set; } = "";
    }

    private sealed class State
    {
        public Session? Session { get; set; }

        /// <summary>When each item was last given to an agent, for the cooldown.</summary>
        public Dictionary<string, DateTime> HandedOff { get; set; } = new(StringComparer.Ordinal);

        /// <summary>The session before the current one, kept so its transcript can be found.</summary>
        public Session? LastSession { get; set; }
    }

    /// <summary>
    /// Called when a scheduled round ends. Items that came through clear their
    /// cooldown; the failures not handed to an agent lately are handed to one now.
    /// </summary>
    public static AutoFixOutcome OnRoundFinished(
        ScheduledRunKind kind,
        IReadOnlyList<SoftwareItem> items,
        bool cancelled
    ) => OnRoundFinished(
        kind == ScheduledRunKind.Full ? "scheduled full run" : "frequent check", items, cancelled
    );

    /// <summary>Manual batches also hand off transfer timeouts, once the browser is free.</summary>
    public static AutoFixOutcome OnTimeoutsFinished(
        IReadOnlyList<SoftwareItem> items,
        bool cancelled,
        string? promptOverride = null
    ) => OnRoundFinished(
        "download timeout", items, cancelled, promptOverride, timeoutsOnly: true
    );

    private static AutoFixOutcome OnRoundFinished(
        string trigger,
        IReadOnlyList<SoftwareItem> items,
        bool cancelled,
        string? promptOverride = null,
        bool timeoutsOnly = false
    )
    {
        var enabled = items.Where(item => item.Enabled).ToList();

        lock (Sync)
        {
            var state = LoadState();
            var cleared = enabled
                .Where(item => IsConclusive(item.Status))
                .Count(item => state.HandedOff.Remove(item.Name));
            if (cleared > 0)
                SaveState(state);
        }

        // Someone stopped it: whatever is half-done says nothing about the recipes.
        if (cancelled)
            return new AutoFixOutcome(false, "The round was cancelled.");

        if (Settings.AutoFixAgent == AutoFixAgent.None)
            return new AutoFixOutcome(false, "Auto-fix is off.");

        var failed = enabled.Where(item =>
            item.Status == DownloadingStatus.Failed && (!timeoutsOnly || item.DownloadTimedOut)
        ).ToList();
        if (failed.Count == 0)
            return new AutoFixOutcome(false, "Nothing failed.");

        List<string> names;
        lock (Sync)
            names = AutoFixPlanner.SelectForHandOff(
                failed.Select(item => item.Name),
                LoadState().HandedOff,
                DateTime.Now
            );

        if (names.Count == 0)
        {
            var message =
                $"{failed.Count} item(s) failed, all handed to an agent within the last "
                + $"{AutoFixPlanner.HandOffCooldown.TotalHours:0} hours";
            Log.ZLogInformation($"{message}; not starting another session");
            return new AutoFixOutcome(false, message);
        }

        return Launch(
            trigger,
            failed.Where(item => names.Contains(item.Name)).Select(ToFailure).ToList(),
            Settings.AutoFixAgent,
            promptOverride
        );
    }

    public static AutoFixFailure ToFailure(SoftwareItem item) =>
        new(item.Name, item.WebPage, item.Status.ToString(), item.ErrorMessage, item.LastChecked);

    /// <summary>The outcomes that say the recipe still works.</summary>
    private static bool IsConclusive(DownloadingStatus status) =>
        status
            is DownloadingStatus.Downloaded
                or DownloadingStatus.SameFileAlreadyDownloaded
                or DownloadingStatus.HasUpdate;

    /// <summary>
    /// Starts an agent on <paramref name="failures"/>. <paramref name="promptOverride"/>
    /// replaces the brief, which lets the debug tools exercise the launch path
    /// without an agent going off to change and push anything.
    /// </summary>
    public static AutoFixOutcome Launch(
        string trigger,
        IReadOnlyList<AutoFixFailure> failures,
        AutoFixAgent agent,
        string? promptOverride = null
    )
    {
        if (agent == AutoFixAgent.None)
            return new AutoFixOutcome(false, "No agent selected.");

        if (IsBusy(out var busy))
            return Refuse($"Not starting an agent: {busy}");

        var repository = ResolveRepository(out var repositoryProblem);
        if (repository is null)
            return Refuse($"Not starting an agent: {repositoryProblem}");

        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var promptPath = Path.Join(SessionFolder, $"{stamp}-prompt.md");
        var logPath = Path.Join(SessionFolder, $"{stamp}-agent.log");

        try
        {
            Directory.CreateDirectory(SessionFolder);
            PruneOldSessions();
            var prompt = promptOverride ?? AutoFixPlanner.BuildPrompt(BuildContext(trigger, failures, repository));
            File.WriteAllText(promptPath, prompt, new UTF8Encoding(false));

            var command = AutoFixPlanner.BuildAgentCommand(agent, promptPath, repository);
            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                // To a file, not a pipe: the agent kills this process when it rebuilds
                // the client, and its output must not die with it.
                Arguments = $"/d /s /c \"{command} > \"{logPath}\" 2>&1\"",
                WorkingDirectory = repository,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            // A client started from inside an agent session inherits its markers, and
            // Claude Code refuses to start when it thinks it is nested in itself.
            foreach (var name in InheritedAgentVariables)
                startInfo.Environment.Remove(name);

            var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("cmd.exe did not start");

            var session = new Session
            {
                ProcessId = process.Id,
                ProcessStartTime = process.StartTime,
                LaunchedAt = DateTime.Now,
                Agent = agent,
                Trigger = trigger,
                Items = failures.Select(failure => failure.Name).ToList(),
                PromptPath = promptPath,
                LogPath = logPath,
            };

            lock (Sync)
            {
                var state = LoadState();
                state.Session = session;
                foreach (var name in session.Items)
                    state.HandedOff[name] = session.LaunchedAt;
                SaveState(state);
            }

            WatchExit(process, session);

            var message =
                $"Started {AutoFixPlanner.DisplayName(agent)} (pid {process.Id}) on "
                + $"{failures.Count} failed item(s) from the {trigger}: {string.Join(", ", session.Items)}. "
                + $"Transcript: {logPath}";
            Log.ZLogInformation($"{message}");
            return new AutoFixOutcome(true, message);
        }
        catch (Exception ex)
        {
            Log.ZLogError(ex, $"Failed to start {AutoFixPlanner.DisplayName(agent)}");
            return new AutoFixOutcome(false, $"Failed to start the agent: {ex.Message}");
        }
    }

    private static readonly string[] InheritedAgentVariables =
    [
        "CLAUDECODE",
        "CLAUDE_CODE_ENTRYPOINT",
        "CODEX_SANDBOX",
        "CODEX_SANDBOX_NETWORK_DISABLED",
    ];

    /// <summary>How long briefs and transcripts are kept: long enough to look back on a month of fixes.</summary>
    private static readonly TimeSpan SessionFileRetention = TimeSpan.FromDays(30);

    private static void PruneOldSessions()
    {
        foreach (var file in new DirectoryInfo(SessionFolder).EnumerateFiles())
        {
            try
            {
                if (DateTime.Now - file.LastWriteTime > SessionFileRetention)
                    file.Delete();
            }
            catch (Exception ex)
            {
                Log.ZLogDebug(ex, $"Could not delete the old session file {file.FullName}");
            }
        }
    }

    private static AutoFixOutcome Refuse(string message)
    {
        Log.ZLogWarning($"{message}");
        return new AutoFixOutcome(false, message);
    }

    /// <summary>Reports the end of a session this instance started itself.</summary>
    private static void WatchExit(Process process, Session session)
    {
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) =>
        {
            int? exitCode = null;
            try
            {
                exitCode = process.ExitCode;
            }
            catch (InvalidOperationException)
            {
                // Already disposed; the code is in the transcript anyway.
            }

            Log.ZLogInformation(
                $"{AutoFixPlanner.DisplayName(session.Agent)} (pid {session.ProcessId}) finished with exit code {exitCode}; transcript: {session.LogPath}"
            );
            EndSession(session.ProcessId);
            process.Dispose();
        };
    }

    private static void EndSession(int processId)
    {
        lock (Sync)
        {
            var state = LoadState();
            if (state.Session?.ProcessId != processId)
                return;

            state.LastSession = state.Session;
            state.Session = null;
            SaveState(state);
        }
    }

    /// <summary>
    /// True while an agent session is at work, with the reason the scheduler shows.
    /// Notices a session that ended while another instance was watching it, and
    /// ends one that has run past <see cref="SessionTimeout"/>.
    /// </summary>
    public static bool IsBusy(out string reason)
    {
        reason = "";
        Session? session;
        lock (Sync)
            session = LoadState().Session;

        if (session is null)
            return false;

        if (!IsAlive(session))
        {
            Log.ZLogInformation(
                $"{AutoFixPlanner.DisplayName(session.Agent)} (pid {session.ProcessId}) is no longer running; transcript: {session.LogPath}"
            );
            EndSession(session.ProcessId);
            return false;
        }

        if (DateTime.Now - session.LaunchedAt > SessionTimeout)
        {
            Log.ZLogWarning(
                $"{AutoFixPlanner.DisplayName(session.Agent)} (pid {session.ProcessId}) has run past {SessionTimeout.TotalHours:0} hours; stopping it"
            );
            KillSession(session);
            EndSession(session.ProcessId);
            return false;
        }

        reason =
            $"{AutoFixPlanner.DisplayName(session.Agent)} has been fixing "
            + $"{string.Join(", ", session.Items)} since {session.LaunchedAt:HH:mm}";
        return true;
    }

    /// <summary>Stops the running session, if any. For the debug tools.</summary>
    public static bool Cancel()
    {
        Session? session;
        lock (Sync)
            session = LoadState().Session;

        if (session is null || !IsAlive(session))
            return false;

        KillSession(session);
        EndSession(session.ProcessId);
        Log.ZLogInformation($"Stopped {AutoFixPlanner.DisplayName(session.Agent)} (pid {session.ProcessId})");
        return true;
    }

    /// <summary>Clears every item's cooldown. For the debug tools.</summary>
    public static int ForgetHandOffs()
    {
        lock (Sync)
        {
            var state = LoadState();
            var count = state.HandedOff.Count;
            state.HandedOff.Clear();
            SaveState(state);
            return count;
        }
    }

    /// <summary>
    /// The checkout the agent works in: the configured one, or the one this copy was
    /// built from. Null when neither is a git checkout.
    /// </summary>
    public static string? ResolveRepository(out string problem)
    {
        problem = "";
        var configured = Settings.AutoFixRepository;
        var root = configured.Length > 0 ? configured : DebugInstanceContext.WorkspaceRoot;

        try
        {
            root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
            var dotGit = Path.Join(root, ".git");
            if (Directory.Exists(dotGit) || File.Exists(dotGit))
                return root;
        }
        catch (Exception ex)
        {
            problem = $"'{root}' is not a usable path ({ex.Message})";
            return null;
        }

        problem =
            configured.Length > 0
                ? $"the configured repository '{root}' is not a git checkout"
                : $"this copy does not run from a git checkout ({root}); set the repository in Settings";
        return null;
    }

    /// <summary>
    /// How the agent restarts the client when it is done. The script rebuilds only
    /// when the client runs from the repository's own bin folder; an installed copy
    /// picks up pushed changes through its updater instead.
    /// </summary>
    public static string BuildRestartCommand(string repository)
    {
        var clientFolder = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar);
        var script = Path.Join(clientFolder, "RestartClient.ps1");
        var builtHere = string.Equals(
            clientFolder,
            Path.GetFullPath(Path.Join(repository, "bin")).TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase
        );

        var command = $"powershell -NoProfile -ExecutionPolicy Bypass -File \"{script}\"";
        if (builtHere)
            command += $" -Configuration {(DebugInstanceContext.IsDebugBuild ? "Debug" : "Release")}";
        return command;
    }

    public static AutoFixContext BuildContext(
        string trigger,
        IReadOnlyList<AutoFixFailure> failures,
        string repository
    ) =>
        new(
            trigger,
            DateTime.Now,
            failures,
            repository,
            Environment.ProcessPath ?? Path.Join(AppContext.BaseDirectory, "SoftwareCrawler.exe"),
            Environment.ProcessId,
            DebugInstanceContext.IsDebugBuild,
            SoftwareManager.ActiveSoftwarePath,
            Path.Join(AppContext.BaseDirectory, "Logs", "SoftwareCrawler.log"),
            BuildRestartCommand(repository)
        );

    /// <summary>What the debug tools report.</summary>
    public static string DescribeStatus()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Agent: {AutoFixPlanner.DisplayName(Settings.AutoFixAgent)}");

        var repository = ResolveRepository(out var problem);
        sb.AppendLine($"Repository: {repository ?? $"(unusable: {problem})"}");
        if (repository is not null)
            sb.AppendLine($"Restart command: {BuildRestartCommand(repository)}");
        sb.AppendLine($"Session folder: {SessionFolder}");
        sb.AppendLine($"State file: {StatePath}");
        sb.AppendLine(
            $"Cooldown: {AutoFixPlanner.HandOffCooldown.TotalHours:0} h, session timeout: {SessionTimeout.TotalHours:0} h"
        );

        sb.AppendLine($"Busy: {(IsBusy(out var reason) ? reason : "no")}");

        State state;
        lock (Sync)
            state = LoadState();

        AppendSession(sb, "Current session", state.Session);
        AppendSession(sb, "Last session", state.LastSession);

        sb.AppendLine("Handed off:");
        if (state.HandedOff.Count == 0)
            sb.AppendLine("  (none)");
        else
            foreach (var (name, at) in state.HandedOff.OrderBy(pair => pair.Value))
                sb.AppendLine($"  {name}\t{at:yyyy-MM-dd HH:mm}");

        return sb.ToString();

        static void AppendSession(StringBuilder sb, string title, Session? session)
        {
            if (session is null)
            {
                sb.AppendLine($"{title}: (none)");
                return;
            }

            sb.AppendLine(
                $"{title}: {AutoFixPlanner.DisplayName(session.Agent)}, pid {session.ProcessId}, "
                    + $"launched {session.LaunchedAt:yyyy-MM-dd HH:mm:ss} by the {session.Trigger}"
            );
            sb.AppendLine($"  Items: {string.Join(", ", session.Items)}");
            sb.AppendLine($"  Prompt: {session.PromptPath}");
            sb.AppendLine($"  Transcript: {session.LogPath}");
        }
    }

    /// <summary>The last lines of a transcript, for the debug tools.</summary>
    public static string TailTranscript(int lines)
    {
        Session? session;
        lock (Sync)
        {
            var state = LoadState();
            session = state.Session ?? state.LastSession;
        }

        if (session is null || !File.Exists(session.LogPath))
            return "(no transcript)";

        try
        {
            using var stream = new FileStream(
                session.LogPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete
            );
            using var reader = new StreamReader(stream);
            var all = reader.ReadToEnd().Split('\n');
            return string.Join('\n', all.TakeLast(lines));
        }
        catch (Exception ex)
        {
            return $"(cannot read {session.LogPath}: {ex.Message})";
        }
    }

    private static bool IsAlive(Session session)
    {
        try
        {
            using var process = Process.GetProcessById(session.ProcessId);
            // A recycled pid belongs to some other program; the start time tells them apart.
            return !process.HasExited
                && Math.Abs((process.StartTime - session.ProcessStartTime).TotalSeconds) < 2;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Kills the session's process tree, except any client in it. The agent starts
    /// the client it tests — and the one asking may be that very client — so
    /// Process.Kill(entireProcessTree) would take the app down with the agent.
    /// </summary>
    private static void KillSession(Session session)
    {
        try
        {
            foreach (var pid in ProcessTree.DescendantsFirst(session.ProcessId, spare: "SoftwareCrawler.exe"))
            {
                try
                {
                    using var process = Process.GetProcessById(pid);
                    process.Kill();
                }
                catch (Exception)
                {
                    // Already gone.
                }
            }
        }
        catch (Exception ex)
        {
            Log.ZLogWarning(ex, $"Failed to stop the agent session (pid {session.ProcessId})");
        }
    }

    private static State LoadState()
    {
        if (_state is not null)
            return _state;

        try
        {
            if (File.Exists(StatePath))
                _state = JsonSerializer.Deserialize<State>(File.ReadAllText(StatePath));
        }
        catch (Exception ex)
        {
            Log.ZLogWarning(ex, $"Failed to read {StatePath}; starting from a clean state");
        }

        _state ??= new State();
        _state.HandedOff = new Dictionary<string, DateTime>(_state.HandedOff, StringComparer.Ordinal);
        return _state;
    }

    private static void SaveState(State state)
    {
        _state = state;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
            var tempPath = StatePath + ".tmp";
            File.WriteAllText(
                tempPath,
                JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true })
            );
            File.Move(tempPath, StatePath, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.ZLogWarning(ex, $"Failed to save {StatePath}");
        }
    }

    /// <summary>Parent links from a Toolhelp snapshot; .NET exposes no parent pid.</summary>
    private static class ProcessTree
    {
        private const uint Th32csSnapProcess = 0x00000002;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct ProcessEntry32
        {
            public uint dwSize;
            public uint cntUsage;
            public uint th32ProcessID;
            public IntPtr th32DefaultHeapID;
            public uint th32ModuleID;
            public uint cntThreads;
            public uint th32ParentProcessID;
            public int pcPriClassBase;
            public uint dwFlags;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szExeFile;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool Process32FirstW(IntPtr snapshot, ref ProcessEntry32 entry);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool Process32NextW(IntPtr snapshot, ref ProcessEntry32 entry);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        /// <summary>
        /// The root and everything under it, deepest first so nothing is left to
        /// respawn what was just killed. A process named <paramref name="spare"/> is
        /// left out together with everything under it.
        /// </summary>
        public static List<int> DescendantsFirst(int root, string spare)
        {
            var entries = Snapshot();
            var order = new List<int> { root };
            for (var i = 0; i < order.Count; i++)
            {
                var parent = order[i];
                order.AddRange(
                    entries
                        .Where(entry =>
                            entry.Parent == parent
                            && entry.Id != parent
                            && entry.Id != Environment.ProcessId
                            && !string.Equals(entry.Name, spare, StringComparison.OrdinalIgnoreCase)
                            && !order.Contains(entry.Id)
                        )
                        .Select(entry => entry.Id)
                );
            }

            order.Reverse();
            return order;
        }

        private static List<(int Id, int Parent, string Name)> Snapshot()
        {
            var result = new List<(int, int, string)>();
            var snapshot = CreateToolhelp32Snapshot(Th32csSnapProcess, 0);
            if (snapshot == IntPtr.Zero || snapshot == new IntPtr(-1))
                return result;

            try
            {
                var entry = new ProcessEntry32 { dwSize = (uint)Marshal.SizeOf<ProcessEntry32>() };
                if (!Process32FirstW(snapshot, ref entry))
                    return result;

                do
                    result.Add(((int)entry.th32ProcessID, (int)entry.th32ParentProcessID, entry.szExeFile));
                while (Process32NextW(snapshot, ref entry));
            }
            finally
            {
                CloseHandle(snapshot);
            }

            return result;
        }
    }
}
