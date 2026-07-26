# SoftwareCrawler

- 这是一个自动爬取网页下载软件的应用。
- Design and mechanisms: [docs/Architecture.md](docs/Architecture.md) — read it before changing config storage, the download pipeline, or the debug channel.

## Layout

- `SoftwareCrawler/` — the WinForms app (WebView2 drives the crawling).
- `JeekTools.NET/` — shared library, a git submodule.
- `Tools/DebugMcpBridge/` — stdio MCP bridge that forwards to the running Debug app.
- `bin/` — build output plus the version-controlled runtime files: `Templates/Software.tab` (the crawl recipes), `7-Zip/`, and the scripts. Everything else under `bin/` is generated or user data.
- `bin/Config/` — user data only, git-ignored apart from the `.gitkeep` that keeps the folder present (its existence is what selects portable mode). `LocalSettings.tab` holds this machine's enabled flags and download directories and has no version control; a Debug build reads and writes `Templates/Software.tab` directly, a released build works on a copy seeded into `Config/`.
- `Build.cmd` / `Run.cmd` / `Publish.cmd` — build, build+launch, optimized publish.

## Rules

- After finishing a feature or fixing a bug
    - Add any interface it need for testing to debug MCP interface.
    - Automatically build and launch the program.
        - If the program from the current worktree is already running, kill only the process whose executable path matches this worktree, then run it again. Leave Debug instances from other worktrees running.
    - Use the current worktree's Debug MCP bridge to test the feature or bug, if anything wrong, try to fix it and test again, until all done.
- When reading code, logs and the Debug MCP bridge are not enough to locate a problem, use a debugger:
    - Use netcoredbg on the Debug build to set breakpoints, step, and inspect variables; feed it a command script via stdin, and drive the program to the breakpoint through the Debug MCP bridge.
    - Use dotnet-dump to analyze hangs and crashes.
    - Only attach to the current worktree's process, run the session with a timeout, and always detach when done.
- Always use rebase and fast-forward for Git, never merge.
- Use English for commit messages, keeping them to a brief sentence or two stating the purpose without elaborating on implementation details.
- Do not copy runtime files from the source directory; keep and version-control them directly under the bin directory.

## Debug MCP bridge

- Only Debug builds listen, on `127.0.0.1` with an auto-picked port (`SC_MCP_PORT` overrides).
- The app writes `bin/debug-mcp.json` (URL, process id, instance info) on startup; the bridge in `.mcp.json` reads it.
- Standard tools: `describe`, `get_value`, `set_value`, `invoke`, `list_members`, `read_logs`.
- App tools: `control_tree`, `screenshot`, `software_list`, `download_probe`.
- Object path roots: `App`, `MainForm`, `Settings`, `SoftwareManager`. `#Name` finds a control by name.
