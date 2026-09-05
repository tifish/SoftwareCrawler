using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using JeekTools;
using Microsoft.Extensions.Logging;
using SoftwareCrawler.Models;
using ZLogger;

namespace SoftwareCrawler.Services;

/// <summary>
/// App-specific configuration over the generic <see cref="McpHost"/> in
/// JeekTools: object-graph roots, '#Name' control lookup, the WinForms tools
/// (control_tree, screenshot), the app probes, and the instance discovery file.
/// Compiled into all configurations so Debug and Release behave identically,
/// but the listener only starts in Debug builds. Agents reach it through
/// <c>bin\SoftwareCrawlerMcp.exe</c>, which forwards stdio to this instance's named pipe —
/// the pipe name carries the worktree's instance id, so parallel Debug builds
/// never answer for each other and there is no port to collide over.
/// </summary>
internal static class DebugMcpServer
{
    private static readonly ILogger Log = LogManager.CreateLogger(nameof(DebugMcpServer));

    // Runtime gate instead of #if DEBUG around the whole file: the code
    // compiles in every configuration, only Debug builds actually listen.
    private static readonly bool ListeningEnabled = DebugInstanceContext.IsDebugBuild;

    private static readonly JsonSerializerOptions PrettyOptions = new() { WriteIndented = true };

    private static readonly ObjectGraph Graph = new(
        new ObjectGraphOptions
        {
            ResolveRoot = ResolveRoot,
            RootNamesHelp = "App, MainForm, Settings, SettingsStore, Browser, Software",
            FindNamedChild = (target, name) =>
                target is Control control
                    ? FindDescendantByName(control, name)
                    : throw new InvalidOperationException(
                        $"'#{name}' requires a Control; {target.GetType().Name} is not one."
                    ),
        }
    );

    private static readonly McpHost Host = CreateHost();

    public static void Start()
    {
        Host.Start();
        OnEndpointChanged();
    }

    public static void Stop()
    {
        Host.Stop();
        OnEndpointChanged();
    }

    private static McpHost CreateHost()
    {
        var host = new McpHost(
            new McpHostOptions
            {
                ServerName = "software-crawler-debug",
                ServerTitle = "SoftwareCrawler Debug Server",
                Graph = Graph,
                GetVersion = () => AutoUpdateService.GetDisplayVersion(),
                Enabled = ListeningEnabled,
                // Named pipe only: nothing to allocate, so worktree instances cannot
                // collide over a port and the adapter needs no discovery to connect.
                PipeName = DebugInstanceContext.DebugMcpPipeName,
                DefaultPort = 0,
                UiInvoker = InvokeOnUiThread,
                Describe = BuildDescribeText,
                ToolListProvider = DebugMcpContract.BuildToolList,
            }
        );

        host.AddTool("control_tree", ControlTreeAsync);
        host.AddTool("screenshot", ScreenshotAsync);
        host.AddTool("software_list", SoftwareListAsync);
        host.AddTool("download_probe", DownloadProbeAsync);
        host.AddTool("download_batch", DownloadBatchAsync);
        host.AddTool("script_edit", ScriptEditAsync);
        host.AddTool("page_state", PageStateAsync);
        host.AddTool("storage_info", _ => Task.FromResult(StorageInfo()));
        host.AddTool("config_monitor", _ => Task.FromResult(ConfigMonitorInfo()));
        host.AddTool("schedule", ScheduleAsync);
        return host;
    }

    private static Task<T> OnUiAsync<T>(Func<T> func) => Host.OnUiAsync(func);

    private static JsonObject ToolText(string text, bool isError = false) =>
        McpHost.ToolText(text, isError);

    #region UI thread

    // MainForm.Current rather than Application.OpenForms: a resident instance sits
    // in the tray with its window hidden, and WinForms leaves hidden forms out of
    // OpenForms, so looking there would find the browser host window instead.
    private static Form? MainForm =>
        SoftwareCrawler.MainForm.Current ?? Application.OpenForms.Cast<Form>().FirstOrDefault();

    /// <summary>The main window as itself, for the tools that need more than <see cref="Form"/>.</summary>
    private static MainForm? CrawlerForm => SoftwareCrawler.MainForm.Current;

    /// <summary>
    /// Marshals onto the UI thread through the main form, with a timeout so a
    /// blocked UI thread fails the tool call instead of hanging the request.
    /// </summary>
    private static Task<object?> InvokeOnUiThread(Func<object?> func)
    {
        var form = MainForm;
        if (form is null || !form.IsHandleCreated || form.IsDisposed)
            return Task.FromResult(func());

        var completion = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        form.BeginInvoke(() =>
        {
            try
            {
                completion.TrySetResult(func());
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        });

        return completion.Task.WaitAsync(TimeSpan.FromSeconds(15));
    }

    #endregion

    #region Discovery

    /// <summary>
    /// The discovery file is no longer how the adapter finds us — it derives the pipe name
    /// from its own folder — but it stays as the human-readable record of which worktree,
    /// process, and config root this instance is.
    /// </summary>
    private static void OnEndpointChanged()
    {
        var pipe = Host.PipeName;
        DebugInstanceContext.SetMcpPipeName(pipe);
        if (pipe.Length > 0)
        {
            WriteDiscovery();
            Log.ZLogInformation(
                $@"Debug MCP server listening on \\.\pipe\{pipe} for {DebugInstanceContext.InstanceLabel}"
            );
        }
        else
        {
            DeleteOwnedDiscovery();
        }
    }

    private static void WriteDiscovery()
    {
        try
        {
            var info = DebugInstanceContext.Info;
            var discovery = new DebugMcpDiscovery
            {
                PipeName = Host.PipeName,
                ProcessId = Environment.ProcessId,
                ExecutablePath = Environment.ProcessPath ?? "",
                InstanceId = info.InstanceId,
                InstanceLabel = info.InstanceLabel,
                WorkspaceRoot = info.WorkspaceRoot,
                ConfigRoot = info.ConfigRoot,
                RuntimeTempRoot = info.RuntimeTempRoot,
            };
            SharedDataFile.WriteAllTextAtomic(
                DebugInstanceContext.DiscoveryPath,
                JsonSerializer.Serialize(discovery, PrettyOptions)
            );
        }
        catch (Exception ex)
        {
            Log.ZLogWarning(ex, $"Could not write Debug MCP discovery file");
        }
    }

    private static void DeleteOwnedDiscovery()
    {
        try
        {
            var path = DebugInstanceContext.DiscoveryPath;
            if (!File.Exists(path))
                return;

            var discovery = JsonSerializer.Deserialize<DebugMcpDiscovery>(File.ReadAllText(path));
            if (discovery?.ProcessId == Environment.ProcessId)
                File.Delete(path);
        }
        catch
        {
            // Best-effort cleanup; the bridge rejects stale process ids.
        }
    }

    #endregion

    #region Roots

    /// <summary>The object the "App" root resolves to: everything worth reaching from one place.</summary>
    private sealed class AppRoot
    {
        /// <summary>
        /// Open forms, with the main window first even when it is hidden in the
        /// tray and therefore missing from Application.OpenForms.
        /// </summary>
        public IReadOnlyList<Form> Forms
        {
            get
            {
                var forms = Application.OpenForms.Cast<Form>().ToList();
                if (DebugMcpServer.MainForm is { } main && !forms.Contains(main))
                    forms.Insert(0, main);
                return forms;
            }
        }

        public Form? MainForm => DebugMcpServer.MainForm;
        public BrowserObject Browser => BrowserObject.Browser;
        public AppSettings Settings => SettingsSingletonContainer.Settings;
        public SettingsService SettingsStore => SettingsSingletonContainer.SettingsStore;
        public List<SoftwareItem> Software => SoftwareManager.Items;
        public DebugInstanceInfo Instance => DebugInstanceContext.Info;

        /// <summary>True while a download holds the gate that keeps them one at a time.</summary>
        public bool IsDownloading => SoftwareItem.IsDownloading;

        /// <summary>The resident scheduler, or null when this instance is one-shot.</summary>
        public DownloadScheduler? Scheduler => CrawlerForm?.Scheduler;

        /// <summary>True when the crawling browser window is where the user can see it.</summary>
        public bool BrowserWindowShown => CrawlerForm?.IsBrowserWindowShown ?? false;

        /// <summary>Same as the main window's "Show browser" button.</summary>
        public bool SetBrowserWindowShown(bool shown)
        {
            if (CrawlerForm is not { } form)
                return false;

            if (shown)
                form.ShowBrowserWindow();
            else
                form.HideBrowserWindow();

            return form.IsBrowserWindowShown == shown;
        }

        public bool StartupEnabled => StartupService.IsEnabled;
        public string StartupLocation => StartupService.Location;

        /// <summary>Same as the schedule tool's enable_startup/disable_startup.</summary>
        public bool SetStartupEnabled(bool enabled) =>
            StartupService.Apply(enabled, out _);

        /// <summary>The queue behind the download and test menu items.</summary>
        public DownloadBatch? DownloadBatch => CrawlerForm?.DownloadBatch;

        /// <summary>Rows the grid is currently showing, to compare against the loaded list.</summary>
        public int GridRowCount =>
            MainForm is { } form
            && form.Controls.Find("softwareListDataGridView", searchAllChildren: true)
                .FirstOrDefault()
                is DataGridView grid
                ? grid.RowCount
                : -1;

        /// <summary>Names selected in the grid, in row order.</summary>
        public IReadOnlyList<string> GridSelectedNames =>
            MainForm is { } form
            && form.Controls.Find("softwareListDataGridView", searchAllChildren: true)
                .FirstOrDefault()
                is DataGridView grid
                ? grid.SelectedRows
                    .Cast<DataGridViewRow>()
                    .OrderBy(row => row.Index)
                    .Select(row => (row.DataBoundItem as SoftwareItem)?.Name)
                    .Where(name => !string.IsNullOrEmpty(name))
                    .Cast<string>()
                    .ToArray()
                : [];

        /// <summary>The name of the row currently at the top of the grid viewport.</summary>
        public string? GridFirstDisplayedName
        {
            get
            {
                if (
                    MainForm is not { } form
                    || form.Controls.Find("softwareListDataGridView", searchAllChildren: true)
                        .FirstOrDefault()
                        is not DataGridView grid
                )
                    return null;

                try
                {
                    var rowIndex = grid.FirstDisplayedScrollingRowIndex;
                    return rowIndex >= 0 && rowIndex < grid.Rows.Count
                        ? (grid.Rows[rowIndex].DataBoundItem as SoftwareItem)?.Name
                        : null;
                }
                catch (InvalidOperationException)
                {
                    return null;
                }
            }
        }

        /// <summary>The row index currently at the top of the grid viewport.</summary>
        public int GridFirstDisplayedRowIndex
        {
            get
            {
                if (
                    MainForm is not { } form
                    || form.Controls.Find("softwareListDataGridView", searchAllChildren: true)
                        .FirstOrDefault()
                        is not DataGridView grid
                )
                    return -1;

                try
                {
                    return grid.FirstDisplayedScrollingRowIndex;
                }
                catch (InvalidOperationException)
                {
                    return -1;
                }
            }
        }

        /// <summary>Per-machine settings held for names the loaded list does not contain.</summary>
        public IReadOnlyList<string> UnclaimedLocalSettings =>
            SoftwareManager.UnclaimedLocalSettingNames;

        /// <summary>
        /// Writes the software list immediately, skipping the save debounce.
        /// Runs off the UI thread for the same reason as
        /// <see cref="ReloadSoftwareList"/>: tools are invoked on it, and a save
        /// that has to merge will load.
        /// </summary>
        public bool FlushSoftwareList() =>
            Task.Run(() => SoftwareManager.FlushAsync()).GetAwaiter().GetResult();

        /// <summary>
        /// Reloads the software list from disk, as an outside edit would. Runs on
        /// the pool rather than inline: tools are invoked on the UI thread, and
        /// Load resumes on the captured context, which blocking here would deadlock.
        /// </summary>
        public void ReloadSoftwareList()
        {
            if (CrawlerForm is { } form)
            {
                form.ReloadSoftwareListFromDebug();
                return;
            }

            Task.Run(() => SoftwareManager.Load()).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Lists the files an item's delete pattern would remove from its download
        /// directory the next time it downloads, without removing anything.
        /// </summary>
        public IReadOnlyList<string> PreviewOldVersionDeletion(string name)
        {
            var item = SoftwareManager.Items.FirstOrDefault(candidate =>
                candidate.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
            );
            if (item is null)
                return [$"No software item named '{name}'."];

            return DownloadPipeline
                .SelectOldVersions(
                    item.FinalDownloadDirectory,
                    item.FilePatternToDeleteBeforeDownload,
                    keepFile: ""
                )
                .Select(Path.GetFileName)
                .ToArray()!;
        }

        /// <summary>
        /// Runs the pipeline's extraction step for one item against a given
        /// archive, for checking that a broken one is reported rather than passed.
        /// </summary>
        public void ExtractProbe(string name, string archiveFile)
        {
            var item =
                SoftwareManager.Items.FirstOrDefault(candidate =>
                    candidate.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
                ) ?? throw new InvalidOperationException($"No software item named '{name}'.");

            Task.Run(() => DownloadPipeline.ExtractOnly(item, archiveFile))
                .GetAwaiter()
                .GetResult();
        }

        /// <summary>
        /// Records an archive download and optionally applies the deletion that
        /// follows a successfully executed extraction or event script.
        /// </summary>
        public void FinalizeArchiveProbe(
            string name,
            string archiveFile,
            bool processingSucceeded
        )
        {
            var item =
                SoftwareManager.Items.FirstOrDefault(candidate =>
                    candidate.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
                ) ?? throw new InvalidOperationException($"No software item named '{name}'.");

            Task.Run(() =>
                    DownloadPipeline.FinalizeArchiveFile(
                        item,
                        archiveFile,
                        processingSucceeded
                    )
                )
                .GetAwaiter()
                .GetResult();
        }

        /// <summary>Reports the archive-metadata decision without starting a download.</summary>
        public object ArchiveMetadataProbe(string name, string archiveFile, long currentSize)
        {
            var item =
                SoftwareManager.Items.FirstOrDefault(candidate =>
                    candidate.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
                ) ?? throw new InvalidOperationException($"No software item named '{name}'.");

            var probeItem = new SoftwareItem
            {
                Name = item.Name,
                DownloadDirectory = Path.GetDirectoryName(archiveFile)!,
            };
            var found = DownloadPipeline.TryCompareArchiveMetadata(
                probeItem,
                archiveFile,
                currentSize,
                currentLastModified: null,
                out var metadataFilePath,
                out var isSame
            );
            return new
            {
                MetadataFound = found,
                IsSame = isSame,
                MetadataFilePath = metadataFilePath,
            };
        }

        /// <summary>
        /// Runs a helper process the way the pipeline runs 7-Zip and event
        /// scripts, and returns its exit code (-1 when it could not start).
        /// </summary>
        public int RunProcessProbe(string fileName, string arguments, string workingDirectory) =>
            Task.Run(() =>
                    DownloadPipeline.RunProcessAsync(fileName, arguments, workingDirectory, "probe")
                )
                .GetAwaiter()
                .GetResult();

        /// <summary>
        /// Runs the staged-file delete the download pipeline uses, for checking
        /// that a locked file makes it give up instead of hanging. True when the
        /// file is gone.
        /// </summary>
        public bool DeleteStagedFileProbe(string path) =>
            Task.Run(() => DownloadPipeline.DeleteStagedFile(path)).GetAwaiter().GetResult();

        /// <summary>Takes today's config backup now, without waiting for a save.</summary>
        public string BackupConfigNow()
        {
            ConfigBackupService.BackupDaily(
                Path.Join(SettingsStore.ResolveConfigRoot(), "Software.tab"),
                Path.Join(SettingsStore.ResolveConfigRoot(), "LocalSettings.tab"),
                SettingsStore.MachineSettingsPath,
                SettingsStore.RoamingSettingsPath
            );
            return ConfigBackupService.Root;
        }

        /// <summary>
        /// Drops the preserved per-machine rows, as the context menu item does
        /// but without the confirmation. False means the save was skipped.
        /// </summary>
        public bool CleanUpLocalSettings() =>
            SoftwareManager.RemoveUnclaimedLocalSettings().GetAwaiter().GetResult();

        /// <summary>
        /// The open settings window, if any. Debug helpers show it modelessly
        /// so the object graph stays reachable; the menu still uses ShowDialog.
        /// </summary>
        public SettingsForm? SettingsForm =>
            Application.OpenForms.OfType<SettingsForm>().FirstOrDefault();

        /// <summary>
        /// Opens Settings as a modeless window so tools can inspect it. If it is
        /// already open, brings that instance forward instead of creating another.
        /// </summary>
        public void OpenSettings()
        {
            if (SettingsForm is { } existing)
            {
                if (existing.WindowState == FormWindowState.Minimized)
                    existing.WindowState = FormWindowState.Normal;
                existing.Activate();
                return;
            }

            var form = new SettingsForm();
            form.Show(MainForm);
        }

        /// <summary>Closes the settings window if it is open. False when none was.</summary>
        public bool CloseSettings()
        {
            if (SettingsForm is not { } form)
                return false;

            form.Close();
            return true;
        }
    }

    private static readonly AppRoot Root = new();

    private static object ResolveRoot(string name) =>
        name switch
        {
            "App" => Root,
            "MainForm" => MainForm
                ?? throw new InvalidOperationException("No form is open yet."),
            "Settings" => SettingsSingletonContainer.Settings,
            "SettingsStore" => SettingsSingletonContainer.SettingsStore,
            "Browser" => BrowserObject.Browser,
            "Software" => SoftwareManager.Items,
            _ => throw new InvalidOperationException(
                $"Unknown root '{name}'. Available roots: App, MainForm, Settings, SettingsStore, Browser, Software."
            ),
        };

    private static Control? FindDescendantByName(Control root, string name)
    {
        var queue = new Queue<Control>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var control = queue.Dequeue();
            if (control.Name == name)
                return control;
            foreach (Control child in control.Controls)
                queue.Enqueue(child);
        }

        return null;
    }

    #endregion

    #region Describe

    private static string BuildDescribeText()
    {
        var sb = new StringBuilder();
        var instance = DebugInstanceContext.Info;
        sb.AppendLine(
            $@"SoftwareCrawler debug MCP server on \\.\pipe\{Host.PipeName} (build {AutoUpdateService.GetDisplayVersion()})."
        );
        sb.AppendLine($"InstanceId: {instance.InstanceId}");
        sb.AppendLine($"InstanceLabel: {instance.InstanceLabel}");
        sb.AppendLine($"WorkspaceRoot: {instance.WorkspaceRoot}");
        sb.AppendLine($"ProcessId: {instance.ProcessId}");
        sb.AppendLine($"ConfigRoot: {instance.ConfigRoot}");
        sb.AppendLine($"RuntimeTempRoot: {instance.RuntimeTempRoot}");
        sb.AppendLine(
            $"Process uptime: {DateTime.Now - Process.GetCurrentProcess().StartTime:hh\\:mm\\:ss}."
        );
        sb.AppendLine($"Log file: {LogManager.CurrentRollingLogFile}");
        sb.AppendLine();
        sb.AppendLine("Roots for object paths:");
        sb.AppendLine("- App: forms, browser, settings, software list, instance info");
        sb.AppendLine("- MainForm: the main window");
        sb.AppendLine("- Settings: the merged settings the app reads");
        sb.AppendLine("- SettingsStore: the settings service (paths, save, reload)");
        sb.AppendLine("- Browser: the WebView2 wrapper used for crawling");
        sb.AppendLine("- Software: the loaded software items");
        sb.AppendLine();
        sb.AppendLine(DebugMcpContract.PathHelp);
        sb.AppendLine();
        sb.AppendLine($"Software items loaded: {SoftwareManager.Items.Count}");
        sb.AppendLine(
            "Open forms: "
                + string.Join(
                    ", ",
                    Application.OpenForms.Cast<Form>().Select(form => form.Name)
                )
        );
        return sb.ToString();
    }

    #endregion

    #region Tools

    private static async Task<JsonObject> ControlTreeAsync(JsonObject args)
    {
        var path = args["path"]?.GetValue<string>() ?? "MainForm";
        var maxDepth = args["max_depth"]?.GetValue<int>() ?? 12;

        var text = await OnUiAsync(() =>
        {
            if (Graph.Resolve(path) is not Control control)
                throw new InvalidOperationException($"'{path}' is not a Control.");

            var sb = new StringBuilder();
            Dump(control, 0);
            return sb.ToString();

            void Dump(Control current, int depth)
            {
                if (depth > maxDepth)
                    return;

                sb.Append(' ', depth * 2)
                    .Append(current.GetType().Name)
                    .Append(" Name=")
                    .Append(current.Name.Length == 0 ? "(unnamed)" : current.Name)
                    .Append(" Visible=")
                    .Append(current.Visible)
                    .Append(" Enabled=")
                    .Append(current.Enabled)
                    .Append(" Bounds=")
                    .Append(current.Bounds)
                    .AppendLine();

                foreach (Control child in current.Controls)
                    Dump(child, depth + 1);
            }
        });

        return ToolText(text);
    }

    private static async Task<JsonObject> ScreenshotAsync(JsonObject args)
    {
        var path = args["path"]?.GetValue<string>() ?? "MainForm";

        var (bytes, width, height, label) = await OnUiAsync(() =>
        {
            if (Graph.Resolve(path) is not Control control)
                throw new InvalidOperationException($"'{path}' is not a Control.");

            var size = control is Form form ? form.ClientSize : control.Size;
            using var bitmap = new Bitmap(Math.Max(1, size.Width), Math.Max(1, size.Height));
            control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            using var stream = new MemoryStream();
            bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
            return (stream.ToArray(), bitmap.Width, bitmap.Height, path);
        });

        return new JsonObject
        {
            ["content"] = new JsonArray(
                new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = $"{label} screenshot, {width}x{height}px.",
                },
                new JsonObject
                {
                    ["type"] = "image",
                    ["data"] = Convert.ToBase64String(bytes),
                    ["mimeType"] = "image/png",
                }
            ),
        };
    }

    private static async Task<JsonObject> SoftwareListAsync(JsonObject args)
    {
        var filter = args["filter"]?.GetValue<string>() ?? "";
        var onlyFailed = args["only_failed"]?.GetValue<bool>() ?? false;

        var text = await OnUiAsync(() =>
        {
            var items = SoftwareManager.Items.AsEnumerable();
            if (filter.Length > 0)
                items = items.Where(item =>
                    item.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                );
            if (onlyFailed)
                items = items.Where(item => item.Status == DownloadingStatus.Failed);

            var lines = items
                .Select(item =>
                    $"{(item.Enabled ? "x" : " ")} {(item.UseProxy ? "p" : " ")} {item.Name}\t{item.Status}\t{item.Progress}\t{item.ErrorMessage}"
                )
                .ToList();
            return lines.Count == 0
                ? "(no matching software items)"
                : "Enabled UseProxy Name\tStatus\tProgress\tError\n" + string.Join('\n', lines);
        });

        return ToolText(text);
    }

    private static async Task<JsonObject> DownloadProbeAsync(JsonObject args)
    {
        var name = McpHost.RequiredString(args, "name");
        var testOnly = args["test_only"]?.GetValue<bool>() ?? true;

        var item = SoftwareManager.Items.FirstOrDefault(candidate =>
            candidate.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
        );
        if (item is null)
            return ToolText($"No software item named '{name}'.", isError: true);

        // Downloading drives the browser, which lives on the UI thread; the
        // returned task is awaited off it so the request is not blocked.
        var download = await OnUiAsync(() =>
        {
            item.ResetStatus();
            return item.Download(testOnly, Settings.DownloadRetryCount);
        });
        var succeeded = await download;

        return ToolText(
            $"{item.Name}: {(succeeded ? "succeeded" : "failed")}\n"
                + $"Status: {item.Status}\n"
                + $"Progress: {item.Progress}\n"
                + $"Error: {item.ErrorMessage}"
        );
    }

    /// <summary>
    /// Drives a whole batch the way the menu does, so the ordering, the cancel
    /// path and the "one at a time" rule can be exercised without clicking.
    /// </summary>
    private static async Task<JsonObject> DownloadBatchAsync(JsonObject args)
    {
        var action = args["action"]?.GetValue<string>() ?? "status";
        var form = CrawlerForm;
        if (form is null)
            return ToolText("The main window is not open yet.", isError: true);

        var batch = form.DownloadBatch;

        switch (action)
        {
            case "cancel":
                await OnUiAsync<object?>(() =>
                {
                    batch.Cancel();
                    return null;
                });
                return ToolText(BatchStatus(batch, "Cancel requested."));

            case "status":
                return ToolText(BatchStatus(batch));

            case "run":
                break;

            default:
                return ToolText($"Unknown action '{action}'. Use run, cancel or status.", true);
        }

        if (batch.IsRunning)
            return ToolText(BatchStatus(batch, "A batch is already running."), isError: true);

        var testOnly = args["test_only"]?.GetValue<bool>() ?? true;
        var wait = args["wait"]?.GetValue<bool>() ?? true;
        var names = args["names"]?.GetValue<string>() ?? "";

        List<SoftwareItem> items;
        if (string.IsNullOrWhiteSpace(names))
        {
            items = SoftwareManager.Items.ToList();
        }
        else
        {
            items = [];
            foreach (
                var name in names.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            )
            {
                var item = SoftwareManager.Items.FirstOrDefault(candidate =>
                    candidate.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
                );
                if (item is null)
                    return ToolText($"No software item named '{name}'.", isError: true);
                items.Add(item);
            }
        }

        // Same shape as download_probe: start on the UI thread the browser lives
        // on, then await the returned task off it so the request is not blocked.
        var run = await OnUiAsync(() =>
            form.RunBatchAsync(
                items,
                testOnly,
                testOnly ? 0 : Settings.DownloadRetryCount,
                "DebugMcp"
            )
        );

        if (!wait)
        {
            _ = run.ContinueWith(
                task => Log.ZLogError(task.Exception!, $"Detached debug batch failed"),
                TaskContinuationOptions.OnlyOnFaulted
            );
            return ToolText(BatchStatus(batch, $"Started {items.Count} item(s), not waiting."));
        }

        var succeeded = await run;

        var lines = items.Select(item =>
            $"{item.Name}\t{item.Status}\t{item.Progress}\t{item.ErrorMessage}"
        );
        return ToolText(
            $"Batch {(succeeded ? "succeeded" : "failed")} over {items.Count} item(s), "
                + $"cancelled = {batch.HasCancelled}\n"
                + "Name\tStatus\tProgress\tError\n"
                + string.Join('\n', lines)
        );
    }

    /// <summary>
    /// The edit-script round trip without the dialogs the menu item puts around
    /// it: export the slots to the file an editor would open, then apply it back.
    /// </summary>
    private static async Task<JsonObject> ScriptEditAsync(JsonObject args)
    {
        var name = McpHost.RequiredString(args, "name");
        var action = args["action"]?.GetValue<string>() ?? "status";

        var item = SoftwareManager.Items.FirstOrDefault(candidate =>
            candidate.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
        );
        if (item is null)
            return ToolText($"No software item named '{name}'.", isError: true);

        var session = new ScriptEditSession(item);

        switch (action)
        {
            case "export":
                await session.WriteAsync();
                return ToolText($"Wrote {item.GetXPathOrScripts().Count} slot(s) to {session.FilePath}");

            case "apply":
                if (!await session.ApplyAsync())
                    return ToolText($"Nothing to apply: {session.FilePath} does not exist.", true);

                // Start the save on the UI thread the grid is bound on, then await
                // it off that thread, the same way download_probe does.
                await await OnUiAsync(() => SoftwareManager.Save());
                return ToolText(
                    $"Applied {session.FilePath} to {item.Name}:\n"
                        + string.Join("\n---\n", item.GetXPathOrScripts())
                );

            case "discard":
                session.Discard();
                return ToolText($"Discarded {session.FilePath}");

            case "status":
                return ToolText(
                    $"File: {session.FilePath}\n"
                        + $"Waiting to be applied: {session.HasUnappliedFile}\n"
                        + $"Slots in the item: {item.GetXPathOrScripts().Count}"
                );

            default:
                return ToolText(
                    $"Unknown action '{action}'. Use export, apply, discard or status.",
                    isError: true
                );
        }
    }

    private static string BatchStatus(DownloadBatch batch, string? prefix = null) =>
        (prefix is null ? "" : prefix + "\n")
        + $"Running: {batch.IsRunning}\n"
        + $"Cancelled: {batch.HasCancelled}\n"
        + $"Current item: {batch.CurrentItem?.Name ?? "(none)"}\n"
        + $"Status: {batch.CurrentItem?.Status.ToString() ?? "(none)"}";

    /// <summary>
    /// What the crawler is waiting on right now. The click target probe is the same one
    /// the pipeline polls, so this answers "is the page slow, or is the XPath wrong".
    /// </summary>
    private static async Task<JsonObject> PageStateAsync(JsonObject args)
    {
        var xpath = args["xpath"]?.GetValue<string>() ?? "";
        var frame = args["frame"]?.GetValue<string>() ?? "";

        var sb = new StringBuilder();

        var state = await OnUiAsync(() =>
        {
            var core = Browser.WebView2?.CoreWebView2;
            return (
                Url: core?.Source ?? "(browser not initialized)",
                LoadEndedFor: Browser.LoadEndedFor,
                QuietFor: Browser.NetworkQuietFor,
                InPlaceFor: Browser.NavigatedInPlaceFor,
                Settled: Browser.IsPageSettled,
                Ready: core != null
            );
        });

        static string Age(TimeSpan? span, string never) =>
            span is { } value ? $"{value.TotalSeconds:F1}s ago" : never;

        sb.AppendLine($"URL: {state.Url}");
        sb.AppendLine($"Load ended: {Age(state.LoadEndedFor, "(not yet)")}");
        sb.AppendLine($"Network went quiet: {Age(state.QuietFor, "(still fetching)")}");
        sb.AppendLine($"Swapped in place: {Age(state.InPlaceFor, "(no)")}");
        sb.AppendLine($"Page settled: {state.Settled}");

        if (xpath.Length > 0 && state.Ready)
        {
            var probe = await await OnUiAsync(() => Browser.ProbeClickTarget(xpath, frame));
            sb.AppendLine($"Click target: {probe}");
            sb.AppendLine($"XPath: {xpath}");
            if (frame.Length > 0)
                sb.AppendLine($"Frame: {frame}");
        }

        return ToolText(sb.ToString());
    }

    private static JsonObject StorageInfo()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Storage mode (saved): {Settings.StorageLocation}");
        sb.AppendLine($"Storage mode (effective): {SettingsStore.CurrentStorageLocation}");
        sb.AppendLine($"Portable forced: {SettingsService.IsPortable}");
        sb.AppendLine($"Custom path: {Settings.CustomStoragePath ?? "(none)"}");
        sb.AppendLine($"Machine settings: {SettingsStore.MachineSettingsPath}");
        sb.AppendLine($"Roaming settings: {SettingsStore.RoamingSettingsPath}");
        sb.AppendLine($"Roaming config root: {SettingsStore.ResolveConfigRoot()}");
        sb.AppendLine($"Machine config root: {SettingsService.MachineConfigRoot}");
        sb.AppendLine($"Program config root: {SettingsService.ProgramConfigRoot}");
        sb.AppendLine($"Watching: {ConfigChangeMonitor.Root}");
        sb.AppendLine($"WebView2 profile: {Browser.UserDataFolder}");
        sb.AppendLine(
            $"Instance lock: {SingleInstanceGuard.LockPath} "
                + $"(held by pid {SingleInstanceGuard.Current?.OwnerProcessId ?? 0})"
        );
        return ToolText(sb.ToString());
    }

    /// <summary>
    /// What the config watcher currently believes about the files it protects:
    /// whether the app would refuse to overwrite them, and what it last saw.
    /// </summary>
    /// <summary>
    /// Inspects and drives the resident scheduler. Window visibility is part of the
    /// tool because "the main window is open" is one of the conditions that hold a
    /// run back, and it cannot otherwise be exercised from an agent session.
    /// </summary>
    private static async Task<JsonObject> ScheduleAsync(JsonObject args)
    {
        var action = args["action"]?.GetValue<string>() ?? "status";

        var form = CrawlerForm;
        if (form is null)
            return ToolText("The main window is not open yet.", isError: true);

        switch (action)
        {
            case "status":
                break;

            case "show_window":
                await OnUiAsync<object?>(() =>
                {
                    form.ShowMainWindow();
                    return null;
                });
                break;

            case "hide_window":
                await OnUiAsync<object?>(() =>
                {
                    form.HideToTray();
                    return null;
                });
                break;

            case "toggle_window":
                // What a left click on the tray icon runs.
                await OnUiAsync<object?>(() =>
                {
                    form.ToggleMainWindow();
                    return null;
                });
                break;

            case "enable_startup":
            case "disable_startup":
            {
                var enable = action == "enable_startup";
                if (!StartupService.Apply(enable, out var startupError))
                    return ToolText(
                        $"Could not {(enable ? "add" : "remove")} the startup entry: {startupError}",
                        isError: true
                    );

                break;
            }

            case "run_frequent":
            case "run_full":
            {
                if (form.Scheduler is null)
                    return ToolText(
                        "The scheduler is not running (this instance is not resident).",
                        isError: true
                    );

                var kind =
                    action == "run_full" ? ScheduledRunKind.Full : ScheduledRunKind.Frequent;
                // Start on the UI thread the browser lives on, then await the
                // returned task off it so the request is not blocking that thread.
                var run = await OnUiAsync(() => form.Scheduler.RunNowAsync(kind));
                if (!await run)
                    return ToolText("A run is already in flight.", isError: true);

                break;
            }

            default:
                return ToolText(
                    $"Unknown action '{action}'. Use status, run_frequent, run_full, show_window, toggle_window, "
                        + "hide_window, enable_startup or disable_startup.",
                    isError: true
                );
        }

        return ToolText(await OnUiAsync(() => ScheduleStatusText(form)));
    }

    private static string ScheduleStatusText(MainForm form)
    {
        var sb = new StringBuilder();

        if (form.Scheduler is null)
        {
            sb.AppendLine("Scheduler: not running (this instance is not resident)");
        }
        else
        {
            var status = form.Scheduler.GetStatus();
            sb.AppendLine(
                $"Full run times: "
                    + (
                        status.ScheduledDownloadTimes.Count == 0
                            ? "(none)"
                            : string.Join(", ", status.ScheduledDownloadTimes)
                    )
            );
            sb.AppendLine(
                $"Frequent check: every {status.FrequentCheckIntervalMinutes} min "
                    + $"over {status.FrequentItemCount} item(s)"
            );
            sb.AppendLine($"Last full run: {Describe(status.LastFullRun)}");
            sb.AppendLine($"Next full run: {Describe(status.NextFullDue)}");
            sb.AppendLine($"Last frequent check: {Describe(status.LastFrequentRun)}");
            sb.AppendLine($"Next frequent check: {Describe(status.NextFrequentDue)}");
            sb.AppendLine($"Running now: {status.IsRunning}");
            sb.AppendLine(
                $"Last skip reason: {(status.LastSkipReason.Length == 0 ? "(none)" : status.LastSkipReason)}"
            );
        }

        sb.AppendLine();
        sb.AppendLine($"Window visible: {form.Visible}");
        sb.AppendLine($"Browser window shown: {form.IsBrowserWindowShown}");
        sb.AppendLine($"Batch running: {form.DownloadBatch.IsRunning}");
        sb.AppendLine(
            $"User busy: {UserPresence.IsBusy(out var busyReason)}"
                + (busyReason.Length == 0 ? "" : $" ({busyReason})")
        );
        sb.AppendLine($"Run at startup: {StartupService.IsEnabled}");
        sb.AppendLine($"Startup entry: {StartupService.Location}");
        sb.AppendLine();

        sb.AppendLine("Frequent items:");
        var frequent = SoftwareManager
            .Items.Where(item => item is { Enabled: true, FrequentCheck: true })
            .ToList();
        if (frequent.Count == 0)
            sb.AppendLine("  (none)");
        else
            foreach (var item in frequent)
                sb.AppendLine($"  {item.Name}\t{item.Status}\t{item.ErrorMessage}");

        return sb.ToString();

        static string Describe(DateTime? moment) =>
            moment?.ToString("yyyy-MM-dd HH:mm:ss") ?? "never";
    }

    private static JsonObject ConfigMonitorInfo()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Watching: {string.Join(", ", ConfigChangeMonitor.Roots)}");
        sb.AppendLine($"Watcher active: {ConfigChangeMonitor.IsWatching}");
        sb.AppendLine($"Software list in use: {SoftwareManager.ActiveSoftwarePath}");
        sb.AppendLine($"Shipped template: {SoftwareManager.TemplatePath}");
        sb.AppendLine($"Pending events: {ConfigChangeMonitor.DescribePending()}");
        sb.AppendLine($"In-memory software items: {SoftwareManager.Items.Count}");
        var unclaimed = SoftwareManager.UnclaimedLocalSettingNames;
        sb.AppendLine(
            $"Unclaimed local settings kept: "
                + (unclaimed.Count == 0 ? "(none)" : string.Join(", ", unclaimed))
        );
        sb.AppendLine();

        foreach (var path in ConfigChangeMonitor.TrackedPaths)
            sb.AppendLine(
                $"{Path.GetFileName(path)}: known={ConfigChangeMonitor.DescribeKnown(path)} "
                    + $"externallyChanged={ConfigChangeMonitor.HasExternalChange(path)}"
            );

        sb.AppendLine();
        sb.AppendLine($"Backup root: {ConfigBackupService.Root}");
        var backups = ConfigBackupService.Describe();
        if (backups.Count == 0)
            sb.AppendLine("Backups: (none)");
        else
            foreach (var day in backups)
                sb.AppendLine($"  {day}");

        return ToolText(sb.ToString());
    }

    #endregion
}
