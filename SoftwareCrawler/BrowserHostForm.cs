namespace SoftwareCrawler;

/// <summary>
/// The window the WebView2 control lives in. Three things it does that a plain
/// Form does not:
///
/// It never takes focus when shown, so a scheduled run cannot pull the caret out
/// of whatever the user is typing in.
///
/// It stays out of the taskbar. It is scaffolding for the crawl, not a window
/// anyone switches to.
///
/// Closing it does not destroy it. The X button would otherwise take WebView2
/// down with it and break every download for the rest of the session; instead the
/// window reports <see cref="HideRequested"/> and the main form parks it
/// off-screen (<see cref="MainForm.ApplyBrowserHostPlacement"/>).
///
/// Note it is parked off-screen rather than hidden: Chromium throttles timers and
/// rendering in a window it believes is not visible, which is exactly what a
/// page-settled check must not have happen to it.
/// </summary>
internal sealed class BrowserHostForm : Form
{
    public BrowserHostForm()
    {
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
    }

    protected override bool ShowWithoutActivation => true;

    /// <summary>Raised when the user closes the window, meaning "get it out of my sight".</summary>
    public event EventHandler? HideRequested;

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Only a deliberate close is turned into a hide. Application.Exit and a
        // Windows shutdown go through untouched, so the process still ends.
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            HideRequested?.Invoke(this, EventArgs.Empty);
        }

        base.OnFormClosing(e);
    }
}
