using System.Diagnostics;
using JeekTools;
using Microsoft.Extensions.Logging;
using SoftwareCrawler.Services;
using ZLogger;

namespace SoftwareCrawler;

/// <summary>
/// The resident half of the window: the tray icon, the close-to-tray behaviour and
/// the scheduler that replaced the Windows scheduled task. Kept apart from the
/// grid-editing code because it is the only part that runs with nobody watching.
/// </summary>
public partial class MainForm
{
    /// <summary>
    /// The one main window, whether or not it is on screen.
    ///
    /// Application.OpenForms cannot stand in for this: WinForms only lists a form
    /// there while it is visible, so a resident instance sitting in the tray is
    /// absent from it entirely. Anything that looks the window up — the debug
    /// channel above all — has to come through here or it will find the browser
    /// host window instead, or nothing.
    /// </summary>
    internal static MainForm? Current { get; private set; }

    private NotifyIcon? _trayIcon;
    private DownloadScheduler? _scheduler;
    private BrowserHostForm? _browserHostForm;
    private bool _residentMode;
    private bool _startHidden;
    private bool _allowClose;

    /// <summary>Where the browser window sits while nobody should see it.</summary>
    private static Point OffScreenLocation =>
        new(SystemInformation.VirtualScreen.Right + 200, SystemInformation.VirtualScreen.Bottom + 200);

    private static Point OnScreenLocation => new(100, 100);

    /// <summary>
    /// Whether the user wants to watch the crawl. Starts on for an ordinary launch,
    /// which is how recipes get debugged, and off for a tray start — nobody asked to
    /// see a browser when the app came up at logon.
    /// </summary>
    private bool _browserWanted;

    /// <summary>
    /// Puts the browser window where it currently belongs: on screen only when the
    /// user asked for it *and* the main window is up. So a background run never puts
    /// a browser on screen, and opening the main window from the tray does not drag
    /// one out with it.
    ///
    /// Moved rather than hidden on purpose: Chromium throttles a window it thinks is
    /// invisible, and the page-settled logic depends on scripts running at full speed.
    /// </summary>
    internal void ApplyBrowserHostPlacement()
    {
        if (_browserHostForm is null || _browserHostForm.IsDisposed)
            return;

        var onScreen = _browserWanted && (!_residentMode || Visible);
        _browserHostForm.Location = onScreen ? OnScreenLocation : OffScreenLocation;
    }

    /// <summary>True when the browser window is actually where the user can see it.</summary>
    internal bool IsBrowserWindowShown =>
        _browserHostForm is { IsDisposed: false } host && host.Location != OffScreenLocation;

    /// <summary>Brings the browser window on screen and in front. Safe before it exists.</summary>
    internal void ShowBrowserWindow()
    {
        if (_browserHostForm is null || _browserHostForm.IsDisposed)
            return;

        _browserWanted = true;
        ApplyBrowserHostPlacement();

        if (_browserHostForm.WindowState == FormWindowState.Minimized)
            _browserHostForm.WindowState = FormWindowState.Normal;

        // Explicitly asked for, so taking focus here is the point.
        _browserHostForm.Activate();
    }

    /// <summary>Parks the browser window off screen without tearing WebView2 down.</summary>
    internal void HideBrowserWindow()
    {
        _browserWanted = false;
        ApplyBrowserHostPlacement();
    }

    /// <summary>Exposed so the debug tools can inspect and drive the schedule.</summary>
    public DownloadScheduler? Scheduler => _scheduler;

    /// <summary>
    /// Chosen by <see cref="Program"/> before the message loop starts.
    /// Resident mode is the normal shape; a one-shot --download-all run is not.
    /// </summary>
    internal void ConfigureResidentMode(bool resident, bool startHidden)
    {
        _residentMode = resident;
        _startHidden = resident && startHidden;

        // Coming up in the tray means nobody is watching; anything else is someone
        // sitting down to work on recipes, where the browser has always been visible.
        _browserWanted = !_startHidden;
    }

    /// <summary>
    /// Keeps the window off the screen on a tray start. Application.Run shows its
    /// main form unconditionally, so suppressing the very first Show is the only
    /// way to come up hidden without a visible flash.
    /// </summary>
    protected override void SetVisibleCore(bool value)
    {
        if (_startHidden && value)
        {
            _startHidden = false;

            // Force the handle anyway. Without one the form never joins
            // Application.OpenForms, and everything that looks itself up that way —
            // the debug channel, the UI-thread invoker — finds a different window
            // or nothing at all.
            if (!IsHandleCreated)
                CreateHandle();

            Log.ZLogInformation($"Started in the notification area; the main window stays hidden");

            base.SetVisibleCore(false);
            return;
        }

        base.SetVisibleCore(value);
    }

    private void SetUpTrayIcon()
    {
        if (!_residentMode)
            return;

        var menu = new ContextMenuStrip();
        menu.Items.Add("&Show", null, (_, _) => ShowMainWindow());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Check &frequent items now", null, (_, _) => RunScheduleNow(ScheduledRunKind.Frequent));
        menu.Items.Add("Download &all now", null, (_, _) => RunScheduleNow(ScheduledRunKind.Full));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("E&xit", null, (_, _) => ExitApplication());

        _trayIcon = new NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = DebugInstanceContext.DecorateTitle("Software Crawler"),
            ContextMenuStrip = menu,
            Visible = true,
        };
        // Left click only: the right button belongs to the context menu. No
        // DoubleClick handler on purpose — the first click of a double click has
        // already toggled, and a second action would undo it in front of the user.
        _trayIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                ToggleMainWindow();
        };
    }

    private static Icon LoadTrayIcon()
    {
        try
        {
            var exePath = Path.Join(AppContext.BaseDirectory, "SoftwareCrawler.exe");
            if (File.Exists(exePath))
                return Icon.ExtractAssociatedIcon(exePath) ?? SystemIcons.Application;
        }
        catch (Exception ex)
        {
            Log.ZLogWarning(ex, $"Failed to load the tray icon; falling back to the default");
        }

        return SystemIcons.Application;
    }

    /// <summary>
    /// What a left click on the tray icon does: show the window, or put it away.
    ///
    /// Deliberately keyed on visibility alone. Raising an already-visible window
    /// instead of hiding it would need to know whether it was in front, and by the
    /// time this runs the click has already made the taskbar the foreground window
    /// — so that test reads false no matter what, and the icon would never be able
    /// to put the window away again.
    /// </summary>
    internal void ToggleMainWindow()
    {
        if (Visible)
            HideToTray();
        else
            ShowMainWindow();
    }

    internal void ShowMainWindow()
    {
        _startHidden = false;
        Show();
        if (WindowState == FormWindowState.Minimized)
            WindowState = FormWindowState.Normal;
        Activate();
        ApplyBrowserHostPlacement();
        ApplyProcessPriority(foreground: true);
    }

    internal void HideToTray()
    {
        Hide();
        ApplyBrowserHostPlacement();
        ApplyProcessPriority(foreground: false);
    }

    /// <summary>Closes for real, as opposed to the X button, which hides to the tray.</summary>
    internal void ExitApplication()
    {
        _allowClose = true;
        Application.Exit();
    }

    /// <summary>
    /// Nudges the whole process down while it runs unattended so a background crawl
    /// competes with nothing the user is doing. Note this covers the app's own
    /// threads; WebView2 runs in its own process tree and keeps its own priority.
    /// </summary>
    private static void ApplyProcessPriority(bool foreground)
    {
        try
        {
            Process.GetCurrentProcess().PriorityClass = foreground
                ? ProcessPriorityClass.Normal
                : ProcessPriorityClass.BelowNormal;
        }
        catch (Exception ex)
        {
            Log.ZLogWarning(ex, $"Failed to set the process priority");
        }
    }

    private void StartScheduler()
    {
        if (!_residentMode)
            return;

        SetUpTrayIcon();

        if (!Visible)
            ApplyProcessPriority(foreground: false);

        _scheduler = new DownloadScheduler(RunScheduledAsync, () => DownloadBatch.IsRunning);
        _scheduler.Start();
    }

    private async Task RunScheduledAsync(ScheduledRunKind kind, IReadOnlyList<SoftwareItem> items)
    {
        // The frequent sweep does not retry: another one is minutes away, and
        // hammering a site that is down is exactly what a short interval must not do.
        // A full run keeps the configured retries — it only comes round a few times a day.
        var retryCount = kind == ScheduledRunKind.Full ? Settings.DownloadRetryCount : 0;

        await RunBatchAsync(items, testOnly: false, retryCount, operation: $"Scheduled{kind}");
    }

    private void RunScheduleNow(ScheduledRunKind kind)
    {
        if (_scheduler is null)
            return;

        _ = _scheduler.RunNowAsync(kind);
    }

    /// <summary>
    /// Shows an error only when someone is there to read it. An unattended run must
    /// never put up a dialog: it would sit there until the next person logs in, and
    /// the scheduler behind it would never tick again.
    /// </summary>
    private void ReportError(string message)
    {
        if (_residentMode && !Visible)
        {
            Log.ZLogError($"{message}");
            return;
        }

        MessageBox.Show(message, "", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
