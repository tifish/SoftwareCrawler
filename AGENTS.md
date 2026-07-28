# SoftwareCrawler

- 这是一个自动爬取网页下载软件的应用。
- Design and mechanisms: [docs/Architecture.md](docs/Architecture.md) — read it before changing config storage, the download pipeline, or the debug channel.

## Layout

- `SoftwareCrawler/` — the WinForms app (WebView2 drives the crawling).
- `JeekTools.NET/` — shared library, a git submodule.
- `Tools/ScMcp/` — stdio MCP adapter, built to `bin/ScMcp.exe`, that forwards to the running Debug app over a named pipe.
- `bin/` — build output plus the version-controlled runtime files: `Templates/Software.tab` (the crawl recipes), `7-Zip/`, and the scripts. Everything else under `bin/` is generated or user data.
- `bin/Config/` — user data only, git-ignored apart from the `.gitkeep` that keeps the folder present (its existence is what selects portable mode). `LocalSettings.tab` holds this machine's enabled flags and download directories and has no version control; a Debug build reads and writes `Templates/Software.tab` directly, a released build works on a copy seeded into `Config/`.
- `Tests/SoftwareCrawler.Tests/` — xunit tests for the logic that runs without a UI: the `.tab` format and the settings merge.
- `Build.cmd` / `Run.cmd` / `Publish.cmd` — build, build+launch, optimized publish.
- `dotnet test Tests/SoftwareCrawler.Tests/SoftwareCrawler.Tests.csproj` — run the tests. Stop this worktree's running instance first; the test build writes the same `bin/SoftwareCrawler.exe`.

## Rules

- After finishing a feature or fixing a bug
    - Add any interface it need for testing to debug MCP interface.
    - Automatically build and launch the program.
        - If the program from the current worktree is already running, kill only the process whose executable path matches this worktree, then run it again. Leave Debug instances from other worktrees running.
    - Use the current worktree's Debug MCP (`bin\ScMcp.exe`, which forwards stdio to this worktree's named pipe) to test the feature or bug, if anything wrong, try to fix it and test again, until all done.
- When reading code, logs and the Debug MCP interface are not enough to locate a problem, use a debugger:
    - Use netcoredbg on the Debug build to set breakpoints, step, and inspect variables; feed it a command script via stdin, and drive the program to the breakpoint through the Debug MCP interface.
    - Use dotnet-dump to analyze hangs and crashes.
    - Only attach to the current worktree's process, run the session with a timeout, and always detach when done.
- Always use rebase and fast-forward for Git, never merge.
- Use English for commit messages, keeping them to a brief sentence or two stating the purpose without elaborating on implementation details.
- Do not copy runtime files from the source directory; keep and version-control them directly under the bin directory.

## Debug MCP

Agents talk to a running instance over a Windows named pipe, never a TCP port. `bin\ScMcp.exe` is the stdio adapter they launch (it is what `.mcp.json` points at); it derives the pipe name from its own folder, so a worktree's copy only ever reaches that worktree's app, and it reconnects on its own when the app restarts.

- Only Debug builds listen (`DebugMcpServer.ListeningEnabled`), on `SoftwareCrawler.Mcp.Debug.<instance id>`. `McpPipeNames` is the single source of that name and is compiled into both the app and the adapter.
- Register a tool in two places: the handler in `DebugMcpServer`, the schema in `DebugMcpContract`. A tool missing from the contract is invisible to clients.
- Tool work that touches UI state runs on the UI thread through the host's invoker.
- Standard tools: `describe`, `get_value`, `set_value`, `invoke`, `list_members`, `read_logs`.
- App tools: `control_tree`, `screenshot`, `software_list`, `download_probe`, `page_state`, `storage_info`, `config_monitor`.
- Object path roots: `App`, `MainForm`, `Settings`, `SettingsStore`, `Browser`, `Software`. `#Name` finds a control by name.
- `bin/debug-mcp.json` still records which instance is up (pipe name, pid, worktree, config root), but it is for manual troubleshooting only — connecting no longer needs it.
- An open agent session keeps `bin\ScMcp.exe` locked, so `Build.cmd` / `Run.cmd` treat a failure to rebuild the adapter as a warning. Changing the adapter needs the MCP client restarted anyway.
