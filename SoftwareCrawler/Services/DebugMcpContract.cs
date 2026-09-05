using System.Text.Json.Nodes;

namespace SoftwareCrawler.Services;

/// <summary>
/// The record the app writes next to itself while the debug surface listens. The
/// SoftwareCrawlerMcp adapter derives the pipe name from its own folder, so this is for
/// humans checking which instance is up, not part of connecting.
/// </summary>
public sealed class DebugMcpDiscovery
{
    public string PipeName { get; set; } = "";
    public int ProcessId { get; set; }
    public string ExecutablePath { get; set; } = "";
    public string InstanceId { get; set; } = "";
    public string InstanceLabel { get; set; } = "";
    public string WorkspaceRoot { get; set; } = "";
    public string ConfigRoot { get; set; } = "";
    public string RuntimeTempRoot { get; set; } = "";
}

/// <summary>
/// The tool catalogue, kept on the app side so the stdio bridge can answer
/// tools/list even while the app is not running.
/// </summary>
public static class DebugMcpContract
{
    public const string SupportedProtocolVersion = "2025-06-18";

    public static readonly string[] KnownProtocolVersions =
    [
        "2024-11-05",
        "2025-03-26",
        SupportedProtocolVersion,
    ];

    public const string PathHelp =
        "Paths start from a root: App (forms, browser, settings, software list), MainForm, "
        + "Settings (the merged settings), SettingsStore (the settings service), Browser, or Software "
        + "(the software list). Segments: '.Member' reads a property or field (non-public included), "
        + "'[0]' indexes a list, '[\"key\"]' indexes a dictionary, and '#Name' finds a named control "
        + "below the current control. "
        + "Examples: Software[0].Name, MainForm.#softwareListDataGridView.RowCount";

    public static JsonArray BuildToolList() =>
        new(
            Tool(
                "describe",
                "Overview of the running app: instance, forms, roots, path syntax, and log file. Start here.",
                new()
            ),
            Tool(
                "get_value",
                "Read a value from the app's object graph. " + PathHelp,
                new()
                {
                    ["path"] = Prop("string", "Object path to read."),
                    ["depth"] = Prop("integer", "Nested expansion depth, 0-5 (default 1)."),
                },
                ["path"]
            ),
            Tool(
                "set_value",
                "Write a property, field, or list element on the UI thread. " + PathHelp,
                new()
                {
                    ["path"] = Prop("string", "Object path to write."),
                    ["value"] = new JsonObject
                    {
                        ["description"] = "New JSON value; {$path: ...} passes a live object.",
                    },
                },
                ["path", "value"]
            ),
            Tool(
                "invoke",
                "Call a method on the UI thread. " + PathHelp,
                new()
                {
                    ["path"] = Prop("string", "Object path ending with a method."),
                    ["args"] = new JsonObject
                    {
                        ["type"] = "array",
                        ["description"] = "JSON arguments.",
                    },
                    ["depth"] = Prop("integer", "Return expansion depth, 0-5 (default 1)."),
                },
                ["path"]
            ),
            Tool(
                "list_members",
                "List properties, fields, and methods at a path. " + PathHelp,
                new() { ["path"] = Prop("string", "Object path to inspect.") }
            ),
            Tool(
                "read_logs",
                "Read the current app log tail.",
                new()
                {
                    ["lines"] = Prop("integer", "Lines, 1-2000 (default 200)."),
                    ["filter"] = Prop("string", "Case-insensitive filter."),
                }
            ),
            Tool(
                "control_tree",
                "Dump the WinForms control tree below a control.",
                new()
                {
                    ["path"] = Prop("string", "Starting control path (default MainForm)."),
                    ["max_depth"] = Prop("integer", "Maximum depth (default 12)."),
                }
            ),
            Tool(
                "screenshot",
                "Render a form or control to PNG.",
                new()
                {
                    ["path"] = Prop(
                        "string",
                        "Control path to render (default MainForm). Example: App.SettingsForm"
                    ),
                }
            ),
            Tool(
                "software_list",
                "List the loaded software items with their enabled/UseProxy/FrequentCheck flags (x/p/f), "
                    + "download status, progress, and error message.",
                new()
                {
                    ["filter"] = Prop("string", "Case-insensitive name filter."),
                    ["only_failed"] = Prop("boolean", "Only report items that failed."),
                }
            ),
            Tool(
                "download_probe",
                "Run a download or a test-only crawl for one item by name and report the outcome. "
                    + "Use this to verify a crawl script end to end.",
                new()
                {
                    ["name"] = Prop("string", "Software item name."),
                    ["test_only"] = Prop(
                        "boolean",
                        "Resolve the download URL without saving the file (default true)."
                    ),
                },
                ["name"]
            ),
            Tool(
                "download_batch",
                "Drive the whole download queue the way the menu does: run a batch, cancel the "
                    + "running one, or report what it is on. Use this to check ordering, the "
                    + "cancel path, or a run of several items; use download_probe for just one.",
                new()
                {
                    ["action"] = Prop(
                        "string",
                        "run, cancel or status (default status).",
                        ["run", "cancel", "status"]
                    ),
                    ["names"] = Prop(
                        "string",
                        "Comma-separated item names to run, in order. Omit for every item."
                    ),
                    ["test_only"] = Prop(
                        "boolean",
                        "Resolve the download URLs without saving the files (default true)."
                    ),
                    ["wait"] = Prop(
                        "boolean",
                        "Wait for the batch to finish (default true). Pass false to return at "
                            + "once, then use action=cancel or action=status."
                    ),
                }
            ),
            Tool(
                "script_edit",
                "Round-trip one item's XPaths and scripts through the file an external editor "
                    + "would open, without the dialogs the menu item puts around it. Export, edit "
                    + "the file yourself, then apply.",
                new()
                {
                    ["name"] = Prop("string", "Software item name."),
                    ["action"] = Prop(
                        "string",
                        "export, apply, discard or status (default status).",
                        ["export", "apply", "discard", "status"]
                    ),
                },
                ["name"]
            ),
            Tool(
                "page_state",
                "Report what the crawling browser sees right now: the current URL, whether the load "
                    + "event fired and whether the page stopped fetching. With an xpath, also probe "
                    + "that click target: ready, pending (there but disabled, invisible, or a "
                    + "placeholder link whose scripts have not wired it up), or missing. "
                    + "Use this to tell 'the page is slow' from 'the target never becomes clickable'.",
                new()
                {
                    ["xpath"] = Prop("string", "Optional XPath of a click target to probe."),
                    ["frame"] = Prop("string", "Optional frame name the XPath lives in."),
                }
            ),
            Tool(
                "storage_info",
                "Report the resolved settings paths: machine config, roaming config, storage mode, and whether portable mode is forced.",
                new()
            ),
            Tool(
                "schedule",
                "Inspect and drive the resident scheduler: the configured full-run times, the frequent-check "
                    + "interval and which items it covers, when each last ran and is next due, and — when a run "
                    + "was held back — why (a batch already running, the main window open, or Windows reporting "
                    + "the user as busy). run_frequent and run_full start a run immediately, ignoring those "
                    + "conditions. show_window and hide_window drive the main window, which is itself one of "
                    + "the conditions, so a scheduled run can be exercised both ways; toggle_window is what a "
                    + "left click on the tray icon runs. enable_startup and "
                    + "disable_startup write or remove the HKCU Run entry that launches the app into the "
                    + "tray at logon.",
                new()
                {
                    ["action"] = Prop(
                        "string",
                        "status, run_frequent, run_full, show_window, hide_window, toggle_window, "
                            + "enable_startup or disable_startup (default status).",
                        [
                            "status",
                            "run_frequent",
                            "run_full",
                            "show_window",
                            "hide_window",
                            "toggle_window",
                            "enable_startup",
                            "disable_startup",
                        ]
                    ),
                }
            ),
            Tool(
                "config_monitor",
                "Report the config watcher state: whether it is live, which events are waiting for the quiet period, "
                    + "and for every tracked config file the content baseline the app holds and whether the file on disk "
                    + "has been changed outside the app (in which case saving it is skipped). Also lists the daily config "
                    + "backups and where they are kept.",
                new()
            )
        );

    public static JsonObject InitializeResult(
        string name,
        string title,
        string version,
        string? requestedVersion
    )
    {
        var protocol = KnownProtocolVersions.Contains(requestedVersion)
            ? requestedVersion!
            : SupportedProtocolVersion;
        return new JsonObject
        {
            ["protocolVersion"] = protocol,
            ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
            ["serverInfo"] = new JsonObject
            {
                ["name"] = name,
                ["title"] = title,
                ["version"] = version,
            },
        };
    }

    private static JsonObject Prop(string type, string description, string[]? allowed = null)
    {
        var prop = new JsonObject { ["type"] = type, ["description"] = description };
        if (allowed is { Length: > 0 })
            prop["enum"] = new JsonArray(allowed.Select(value => (JsonNode)value!).ToArray());
        return prop;
    }

    private static JsonObject Tool(
        string name,
        string description,
        JsonObject properties,
        string[]? required = null
    )
    {
        var schema = new JsonObject { ["type"] = "object", ["properties"] = properties };
        if (required is { Length: > 0 })
            schema["required"] = new JsonArray(required.Select(r => (JsonNode)r!).ToArray());

        return new JsonObject
        {
            ["name"] = name,
            ["description"] = description,
            ["inputSchema"] = schema,
        };
    }
}
