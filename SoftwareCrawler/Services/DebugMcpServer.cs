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
/// App-specific configuration over the generic <see cref="DebugMcpHost"/> in
/// JeekTools: object-graph roots, '#Name' control lookup, the WinForms tools
/// (control_tree, screenshot), the app probes, and the instance discovery file.
/// Compiled into all configurations so Debug and Release behave identically,
/// but the listener only starts in Debug builds.
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

    private static readonly DebugMcpHost Host = CreateHost();

    public static void Start() => Host.Start();

    public static void Stop() => Host.Stop();

    private static DebugMcpHost CreateHost()
    {
        var host = new DebugMcpHost(
            new DebugMcpHostOptions
            {
                ServerName = "software-crawler-debug",
                ServerTitle = "SoftwareCrawler Debug Server",
                Graph = Graph,
                GetVersion = () => AutoUpdateService.GetDisplayVersion(),
                Enabled = ListeningEnabled,
                DefaultPort = 8747,
                PortEnvironmentVariable = "SC_MCP_PORT",
                PortMutexPrefix = "SoftwareCrawler.DebugMcp.Port.",
                UiInvoker = InvokeOnUiThread,
                Describe = BuildDescribeText,
                ToolListProvider = DebugMcpContract.BuildToolList,
                UrlChanged = OnUrlChanged,
            }
        );

        host.AddTool("control_tree", ControlTreeAsync);
        host.AddTool("screenshot", _ => ScreenshotAsync());
        host.AddTool("software_list", SoftwareListAsync);
        host.AddTool("download_probe", DownloadProbeAsync);
        host.AddTool("storage_info", _ => Task.FromResult(StorageInfo()));
        host.AddTool("config_monitor", _ => Task.FromResult(ConfigMonitorInfo()));
        return host;
    }

    private static Task<T> OnUiAsync<T>(Func<T> func) => Host.OnUiAsync(func);

    private static JsonObject ToolText(string text, bool isError = false) =>
        DebugMcpHost.ToolText(text, isError);

    #region UI thread

    private static Form? MainForm =>
        Application.OpenForms.OfType<MainForm>().FirstOrDefault()
        ?? Application.OpenForms.Cast<Form>().FirstOrDefault();

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

    private static void OnUrlChanged(string url)
    {
        DebugInstanceContext.SetMcpUrl(url);
        if (url.Length > 0)
        {
            WriteDiscovery();
            Log.ZLogInformation(
                $"Debug MCP server listening on {url} for {DebugInstanceContext.InstanceLabel}"
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
                Url = Host.Url,
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
        public IReadOnlyList<Form> Forms => Application.OpenForms.Cast<Form>().ToList();
        public Form? MainForm => DebugMcpServer.MainForm;
        public BrowserObject Browser => BrowserObject.Browser;
        public AppSettings Settings => SettingsSingletonContainer.Settings;
        public SettingsService SettingsStore => SettingsSingletonContainer.SettingsStore;
        public List<SoftwareItem> Software => SoftwareManager.Items;
        public DebugInstanceInfo Instance => DebugInstanceContext.Info;

        /// <summary>True while a download holds the gate that keeps them one at a time.</summary>
        public bool IsDownloading => SoftwareItem.IsDownloading;

        /// <summary>Rows the grid is currently showing, to compare against the loaded list.</summary>
        public int GridRowCount =>
            MainForm is { } form
            && form.Controls.Find("softwareListDataGridView", searchAllChildren: true)
                .FirstOrDefault()
                is DataGridView grid
                ? grid.RowCount
                : -1;

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
        public void ReloadSoftwareList() =>
            Task.Run(() => SoftwareManager.Load()).GetAwaiter().GetResult();

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

            var directory = item.FinalDownloadDirectory;
            return DownloadPipeline
                .SelectOldVersions(
                    directory,
                    item.FilePatternToDeleteBeforeDownload,
                    keepFile: "",
                    SoftwareManager.OtherItemPatternsInDirectory(item, directory)
                )
                .Select(Path.GetFileName)
                .ToArray()!;
        }

        /// <summary>
        /// Reports the download directories more than one item writes to, which
        /// is what makes the pattern delete stand down.
        /// </summary>
        public IReadOnlyList<string> SharedDownloadDirectories()
        {
            var shared = new List<string>();
            foreach (var item in SoftwareManager.Items)
            foreach (var directory in new[] { item.DownloadDirectory, item.DownloadDirectory2 })
            {
                var others = SoftwareManager.OtherItemsUsingDirectory(item, directory);
                if (others.Count > 0)
                    shared.Add($"{directory}: {item.Name} + {string.Join(", ", others)}");
            }

            return shared;
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
            $"SoftwareCrawler debug MCP server at {Host.Url} (build {AutoUpdateService.GetDisplayVersion()})."
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

    private static async Task<JsonObject> ScreenshotAsync()
    {
        var (bytes, width, height) = await OnUiAsync(() =>
        {
            var form =
                MainForm ?? throw new InvalidOperationException("No form is open yet.");
            var size = form.ClientSize;
            using var bitmap = new Bitmap(Math.Max(1, size.Width), Math.Max(1, size.Height));
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            using var stream = new MemoryStream();
            bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
            return (stream.ToArray(), bitmap.Width, bitmap.Height);
        });

        return new JsonObject
        {
            ["content"] = new JsonArray(
                new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = $"Main form screenshot, {width}x{height}px.",
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
                    $"{(item.Enabled ? "x" : " ")} {item.Name}\t{item.Status}\t{item.Progress}\t{item.ErrorMessage}"
                )
                .ToList();
            return lines.Count == 0
                ? "(no matching software items)"
                : "Enabled Name\tStatus\tProgress\tError\n" + string.Join('\n', lines);
        });

        return ToolText(text);
    }

    private static async Task<JsonObject> DownloadProbeAsync(JsonObject args)
    {
        var name = DebugMcpHost.RequiredString(args, "name");
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
        return ToolText(sb.ToString());
    }

    /// <summary>
    /// What the config watcher currently believes about the files it protects:
    /// whether the app would refuse to overwrite them, and what it last saw.
    /// </summary>
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
