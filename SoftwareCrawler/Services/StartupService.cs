using JeekTools;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using ZLogger;

namespace SoftwareCrawler.Services;

/// <summary>
/// Runs the app at logon through HKCU\...\Run.
///
/// This is the one thing the app writes to the registry — installing still does
/// not, and uninstalling is still "delete the folder" plus this one value. A Run
/// entry beats a Startup-folder shortcut here: no COM to create the .lnk, and
/// cleanup tools that sweep the Startup folder leave it alone.
/// </summary>
public static class StartupService
{
    private static readonly ILogger Log = LogManager.CreateLogger(nameof(StartupService));

    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>
    /// Debug builds register under their own value name so several worktrees do
    /// not overwrite each other — or a real installation.
    /// </summary>
    private static string ValueName =>
        DebugInstanceContext.IsDebugBuild
            ? $"SoftwareCrawler.Debug.{DebugInstanceContext.InstanceId}"
            : "SoftwareCrawler";

    private static string ExePath => Path.Join(AppContext.BaseDirectory, "SoftwareCrawler.exe");

    /// <summary>Started this way the app goes straight to the notification area.</summary>
    private static string Command => $"\"{ExePath}\" --tray";

    /// <summary>Where the setting actually lives, for the debug tools.</summary>
    public static string Location => $@"HKCU\{RunKeyPath}\{ValueName}";

    /// <summary>
    /// True when the Run entry points at <em>this</em> copy. An entry left by a
    /// different install answers false, so ticking the box here takes it over
    /// rather than silently disagreeing with what is registered.
    /// </summary>
    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
                return key?.GetValue(ValueName) as string == Command;
            }
            catch (Exception ex)
            {
                Log.ZLogWarning(ex, $"Failed to read the startup registration");
                return false;
            }
        }
    }

    /// <summary>
    /// Brings the registration in line with the setting. Returns false and fills
    /// <paramref name="error"/> on failure, so the settings dialog can say so
    /// instead of silently disagreeing with the checkbox.
    /// </summary>
    public static bool Apply(bool enabled, out string? error)
    {
        error = null;

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (key is null)
                throw new InvalidOperationException($@"Cannot open HKCU\{RunKeyPath}");

            if (enabled)
                key.SetValue(ValueName, Command, RegistryValueKind.String);
            else
                key.DeleteValue(ValueName, throwOnMissingValue: false);

            RemoveLegacyShortcut();
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            Log.ZLogWarning(
                ex,
                $"Failed to {(enabled ? "add" : "remove")} the startup registration"
            );
            return false;
        }
    }

    /// <summary>
    /// Clears the Startup-folder shortcut an earlier build used, so switching to
    /// the Run entry cannot leave the app starting twice.
    /// </summary>
    private static void RemoveLegacyShortcut()
    {
        try
        {
            var path = Path.Join(
                Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                DebugInstanceContext.IsDebugBuild
                    ? $"SoftwareCrawler.Debug.{DebugInstanceContext.InstanceId}.lnk"
                    : "SoftwareCrawler.lnk"
            );

            if (File.Exists(path))
            {
                File.Delete(path);
                Log.ZLogInformation($"Removed the legacy startup shortcut at {path}");
            }
        }
        catch (Exception ex)
        {
            Log.ZLogWarning(ex, $"Failed to remove the legacy startup shortcut");
        }
    }
}
