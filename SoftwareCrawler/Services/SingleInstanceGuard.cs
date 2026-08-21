using System.Diagnostics;
using System.Runtime.InteropServices;
using JeekTools;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace SoftwareCrawler.Services;

/// <summary>
/// Keeps one app per executable directory.
///
/// Two instances started from the same folder share its <c>Cache</c> WebView2 profile, and
/// WebView2 refuses a profile another process already opened with different command line
/// options. Whichever instance then had to switch <c>--proxy-server</c> could not create a
/// browser at all (0x8007139F): a nightly run failed every proxied item while the direct
/// ones, matching the other instance's browser, went through.
///
/// The claim is a lock file inside that profile folder rather than a mutex. It crosses the
/// session boundary between a desktop instance and the scheduled task without the privilege
/// a <c>Global\</c> kernel object needs, and Windows releases it when the owner dies.
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    private static readonly ILogger Log = LogManager.CreateLogger(nameof(SingleInstanceGuard));

    /// <summary>The file whose exclusive handle is the claim on this folder.</summary>
    public static string LockPath =>
        Path.Combine(AppContext.BaseDirectory, "Cache", "instance.lock");

    /// <summary>The claim this process made at startup, for diagnostics.</summary>
    public static SingleInstanceGuard? Current { get; private set; }

    private readonly FileStream? _lockFile;

    private SingleInstanceGuard(FileStream? lockFile, bool isOnlyInstance, int ownerProcessId)
    {
        _lockFile = lockFile;
        IsOnlyInstance = isOnlyInstance;
        OwnerProcessId = ownerProcessId;
    }

    /// <summary>False only when another instance is known to hold this folder.</summary>
    public bool IsOnlyInstance { get; }

    /// <summary>The instance holding the folder, or 0 when it could not be read.</summary>
    public int OwnerProcessId { get; }

    public static SingleInstanceGuard Acquire() => Current = Claim();

    private static SingleInstanceGuard Claim()
    {
        var path = LockPath;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            var lockFile = new FileStream(
                path,
                FileMode.Create,
                FileAccess.Write,
                FileShare.Read | FileShare.Delete,
                bufferSize: 1,
                FileOptions.DeleteOnClose
            );

            using (var writer = new StreamWriter(lockFile, leaveOpen: true))
                writer.Write(Environment.ProcessId);

            return new SingleInstanceGuard(lockFile, true, Environment.ProcessId);
        }
        catch (IOException)
        {
            return new SingleInstanceGuard(null, false, ReadOwnerProcessId(path));
        }
        catch (Exception ex)
        {
            // A folder we cannot write to says nothing about other instances; starting and
            // risking the profile clash beats refusing to run at all.
            Log.ZLogWarning(
                $"Cannot claim {path}, skipping the single instance check: {ex.Message}"
            );
            return new SingleInstanceGuard(null, true, 0);
        }
    }

    /// <summary>
    /// Puts the running instance's window in front, so a start that goes nowhere still looks
    /// like the app answered. Best effort: Windows may refuse the foreground to a process
    /// that is on its way out, which leaves the taskbar button flashing instead.
    /// </summary>
    public bool TryActivateOwnerWindow()
    {
        if (OwnerProcessId <= 0 || OwnerProcessId == Environment.ProcessId)
            return false;

        try
        {
            using var owner = Process.GetProcessById(OwnerProcessId);
            var window = owner.MainWindowHandle;
            if (window == IntPtr.Zero)
                return false;

            if (IsIconic(window))
                ShowWindow(window, SW_RESTORE);
            return SetForegroundWindow(window);
        }
        catch (Exception ex)
        {
            Log.ZLogDebug($"Could not activate instance {OwnerProcessId}: {ex.Message}");
            return false;
        }
    }

    public void Dispose() => _lockFile?.Dispose();

    private static int ReadOwnerProcessId(string path)
    {
        try
        {
            // The owner holds the file open with DeleteOnClose, so reading it means sharing
            // delete as well as its write access.
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete
            );
            using var reader = new StreamReader(stream);
            return int.TryParse(reader.ReadToEnd().Trim(), out var processId) ? processId : 0;
        }
        catch (Exception ex)
        {
            Log.ZLogDebug($"Could not read {path}: {ex.Message}");
            return 0;
        }
    }

    private const int SW_RESTORE = 9;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hWnd);
}
