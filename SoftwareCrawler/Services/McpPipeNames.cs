using System.Security.Cryptography;
using System.Text;

namespace SoftwareCrawler.Services;

/// <summary>
/// Named-pipe naming convention shared by the app and the <c>ScMcp</c> stdio adapter.
/// The adapter links this file instead of copying the rule, so the two ends cannot drift.
///
/// A pipe replaces the loopback HTTP endpoint: there is no port to allocate, so the name is
/// stable across runs and a client config never goes stale, and access is limited by the
/// pipe ACL rather than by a secret in a URL. Debug builds append the instance id derived
/// from the executable directory, so parallel worktrees never answer for each other.
/// </summary>
public static class McpPipeNames
{
    /// <summary>Debug surface: object graph, control tree, probes. Debug builds only.</summary>
    public const string DebugBase = "SoftwareCrawler.Mcp.Debug";

    /// <summary>
    /// Stable 12-hex identity of an installation, hashed from its executable directory.
    /// The adapter sits in the same folder as the app, so it derives the same value without
    /// being told which instance to talk to.
    /// </summary>
    public static string InstanceId(string executableDirectory) =>
        Convert
            .ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(executableDirectory))))[
                ..12
            ]
            .ToLowerInvariant();

    /// <summary>Debug pipe name; a null or "release" instance id gives the bare name.</summary>
    public static string Debug(string? instanceId) =>
        string.IsNullOrWhiteSpace(instanceId) || instanceId == "release"
            ? DebugBase
            : $"{DebugBase}.{instanceId.Trim()}";

    private static string Normalize(string path) =>
        Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToUpperInvariant();
}
