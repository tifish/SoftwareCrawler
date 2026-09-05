namespace SoftwareCrawler;

/// <summary>
/// The window the WebView2 control lives in. Two things it does that a plain Form
/// does not:
///
/// It never takes focus when shown, so a scheduled run cannot pull the caret out
/// of whatever the user is typing in.
///
/// It stays out of the taskbar. It is scaffolding for the crawl, not a window
/// anyone switches to.
///
/// Note it is parked off-screen rather than hidden while running unattended
/// (<see cref="MainForm.ApplyBrowserHostPlacement"/>): Chromium throttles timers
/// and rendering in a window it believes is not visible, which is exactly what a
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
}
