using System.IO.Pipes;
using System.Text;
using System.Text.Json.Nodes;
using SoftwareCrawler.Services;

// SoftwareCrawler debug MCP stdio adapter.
//
// An agent launches this executable as an ordinary stdio MCP server; it forwards JSON-RPC to
// the running Debug app over a named pipe. There is no port, URL, or token in the client
// config, so the config never goes stale:
//
//   { "command": "cmd", "args": ["/c", ".\\bin\\ScMcp.exe"] }
//
// The adapter ships beside the app, so it derives the pipe name from its own folder and can
// only reach the instance it was built with — parallel Debug worktrees stay separate without
// anyone assigning ports.

var options = AdapterOptions.Parse(args);

using var stdin = new StreamReader(Console.OpenStandardInput(), AdapterText.Utf8);
await using var stdout = new StreamWriter(Console.OpenStandardOutput(), AdapterText.Utf8)
{
    AutoFlush = true,
};

using var connection = new PipeConnection(options);

while (await stdin.ReadLineAsync().ConfigureAwait(false) is { } line)
{
    if (string.IsNullOrWhiteSpace(line))
        continue;

    JsonNode? message;
    try
    {
        message = JsonNode.Parse(line);
    }
    catch (Exception ex)
    {
        await stdout
            .WriteLineAsync(
                AdapterText.RpcError(null, -32700, $"Parse error: {ex.Message}").ToJsonString()
            )
            .ConfigureAwait(false);
        continue;
    }

    if (message is not null)
        await HandleAsync(message).ConfigureAwait(false);
}

async Task HandleAsync(JsonNode message)
{
    var envelope = message as JsonObject;
    var method = envelope?["method"]?.GetValue<string>();
    var id = envelope?["id"]?.DeepClone();

    string? response;
    try
    {
        response = await connection
            .SendAsync(message, AdapterText.ExpectsResponse(message))
            .ConfigureAwait(false);
    }
    catch (Exception ex)
    {
        response = OfflineResponse(envelope, method, id, ex.Message)?.ToJsonString();
    }

    if (response is not null)
        await stdout.WriteLineAsync(response).ConfigureAwait(false);
}

// The app is not running. Keep the session usable instead of failing the handshake: the
// client stays connected and still sees the tool list, and only real tool calls report why
// nothing happened.
JsonNode? OfflineResponse(JsonObject? envelope, string? method, JsonNode? id, string reason) =>
    method switch
    {
        "initialize" => AdapterText.RpcResult(
            id,
            DebugMcpContract.InitializeResult(
                "software-crawler-debug",
                "SoftwareCrawler Debug Server",
                "0",
                (envelope?["params"] as JsonObject)?["protocolVersion"]?.GetValue<string>()
            )
        ),
        "ping" => AdapterText.RpcResult(id, new JsonObject()),
        "tools/list" => AdapterText.RpcResult(
            id,
            new JsonObject { ["tools"] = DebugMcpContract.BuildToolList() }
        ),
        "tools/call" => AdapterText.RpcResult(
            id,
            new JsonObject
            {
                ["content"] = new JsonArray(
                    new JsonObject
                    {
                        ["type"] = "text",
                        ["text"] =
                            $"No Debug instance is listening on {options.DescribePipe()}. "
                            + "Build and launch this worktree (Run.cmd), then retry. "
                            + $"Details: {reason}",
                    }
                ),
                ["isError"] = true,
            }
        ),
        _ when id is null => null,
        _ => AdapterText.RpcError(
            id,
            -32601,
            $"Method not available while SoftwareCrawler is closed: {method}"
        ),
    };

/// <summary>JSON-RPC helpers and the encoding shared by both ends of the adapter.</summary>
internal static class AdapterText
{
    internal static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    internal static bool ExpectsResponse(JsonNode message) =>
        message switch
        {
            JsonObject single => single["id"] is not null,
            JsonArray batch => batch.Any(item => item is JsonObject entry && entry["id"] is not null),
            _ => true,
        };

    internal static JsonObject RpcResult(JsonNode? id, JsonNode result) =>
        new()
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["result"] = result,
        };

    internal static JsonObject RpcError(JsonNode? id, int code, string message) =>
        new()
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["error"] = new JsonObject { ["code"] = code, ["message"] = message },
        };
}

/// <summary>Command line of the adapter; everything has a working default.</summary>
internal sealed record AdapterOptions(string PipeName)
{
    public string DescribePipe() => $@"\\.\pipe\{PipeName}";

    public static AdapterOptions Parse(string[] args)
    {
        string? pipe = null;
        string? instance = null;

        for (var i = 0; i < args.Length; i++)
        {
            var value = i + 1 < args.Length ? args[i + 1] : null;
            switch (args[i])
            {
                case "--pipe" when value is not null:
                    pipe = value;
                    i++;
                    break;
                case "--instance" when value is not null:
                    instance = value;
                    i++;
                    break;
            }
        }

        // No argument is the normal case: the adapter sits in the app's folder, so hashing
        // that folder yields the same instance id the app registered its pipe under.
        return new AdapterOptions(
            pipe
                ?? McpPipeNames.Debug(instance ?? McpPipeNames.InstanceId(AppContext.BaseDirectory))
        );
    }
}

/// <summary>Lazily connected, self-healing named pipe client.</summary>
internal sealed class PipeConnection(AdapterOptions options) : IDisposable
{
    private NamedPipeClientStream? _pipe;
    private StreamReader? _reader;
    private StreamWriter? _writer;

    /// <summary>
    /// Forwards one message and returns the matching response line, or null when the message
    /// was a notification. Retries once on a broken pipe so restarting the app does not end
    /// the agent's session — the thing the HTTP bridge could never do.
    /// </summary>
    public async Task<string?> SendAsync(JsonNode message, bool expectsResponse)
    {
        var payload = message.ToJsonString();
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                var (reader, writer) = await ConnectAsync().ConfigureAwait(false);
                await writer.WriteLineAsync(payload).ConfigureAwait(false);
                if (!expectsResponse)
                    return null;

                // Skip server-initiated notifications so they cannot be mistaken for the
                // reply to this request (the pipe is duplex; the app may push later).
                while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
                {
                    if (line.Length == 0)
                        continue;
                    if (JsonNode.Parse(line) is JsonObject reply && reply["id"] is null)
                        continue;
                    return line;
                }

                throw new IOException("The app closed the pipe before replying.");
            }
            catch (Exception) when (attempt == 0)
            {
                Reset();
            }
        }
    }

    private async Task<(StreamReader Reader, StreamWriter Writer)> ConnectAsync()
    {
        if (_reader is { } reader && _writer is { } writer && _pipe?.IsConnected == true)
            return (reader, writer);

        Reset();

        var pipe = new NamedPipeClientStream(
            ".",
            options.PipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous
        );
        try
        {
            await pipe.ConnectAsync(500).ConfigureAwait(false);
        }
        catch (Exception)
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        _pipe = pipe;
        _reader = new StreamReader(
            pipe,
            AdapterText.Utf8,
            detectEncodingFromByteOrderMarks: false,
            leaveOpen: true
        );
        _writer = new StreamWriter(pipe, AdapterText.Utf8, leaveOpen: true) { AutoFlush = true };
        return (_reader, _writer);
    }

    private void Reset()
    {
        try
        {
            _reader?.Dispose();
        }
        catch
        {
            // Already torn down.
        }

        try
        {
            _writer?.Dispose();
        }
        catch
        {
            // Already torn down.
        }

        try
        {
            _pipe?.Dispose();
        }
        catch
        {
            // Already torn down.
        }

        _reader = null;
        _writer = null;
        _pipe = null;
    }

    public void Dispose() => Reset();
}
