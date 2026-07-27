global using static SoftwareCrawler.BrowserObject;
using System.Globalization;
using System.Text.RegularExpressions;
using JeekTools;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using ZLogger;
using Timer = System.Threading.Timer;

namespace SoftwareCrawler;

public enum BrowserType
{
    OffScreen,
    WinForms,
}

public class BrowserObject
{
    private static readonly ILogger Log = LogManager.CreateLogger(nameof(BrowserObject));

    public static BrowserObject Browser { get; } = new();

    public WebView2 WebView2 = null!;

    /// <summary>The WebView2 profile folder actually in use. Diagnostics only.</summary>
    public string UserDataFolder { get; private set; } = "";

    // Track frames for frame-specific script execution
    private readonly Dictionary<string, CoreWebView2Frame> _frames = new();

    private string? _proxyServer;

    public async Task Init(Control? parentForm = null, string proxyServer = "")
    {
        _hasDownloadCancelled = false;

        _navigationCompletedTaskCompletionSource = null;
        _downloadTaskCompletionSource = null;

        _currentNavigationId = 0;
        Volatile.Write(ref _navigatedInPlaceTicks, 0);
        Volatile.Write(ref _networkQuietSinceTicks, 0);
        Volatile.Write(ref _loadEndedTicks, 0);
        _mainFrameId = "";

        _lastRespondTime = null;
        _proxyServer = proxyServer;

        var webView2 = new WebView2();

        if (parentForm != null)
        {
            webView2.Parent = parentForm;
            webView2.Dock = DockStyle.Fill;
            parentForm.Show();
        }

        WebView2 = webView2;

        // Build command line arguments
        List<string> args =
        [
            "--safebrowsing-disable-download-protection",
            "--disable-features=SafetyTipUI,SafetyCheck,InsecureDownloadWarnings,DownloadBubble,DownloadBubbleV2",
            // "--safebrowsing-disable-extension-blacklist",
            // "--no-sandbox",
            // "--disable-web-security",
            // "--allow-running-insecure-content",
            // "--disable-popup-blocking",
        ];
        if (!string.IsNullOrWhiteSpace(proxyServer))
        {
            args.Add($"--proxy-server={proxyServer}");
        }

        // Anchored to the executable, not the working directory: a scheduled task
        // starts the app in system32, which would give the nightly run a different
        // WebView2 profile - no cookies, no logins - than a manual start.
        UserDataFolder = Path.Combine(AppContext.BaseDirectory, "Cache");

        var environment = await CoreWebView2Environment.CreateAsync(
            null,
            UserDataFolder,
            new CoreWebView2EnvironmentOptions(string.Join(" ", args), "zh-CN")
        );
        await WebView2.EnsureCoreWebView2Async(environment);

        // Configure settings
        WebView2.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
        WebView2.CoreWebView2.Settings.AreDevToolsEnabled = true;

        // Setup event handlers
        WebView2.CoreWebView2.NavigationStarting += WebView2OnNavigationStarting;
        WebView2.CoreWebView2.SourceChanged += WebView2OnSourceChanged;
        WebView2.CoreWebView2.DOMContentLoaded += WebView2OnDomContentLoaded;
        WebView2.CoreWebView2.NavigationCompleted += WebView2OnNavigationCompleted;
        WebView2.CoreWebView2.DownloadStarting += WebView2OnDownloadStarting;
        WebView2.CoreWebView2.NewWindowRequested += WebView2OnNewWindowRequested;
        WebView2.CoreWebView2.FrameCreated += WebView2OnFrameCreated;

        // Setup DevTools Protocol to capture response headers
        await SetupDevToolsProtocolForResponseHeaders();

        // Setup DevTools Protocol to know when the page stopped fetching
        await SetupDevToolsProtocolForPageLifecycle();

        // Navigate to blank page
        WebView2.CoreWebView2.Navigate("about:blank");
        await Task.Delay(100); // Give it time to navigate
    }

    #region Load events

    private TaskCompletionSource<bool>? _navigationCompletedTaskCompletionSource;

    /// <summary>When the network first went quiet for this page; 0 while it is still fetching.</summary>
    private long _networkQuietSinceTicks;

    /// <summary>When the load event was raised for this page; 0 before that.</summary>
    private long _loadEndedTicks;

    /// <summary>
    /// How long 'networkAlmostIdle' has to hold before it counts as settled. That event
    /// tolerates up to two open connections, so something can still be on its way the
    /// moment it first fires.
    /// </summary>
    private static readonly TimeSpan NetworkQuietHold = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How long after the page announces itself - the load event, or a swap in place - it
    /// counts as settled. Neither announcement means the page is done: GitHub raises load
    /// about four seconds before the lazily fetched releases panel the crawl is after. The
    /// grace is what bounds the wait when the network never reports going quiet, which
    /// happens on pages holding a connection open and after in-place swaps, where Chrome
    /// does not report idle again for a navigation it does not consider new.
    /// </summary>
    private static readonly TimeSpan AnnouncedGrace = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The navigation the waits belong to. Load events name the navigation they report on,
    /// and a click that navigates away aborts whatever was in flight: without this, that
    /// abort would be read as "the page we are waiting for has loaded".
    /// </summary>
    private ulong _currentNavigationId;

    /// <summary>
    /// When the page was last replaced under us without a navigation - pushState, or a
    /// framework like GitHub's turbo swapping the document in place; 0 if that has not
    /// happened. No load event is ever raised for such a page, so this is the only
    /// announcement it gets.
    /// </summary>
    private long _navigatedInPlaceTicks;

    /// <summary>How long ago the page was swapped in place, or null if it has not been.</summary>
    public TimeSpan? NavigatedInPlaceFor => Since(ref _navigatedInPlaceTicks);

    /// <summary>
    /// The URL changed without a navigation: the page was swapped in place. Waiting for a
    /// load event past this point is waiting for something that is never coming.
    /// </summary>
    public bool HasNavigatedInPlace => NavigatedInPlaceFor is not null;

    /// <summary>How long the page has been quiet, or null while it is still fetching.</summary>
    public TimeSpan? NetworkQuietFor => Since(ref _networkQuietSinceTicks);

    /// <summary>How long ago the load event was raised, or null if it has not been.</summary>
    public TimeSpan? LoadEndedFor => Since(ref _loadEndedTicks);

    private static TimeSpan? Since(ref long ticks)
    {
        var stamp = Volatile.Read(ref ticks);
        return stamp == 0 ? null : TimeSpan.FromTicks(DateTime.UtcNow.Ticks - stamp);
    }

    /// <summary>
    /// The page has settled: nothing has been in flight for a while, or the load event
    /// came and went long enough ago. Not a precondition for acting on the page - it is
    /// the point past which waiting for the page's own scripts buys nothing.
    /// </summary>
    public bool IsPageSettled =>
        NetworkQuietFor >= NetworkQuietHold
        || LoadEndedFor >= AnnouncedGrace
        || NavigatedInPlaceFor >= AnnouncedGrace;

    private void WebView2OnNavigationStarting(
        object? sender,
        CoreWebView2NavigationStartingEventArgs e
    )
    {
        // A redirect keeps the id of the navigation it continues, so this stays put
        // across them and only moves when something genuinely new starts.
        _currentNavigationId = e.NavigationId;
        Volatile.Write(ref _navigatedInPlaceTicks, 0);
        // The document being replaced stops speaking for the page here; the loader of the
        // one taking its place is only known once it commits (Page.frameNavigated).
        _currentLoaderId = "";
        Volatile.Write(ref _networkQuietSinceTicks, 0);
        Volatile.Write(ref _loadEndedTicks, 0);
    }

    private void WebView2OnSourceChanged(object? sender, CoreWebView2SourceChangedEventArgs e)
    {
        if (e.IsNewDocument)
            return;

        // The URL changed with no navigation behind it. Restart the quiet measurement from
        // here: whatever the old page had settled into says nothing about this one, and
        // "quiet since the swap" is what tells us the new page has finished arriving.
        Volatile.Write(ref _networkQuietSinceTicks, 0);
        Volatile.Write(ref _navigatedInPlaceTicks, DateTime.UtcNow.Ticks);
        Log.ZLogDebug($"Page swapped in place: {WebView2.Source}");
    }

    /// <summary>
    /// Whether a load event belongs to the navigation being waited on. Zero means nothing
    /// has started since the wait was armed, so any event arriving is about the page we
    /// are navigating away from.
    /// </summary>
    private bool IsCurrentNavigation(ulong navigationId) =>
        _currentNavigationId != 0 && navigationId == _currentNavigationId;

    private void WebView2OnDomContentLoaded(object? sender, CoreWebView2DOMContentLoadedEventArgs e)
    {
        // The document is parsed and scriptable from here on. NavigationCompleted only
        // comes after every image, ad and tracker finished, which crawling does not need
        // to wait for - whether the click target is usable is decided by probing it.
        if (IsCurrentNavigation(e.NavigationId) && WebView2.Source.ToString() != "about:blank")
            _navigationCompletedTaskCompletionSource?.TrySetResult(true);
    }

    private void WebView2OnNavigationCompleted(
        object? sender,
        CoreWebView2NavigationCompletedEventArgs e
    )
    {
        if (!IsCurrentNavigation(e.NavigationId) || WebView2.Source.ToString() == "about:blank")
            return;

        // A failed navigation is still the end of one. Treating only success as "loaded"
        // left the waiter hanging for the whole LoadPageEndTimeout whenever a navigation
        // turned into a download or errored out.
        if (!e.IsSuccess)
            Log.ZLogDebug($"Navigation ended with {e.WebErrorStatus}: {WebView2.Source}");

        Volatile.Write(ref _loadEndedTicks, DateTime.UtcNow.Ticks);
        _navigationCompletedTaskCompletionSource?.TrySetResult(true);
    }

    private static Task<bool> WithTimeout(Task<bool> task, TimeSpan timeout)
    {
        var result = new TaskCompletionSource<bool>(task.AsyncState);
        var timer = new Timer(
            state => ((TaskCompletionSource<bool>)state!).TrySetResult(false),
            result,
            timeout,
            TimeSpan.FromMilliseconds(-1)
        );
        task.ContinueWith(
            _ =>
            {
                timer.Dispose();
                result.TrySetResult(task.Result);
            },
            TaskContinuationOptions.ExecuteSynchronously
        );
        return result.Task;
    }

    public async Task<bool> WaitForMainFrameLoadEnd(TimeSpan timeout)
    {
        if (_navigationCompletedTaskCompletionSource != null)
            return await WithTimeout(_navigationCompletedTaskCompletionSource.Task, timeout);

        return false;
    }

    #endregion

    #region Page lifecycle

    /// <summary>
    /// The frame id of the top-level document. Lifecycle events are reported per frame
    /// and a sub-frame going quiet says nothing about the page.
    /// </summary>
    private string _mainFrameId = "";

    /// <summary>
    /// The loader of the document currently in the main frame, or empty while a navigation
    /// is in flight. Lifecycle events name the document they describe, and they arrive late:
    /// about:blank's "network is idle" routinely lands after the next navigation has started,
    /// where it would otherwise be read as the new page having settled.
    /// </summary>
    private string _currentLoaderId = "";

    private async Task SetupDevToolsProtocolForPageLifecycle()
    {
        try
        {
            await WebView2.CoreWebView2.CallDevToolsProtocolMethodAsync("Page.enable", "{}");
            await WebView2.CoreWebView2.CallDevToolsProtocolMethodAsync(
                "Page.setLifecycleEventsEnabled",
                """{"enabled":true}"""
            );

            var frameTree = await WebView2.CoreWebView2.CallDevToolsProtocolMethodAsync(
                "Page.getFrameTree",
                "{}"
            );
            _mainFrameId = ReadMainFrameId(frameTree);

            WebView2
                .CoreWebView2.GetDevToolsProtocolEventReceiver("Page.frameNavigated")
                .DevToolsProtocolEventReceived += OnPageFrameNavigated;
            WebView2
                .CoreWebView2.GetDevToolsProtocolEventReceiver("Page.lifecycleEvent")
                .DevToolsProtocolEventReceived += OnPageLifecycleEvent;
        }
        catch (Exception ex)
        {
            Log.ZLogWarning($"Failed to setup DevTools Protocol for page lifecycle: {ex.Message}");
        }
    }

    private static string ReadMainFrameId(string frameTreeJson)
    {
        try
        {
            var json = System.Text.Json.JsonDocument.Parse(frameTreeJson);
            if (
                json.RootElement.TryGetProperty("frameTree", out var tree)
                && tree.TryGetProperty("frame", out var frame)
                && frame.TryGetProperty("id", out var id)
            )
                return id.GetString() ?? "";
        }
        catch (Exception ex)
        {
            Log.ZLogDebug($"Failed to read the main frame id: {ex.Message}");
        }

        return "";
    }

    private void OnPageFrameNavigated(
        object? sender,
        CoreWebView2DevToolsProtocolEventReceivedEventArgs e
    )
    {
        try
        {
            var json = System.Text.Json.JsonDocument.Parse(e.ParameterObjectAsJson);
            if (!json.RootElement.TryGetProperty("frame", out var frame))
                return;
            // No parent means this is the top-level document.
            if (frame.TryGetProperty("parentId", out _))
                return;
            if (frame.TryGetProperty("id", out var id))
                _mainFrameId = id.GetString() ?? _mainFrameId;
            // The new document is in: lifecycle events carrying this loader describe it.
            if (frame.TryGetProperty("loaderId", out var loaderId))
                _currentLoaderId = loaderId.GetString() ?? "";
        }
        catch (Exception ex)
        {
            Log.ZLogDebug($"Failed to parse Page.frameNavigated: {ex.Message}");
        }
    }

    private void OnPageLifecycleEvent(
        object? sender,
        CoreWebView2DevToolsProtocolEventReceivedEventArgs e
    )
    {
        try
        {
            var json = System.Text.Json.JsonDocument.Parse(e.ParameterObjectAsJson);
            if (!json.RootElement.TryGetProperty("name", out var name))
                return;
            if (name.GetString() != "networkAlmostIdle")
                return;
            if (
                _mainFrameId.Length > 0
                && json.RootElement.TryGetProperty("frameId", out var frameId)
                && frameId.GetString() != _mainFrameId
            )
                return;
            // Belongs to a document that has already been navigated away from, or to one
            // that has not committed yet - either way it says nothing about this page.
            if (
                _currentLoaderId.Length == 0
                || !json.RootElement.TryGetProperty("loaderId", out var eventLoaderId)
                || eventLoaderId.GetString() != _currentLoaderId
            )
                return;

            // Chrome reports this every time the page falls idle, and the first report
            // routinely lands in a lull early in the load - GitHub goes quiet for a moment
            // before fetching the panels the crawl is after. So the hold runs from the
            // latest report, which is what makes "quiet" mean "nothing since".
            Volatile.Write(ref _networkQuietSinceTicks, DateTime.UtcNow.Ticks);
        }
        catch (Exception ex)
        {
            Log.ZLogDebug($"Failed to parse Page.lifecycleEvent: {ex.Message}");
        }
    }

    #endregion

    #region Prevent popup window / get referer

    private string _referer = string.Empty;

    private void WebView2OnNewWindowRequested(
        object? sender,
        CoreWebView2NewWindowRequestedEventArgs e
    )
    {
        // Prevent popup windows and navigate to the target URL in the same window
        e.Handled = true;
        _referer = WebView2.Source.ToString();
        WebView2.CoreWebView2.Navigate(e.Uri);
    }

    #endregion

    #region Get file time

    private DateTime? _lastRespondTime = null;

    private async Task SetupDevToolsProtocolForResponseHeaders()
    {
        try
        {
            // Enable Network domain to intercept network events
            await WebView2.CoreWebView2.CallDevToolsProtocolMethodAsync("Network.enable", "{}");

            // Subscribe to Network.responseReceived event
            var receiver = WebView2.CoreWebView2.GetDevToolsProtocolEventReceiver(
                "Network.responseReceived"
            );
            receiver.DevToolsProtocolEventReceived += OnNetworkResponseReceived;
        }
        catch (Exception ex)
        {
            Log.ZLogWarning($"Failed to setup DevTools Protocol for response headers: {ex.Message}");
        }
    }

    private void OnNetworkResponseReceived(
        object? sender,
        CoreWebView2DevToolsProtocolEventReceivedEventArgs e
    )
    {
        var payload = e.ParameterObjectAsJson;
        if (string.IsNullOrEmpty(payload))
            return;

        // Offload JSON parsing to a worker thread. A busy page may produce hundreds of
        // Network.responseReceived events; parsing them on the UI thread caused jank.
        _ = Task.Run(() => ParseAndUpdateLastRespondTime(payload));
    }

    private void ParseAndUpdateLastRespondTime(string payload)
    {
        try
        {
            var json = System.Text.Json.JsonDocument.Parse(payload);

            if (!json.RootElement.TryGetProperty("response", out var response))
                return;
            if (!response.TryGetProperty("url", out var urlElement))
                return;

            var url = urlElement.GetString();
            if (string.IsNullOrEmpty(url))
                return;

            // Get headers
            if (!response.TryGetProperty("headers", out var headers))
                return;

            // Try to get Last-Modified header
            DateTime? lastModified = null;

            // Headers can be case-insensitive, check common variations
            foreach (var headerName in new[] { "Last-Modified", "last-modified", "lastModified" })
            {
                if (headers.TryGetProperty(headerName, out var lastModifiedElement))
                {
                    var lastModifiedStr = lastModifiedElement.GetString();
                    if (!string.IsNullOrEmpty(lastModifiedStr))
                    {
                        if (
                            DateTime.TryParseExact(
                                lastModifiedStr,
                                "ddd, dd MMM yyyy HH:mm:ss 'GMT'",
                                CultureInfo.InvariantCulture,
                                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                                out var parsedDate
                            )
                        )
                        {
                            lastModified = parsedDate.ToLocalTime();
                            break;
                        }
                        // Fallback to general parsing
                        else if (DateTime.TryParse(lastModifiedStr, out parsedDate))
                        {
                            lastModified = parsedDate;
                            break;
                        }
                    }
                }
            }

            // Update _lastRespondTime for the most recent response
            if (lastModified.HasValue)
            {
                _lastRespondTime = lastModified;
            }
        }
        catch (Exception ex)
        {
            // Silently ignore parsing errors to avoid spam
            Log.ZLogDebug($"Failed to parse DevTools Protocol event: {ex.Message}");
        }
    }

    #endregion

    #region Download events

    public EventHandler<DownloadItem>? BeginDownloadHandler;
    public EventHandler<DownloadItem>? DownloadProgressHandler;

    private bool _hasDownloadCancelled;
    private CoreWebView2DownloadOperation? _currentDownloadOperation;

    private void WebView2OnDownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs e)
    {
        Log.ZLogDebug($"Download starting: {e.DownloadOperation.Uri}");

        _lastProgressInvokeTime = DateTime.MinValue;

        var downloadItem = new DownloadItem
        {
            Url = e.DownloadOperation.Uri,
            SuggestedFileName = e.ResultFilePath.Split(Path.DirectorySeparatorChar).Last(),
            TotalBytes = (long)(e.DownloadOperation.TotalBytesToReceive ?? 0),
            EndTime = _lastRespondTime,
            LastReceivedBytes = 0,
            LastUpdateTime = DateTime.Now,
        };

        // Remove (n) prefix in downloaded file name.
        var fileExt = Path.GetExtension(downloadItem.SuggestedFileName);
        downloadItem.SuggestedFileName = Regex.Replace(
            downloadItem.SuggestedFileName,
            @$" \(\d+\)\{fileExt}$",
            fileExt
        );

        if (_hasDownloadCancelled)
        {
            e.Cancel = true;
            e.Handled = true; // Suppress default download UI and security warnings
            return;
        }

        if (BeginDownloadHandler == null)
        {
            e.Cancel = true;
            e.Handled = true; // Suppress default download UI and security warnings
            return;
        }

        BeginDownloadHandler?.Invoke(this, downloadItem);

        // Download directory may be changed in BeginDownloadHandler.
        e.ResultFilePath = downloadItem.DownloadedFilePath;
        e.Handled = true; // Suppress default download UI and security warnings
        _currentDownloadOperation = e.DownloadOperation;

        // Track download progress
        e.DownloadOperation.BytesReceivedChanged += (s, args) =>
            WebView2OnDownloadBytesReceivedChanged(s, args, e.DownloadOperation, downloadItem);

        e.DownloadOperation.StateChanged += (s, args) =>
            WebView2OnDownloadStateChanged(s, args, e.DownloadOperation, downloadItem);
    }

    // Throttle UI progress updates to avoid flooding the UI thread when downloading large files
    // (BytesReceivedChanged can fire dozens of times per second).
    private const int ProgressInvokeIntervalMs = 200;
    private DateTime _lastProgressInvokeTime = DateTime.MinValue;

    private void WebView2OnDownloadBytesReceivedChanged(
        object? s,
        object? args,
        CoreWebView2DownloadOperation downloadOperation,
        DownloadItem downloadItem
    )
    {
        if (_hasDownloadCancelled)
        {
            downloadOperation.Cancel();
            return;
        }

        var currentTime = DateTime.Now;
        var currentBytes = downloadOperation.BytesReceived;

        downloadItem.ReceivedBytes = currentBytes;
        downloadItem.TotalBytes = (long)(downloadOperation.TotalBytesToReceive ?? 0);
        downloadItem.PercentComplete =
            downloadItem.TotalBytes > 0
                ? (int)((double)downloadItem.ReceivedBytes / downloadItem.TotalBytes * 100)
                : 0;

        // Only treat byte-count equality as final when the total size is known.
        // If the server does not provide Content-Length, TotalBytes stays 0 and this
        // check would never match; completion is handled by the Completed state instead.
        var isFinal =
            downloadItem.TotalBytes > 0 && downloadItem.ReceivedBytes == downloadItem.TotalBytes;
        var dueForUpdate =
            isFinal
            || (currentTime - _lastProgressInvokeTime).TotalMilliseconds
                >= ProgressInvokeIntervalMs;

        if (dueForUpdate)
        {
            // Compute speed across the throttle interval for smoother readings.
            var timeDiff = (currentTime - downloadItem.LastUpdateTime).TotalSeconds;
            if (timeDiff > 0)
            {
                var bytesDiff = currentBytes - downloadItem.LastReceivedBytes;
                downloadItem.CurrentSpeed = (long)(bytesDiff / timeDiff);

                downloadItem.LastReceivedBytes = currentBytes;
                downloadItem.LastUpdateTime = currentTime;
            }

            downloadItem.RemainingTime = downloadOperation.EstimatedEndTime - currentTime;
            downloadItem.DownloadedFilePath = downloadOperation.ResultFilePath;
            _lastProgressInvokeTime = currentTime;
            DownloadProgressHandler?.Invoke(this, downloadItem);
        }

        // Edge may block downloading, so we need to check if the download is complete.
        if (isFinal)
        {
            downloadItem.IsComplete = true;
            _downloadTaskCompletionSource?.TrySetResult(true);
        }
    }

    private void WebView2OnDownloadStateChanged(
        object? s,
        object? args,
        CoreWebView2DownloadOperation downloadOperation,
        DownloadItem downloadItem
    )
    {
        Log.ZLogDebug(
            $"Download state changed to: {downloadOperation.State}, InterruptReason: {downloadOperation.InterruptReason}"
        );

        if (downloadOperation.State == CoreWebView2DownloadState.Completed)
        {
            // Reached for normal downloads. When the server does not provide Content-Length,
            // TotalBytes stays 0 and the byte-count check in BytesReceivedChanged never reaches
            // "final", so this is the only place that completes the download for such files.
            // (Edge may block downloading, in which case this state is never raised and the
            // byte-count check is the fallback.)
            downloadItem.ReceivedBytes = downloadOperation.BytesReceived;
            if (downloadItem.TotalBytes == 0)
                downloadItem.TotalBytes = downloadItem.ReceivedBytes;
            downloadItem.PercentComplete = 100;
            downloadItem.DownloadedFilePath = downloadOperation.ResultFilePath;
            DownloadProgressHandler?.Invoke(this, downloadItem);

            downloadItem.IsComplete = true;
            _downloadTaskCompletionSource?.TrySetResult(true);
        }
        else if (downloadOperation.State == CoreWebView2DownloadState.Interrupted)
        {
            downloadItem.IsCancelled = true;
            _downloadTaskCompletionSource?.TrySetResult(false);
        }
    }

    private TaskCompletionSource<bool>? _downloadTaskCompletionSource;

    public async Task<bool> WaitForDownloaded(TimeSpan timeout)
    {
        if (_downloadTaskCompletionSource != null)
            return await WithTimeout(_downloadTaskCompletionSource.Task, timeout);

        return false;
    }

    #endregion

    public void PrepareLoadEvents()
    {
        _hasDownloadCancelled = false;
        // Nothing has started yet: until it does, any load event belongs to the page
        // being left behind, not to the one this wait is for.
        _currentNavigationId = 0;
        Volatile.Write(ref _navigatedInPlaceTicks, 0);
        Volatile.Write(ref _networkQuietSinceTicks, 0);
        Volatile.Write(ref _loadEndedTicks, 0);
        _navigationCompletedTaskCompletionSource?.TrySetResult(false);
        _navigationCompletedTaskCompletionSource = new TaskCompletionSource<bool>();
        _downloadTaskCompletionSource?.TrySetResult(false);
        _downloadTaskCompletionSource = new TaskCompletionSource<bool>();
    }

    public async Task ResetToBlankPage()
    {
        // Disable beforeunload event handlers to prevent "leave site" confirmation dialog
        try
        {
            await WebView2.CoreWebView2.ExecuteScriptAsync(
                "window.onbeforeunload = null; "
                    + "document.querySelectorAll('*').forEach(el => el.onbeforeunload = null);"
            );
        }
        catch (Exception ex)
        {
            // Ignore errors if the page is not yet loaded or already navigating
            Log.ZLogDebug($"Failed to disable beforeunload: {ex.Message}");
        }

        await Load("about:blank");
    }

    public async Task Load(string url)
    {
        PrepareLoadEvents();
        WebView2.CoreWebView2.Navigate(url);
        await Task.Delay(100); // Give navigation time to start
    }

    public async Task<bool> Click(string xpath, string frameName = "")
    {
        xpath = xpath.Replace('\"', '\'');
        var js =
            $"""document.evaluate("{xpath}", document, null, XPathResult.FIRST_ORDERED_NODE_TYPE, null).singleNodeValue.click()""";
        return await EvaluateJavascript(js, frameName);
    }

    /// <summary>What a click target looks like right now.</summary>
    public enum ClickTargetState
    {
        /// <summary>Nothing matches the XPath - the page has not built it yet, if ever.</summary>
        Missing,

        /// <summary>
        /// The node is there but not actionable yet: disabled, invisible, or still a
        /// placeholder link ('#', 'javascript:void(0)') with no handler on it. Typical of
        /// a page whose scripts have not finished wiring the download button up.
        /// </summary>
        Pending,

        /// <summary>
        /// The node is actionable, but only the page's own scripts can act on it - a
        /// button, or anything else that is not a link. Clicking one before its handler
        /// is bound does nothing at all, and nothing about the node says whether it is:
        /// frameworks bind by delegation, far from the node itself. So this waits for the
        /// page to settle, where a link does not have to.
        /// </summary>
        Ready,

        /// <summary>
        /// A plain link with a real target. Following it does not depend on any script
        /// having run, so there is nothing left to wait for.
        /// </summary>
        ReadyLink,
    }

    /// <summary>
    /// Ask the page about a click target without touching it. Polling this is what
    /// replaced clicking repeatedly and hoping: one click, once the target can take it.
    /// </summary>
    public async Task<ClickTargetState> ProbeClickTarget(string xpath, string frameName = "")
    {
        var js = $$"""
            (function () {
                try {
                    var node = document.evaluate("{{xpath.Replace('"', '\'')}}", document, null, XPathResult.FIRST_ORDERED_NODE_TYPE, null).singleNodeValue;
                    if (!node)
                        return 'missing';
                    if (node.disabled === true || node.getAttribute('aria-disabled') === 'true')
                        return 'pending';
                    var style = window.getComputedStyle(node);
                    var rect = node.getBoundingClientRect();
                    if (style.display === 'none' || style.visibility === 'hidden' || (rect.width === 0 && rect.height === 0))
                        return 'pending';
                    var href = node.getAttribute('href');
                    if (href !== null) {
                        var target = href.trim().toLowerCase();
                        var placeholder = target === '' || target === '#' || target.indexOf('javascript:') === 0;
                        if (!placeholder)
                            return 'ready-link';
                        if (!node.onclick && !node.getAttribute('onclick'))
                            return 'pending';
                    }
                    return 'ready';
                } catch (e) {
                    return 'missing';
                }
            })()
            """;

        return await EvaluateJavascriptForResult(js, frameName) switch
        {
            "ready-link" => ClickTargetState.ReadyLink,
            "ready" => ClickTargetState.Ready,
            "pending" => ClickTargetState.Pending,
            _ => ClickTargetState.Missing,
        };
    }

    /// <summary>Runs a script and returns what it evaluated to, or null if it could not run.</summary>
    private async Task<string?> EvaluateJavascriptForResult(string script, string frameName)
    {
        try
        {
            string json;
            if (!string.IsNullOrWhiteSpace(frameName))
            {
                CoreWebView2Frame? frame;
                lock (_frames)
                {
                    _frames.TryGetValue(frameName, out frame);
                }

                if (frame == null)
                    return null;

                json = await frame.ExecuteScriptAsync(script);
            }
            else
            {
                json = await WebView2.CoreWebView2.ExecuteScriptAsync(script);
            }

            LastJavascriptError = "";
            return System.Text.Json.JsonDocument.Parse(json).RootElement.GetString();
        }
        catch (Exception ex)
        {
            LastJavascriptError = ex.Message;
            return null;
        }
    }

    public async Task<bool> TryEvaluateJavascript(
        string script,
        string frameName = "",
        int count = 10,
        int interval = 500
    )
    {
        var success = false;
        for (var i = 0; i < count; i++)
        {
            success = await EvaluateJavascript(script, frameName);
            if (success)
                break;

            await Task.Delay(interval);
        }

        return success;
    }

    public string LastJavascriptError { get; private set; } = "";

    public async Task<bool> EvaluateJavascript(string script, string frameName = "")
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(frameName))
            {
                // Use official CoreWebView2Frame API to execute script in frame
                return await ExecuteScriptInFrame(script, frameName);
            }

            var result = await WebView2.CoreWebView2.ExecuteScriptAsync(script);
            LastJavascriptError = "";
            return true;
        }
        catch (Exception ex)
        {
            LastJavascriptError = ex.Message;
            return false;
        }
    }

    private void WebView2OnFrameCreated(object? sender, CoreWebView2FrameCreatedEventArgs e)
    {
        var frame = e.Frame;
        lock (_frames)
        {
            _frames[frame.Name] = frame;
        }

        // Clean up when frame is destroyed
        frame.Destroyed += (s, args) =>
        {
            lock (_frames)
            {
                var keysToRemove = _frames
                    .Where(kvp => kvp.Value == frame)
                    .Select(kvp => kvp.Key)
                    .ToList();
                foreach (var key in keysToRemove)
                {
                    _frames.Remove(key);
                    Log.ZLogDebug($"Frame unregistered: {key}");
                }
            }
        };
    }

    private async Task<bool> ExecuteScriptInFrame(string script, string frameName)
    {
        // First, try to find the frame in our tracked frames
        CoreWebView2Frame? frame = null;
        lock (_frames)
        {
            _frames.TryGetValue(frameName, out frame);
        }

        if (frame == null)
            return false;

        try
        {
            // Execute script using CoreWebView2Frame API
            var result = await frame.ExecuteScriptAsync(script);
            LastJavascriptError = "";
            return true;
        }
        catch (Exception ex)
        {
            Log.ZLogWarning(
                $"Failed to execute script in tracked frame '{frameName}': {ex.Message}. Falling back to JavaScript wrapper."
            );
            return false;
        }
    }

    public void Cancel()
    {
        _navigationCompletedTaskCompletionSource?.TrySetResult(false);
        _navigationCompletedTaskCompletionSource = null;
        _downloadTaskCompletionSource?.TrySetResult(false);
        _downloadTaskCompletionSource = null;

        _hasDownloadCancelled = true;

        if (
            _currentDownloadOperation != null
            && _currentDownloadOperation.State == CoreWebView2DownloadState.InProgress
        )
        {
            _currentDownloadOperation.Cancel();
        }

        WebView2.CoreWebView2.Navigate("about:blank");
    }

    public void ShowDevTools()
    {
        WebView2.CoreWebView2.OpenDevToolsWindow();
    }

    public Task ClearCookies()
    {
        // DeleteAllCookies is an atomic, fast call. Looping over GetCookiesAsync +
        // DeleteCookie can block the UI thread when there are hundreds of cookies.
        WebView2.CoreWebView2.CookieManager.DeleteAllCookies();
        return Task.CompletedTask;
    }
}

public class DownloadItem
{
    public string Url { get; set; } = string.Empty;
    public string SuggestedFileName { get; set; } = string.Empty;
    public string DownloadedFilePath { get; set; } = string.Empty;
    public long TotalBytes { get; set; }
    public long ReceivedBytes { get; set; }
    public long CurrentSpeed { get; set; }
    public int PercentComplete { get; set; }
    public TimeSpan RemainingTime { get; set; }
    public bool IsComplete { get; set; }
    public bool IsCancelled { get; set; }
    public bool IsInProgress => !IsComplete && !IsCancelled;
    public DateTime? EndTime { get; set; }

    // For speed calculation
    internal long LastReceivedBytes { get; set; }
    internal DateTime LastUpdateTime { get; set; } = DateTime.Now;
}
