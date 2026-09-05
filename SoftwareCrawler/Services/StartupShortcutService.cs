using System.Runtime.InteropServices;
using JeekTools;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace SoftwareCrawler.Services;

/// <summary>
/// Runs the app at logon through a shortcut in the user's Startup folder.
///
/// A shortcut, not an HKCU\...\Run value: the app promises it never writes the
/// registry and that uninstalling is "delete the folder" (Requirements §4.8), and
/// a stray Run entry would outlive a deleted install. A shortcut also survives
/// being moved to another machine's Startup folder unchanged.
/// </summary>
public static class StartupShortcutService
{
    private static readonly ILogger Log = LogManager.CreateLogger(nameof(StartupShortcutService));

    /// <summary>
    /// Started this way the app goes straight to the tray. Debug builds get their
    /// own file name so several worktrees do not fight over one shortcut.
    /// </summary>
    private static string ShortcutFileName =>
        DebugInstanceContext.IsDebugBuild
            ? $"SoftwareCrawler.Debug.{DebugInstanceContext.InstanceId}.lnk"
            : "SoftwareCrawler.lnk";

    public static string ShortcutPath =>
        Path.Join(
            Environment.GetFolderPath(Environment.SpecialFolder.Startup),
            ShortcutFileName
        );

    public static bool IsEnabled => File.Exists(ShortcutPath);

    /// <summary>
    /// Brings the Startup folder in line with the setting. Returns false and fills
    /// <paramref name="error"/> on failure, so the settings dialog can say so
    /// instead of silently disagreeing with the checkbox.
    /// </summary>
    public static bool Apply(bool enabled, out string? error)
    {
        error = null;

        try
        {
            if (enabled)
                CreateShortcut();
            else if (File.Exists(ShortcutPath))
                File.Delete(ShortcutPath);

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            Log.ZLogWarning(ex, $"Failed to {(enabled ? "create" : "remove")} the startup shortcut");
            return false;
        }
    }

    private static void CreateShortcut()
    {
        var exePath = Path.Join(AppContext.BaseDirectory, "SoftwareCrawler.exe");
        if (!File.Exists(exePath))
            throw new FileNotFoundException($"Not found: {exePath}");

        var shellType =
            Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("WScript.Shell is not available");

        object? shell = null;
        object? shortcut = null;
        try
        {
            shell = Activator.CreateInstance(shellType);
            dynamic shellObject = shell!;
            shortcut = shellObject.CreateShortcut(ShortcutPath);
            dynamic shortcutObject = shortcut!;

            shortcutObject.TargetPath = exePath;
            shortcutObject.Arguments = "--tray";
            shortcutObject.WorkingDirectory = AppContext.BaseDirectory;
            shortcutObject.Description = "Software Crawler (resident)";
            shortcutObject.Save();
        }
        finally
        {
            if (shortcut is not null)
                Marshal.FinalReleaseComObject(shortcut);
            if (shell is not null)
                Marshal.FinalReleaseComObject(shell);
        }
    }
}
