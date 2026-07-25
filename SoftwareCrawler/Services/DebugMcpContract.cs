using System.Text.Json.Nodes;

namespace SoftwareCrawler.Services;

/// <summary>The discovery file the app writes so the bridge can find this instance.</summary>
public sealed class DebugMcpDiscovery
{
    public string Url { get; set; } = "";
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
            Tool("screenshot", "Render the main form to PNG.", new()),
            Tool(
                "software_list",
                "List the loaded software items with their download status, progress, and error message.",
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
                "storage_info",
                "Report the resolved settings paths: machine config, roaming config, storage mode, and whether portable mode is forced.",
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

    private static JsonObject Prop(string type, string description) =>
        new() { ["type"] = type, ["description"] = description };

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
