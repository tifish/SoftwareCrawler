global using static SoftwareCrawler.BrowserObject;
using System.Globalization;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Timer = System.Threading.Timer;

namespace SoftwareCrawler;

public enum BrowserType
{
    OffScreen,
    WinForms,
}

public class BrowserObject
{
    public static BrowserObject Browser { get; } = new();

    public WebView2 WebView2 = null!;

    // Track frames for frame-specific script execution
    private readonly Dictionary<string, CoreWebView2Frame> _frames = new();

    private string? _proxyServer;

    public async Task Init(Control? parentForm = null, string proxyServer = "")
    {
        _hasDownloadCancelled = false;

        _navigationCompletedTaskCompletionSource = null;
        _downloadTaskCompletionSource = null;

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
        var args = "--safebrowsing-disable-download-protection";
        if (!string.IsNullOrWhiteSpace(proxyServer))
        {
            args += $" --proxy-server={proxyServer}";
        }

        var environment = await CoreWebView2Environment.CreateAsync(
            null,
            Path.GetFullPath("Cache"),
            new CoreWebView2EnvironmentOptions(args) { Language = "zh-CN" }
        );
        await WebView2.EnsureCoreWebView2Async(environment);

        // Configure settings
        WebView2.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
        WebView2.CoreWebView2.Settings.AreDevToolsEnabled = true;

        // Setup event handlers
        WebView2.CoreWebView2.NavigationCompleted += WebView2OnNavigationCompleted;
        WebView2.CoreWebView2.DownloadStarting += WebView2OnDownloadStarting;
        WebView2.CoreWebView2.NewWindowRequested += WebView2OnNewWindowRequested;
        WebView2.CoreWebView2.FrameCreated += WebView2OnFrameCreated;

        // Setup DevTools Protocol to capture response headers
        await SetupDevToolsProtocolForResponseHeaders();

        // Navigate to blank page
        WebView2.CoreWebView2.Navigate("about:blank");
        await Task.Delay(100); // Give it time to navigate
    }

    #region Load events

    private TaskCompletionSource<bool>? _navigationCompletedTaskCompletionSource;

    private void WebView2OnNavigationCompleted(
        object? sender,
        CoreWebView2NavigationCompletedEventArgs e
    )
    {
        if (e.IsSuccess && WebView2.Source.ToString() != "about:blank")
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
            Logger.Warning($"Failed to setup DevTools Protocol for response headers: {ex.Message}");
        }
    }

    private void OnNetworkResponseReceived(
        object? sender,
        CoreWebView2DevToolsProtocolEventReceivedEventArgs e
    )
    {
        if (e.ParameterObjectAsJson == null)
            return;

        try
        {
            var json = System.Text.Json.JsonDocument.Parse(e.ParameterObjectAsJson);

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
            Logger.Debug($"Failed to parse DevTools Protocol event: {ex.Message}");
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
        var downloadItem = new DownloadItem
        {
            Id = _downloadIdCounter++,
            Url = e.DownloadOperation.Uri,
            SuggestedFileName = e.ResultFilePath.Split(Path.DirectorySeparatorChar).Last(),
            TotalBytes = (long)(e.DownloadOperation.TotalBytesToReceive ?? 0),
            EndTime = _lastRespondTime,
            LastReceivedBytes = 0,
            LastUpdateTime = DateTime.Now,
        };

        if (_hasDownloadCancelled)
        {
            e.Cancel = true;
            return;
        }

        if (BeginDownloadHandler == null)
        {
            e.Cancel = true;
            return;
        }

        BeginDownloadHandler?.Invoke(this, downloadItem);

        if (downloadItem.IsCancelled)
        {
            e.Cancel = true;
            return;
        }

        // Set download path
        e.ResultFilePath = downloadItem.FullPath;
        _currentDownloadOperation = e.DownloadOperation;

        // Track download progress
        e.DownloadOperation.BytesReceivedChanged += (s, args) =>
        {
            if (_hasDownloadCancelled)
            {
                e.DownloadOperation.Cancel();
                return;
            }

            var currentTime = DateTime.Now;
            var currentBytes = (long)e.DownloadOperation.BytesReceived;

            // Calculate download speed
            var timeDiff = (currentTime - downloadItem.LastUpdateTime).TotalSeconds;
            if (timeDiff > 0)
            {
                var bytesDiff = currentBytes - downloadItem.LastReceivedBytes;
                downloadItem.CurrentSpeed = (long)(bytesDiff / timeDiff);

                downloadItem.LastReceivedBytes = currentBytes;
                downloadItem.LastUpdateTime = currentTime;
            }

            downloadItem.ReceivedBytes = currentBytes;
            downloadItem.TotalBytes = (long)(e.DownloadOperation.TotalBytesToReceive ?? 0);
            downloadItem.PercentComplete =
                downloadItem.TotalBytes > 0
                    ? (int)((double)downloadItem.ReceivedBytes / downloadItem.TotalBytes * 100)
                    : 0;

            DownloadProgressHandler?.Invoke(this, downloadItem);
        };

        e.DownloadOperation.StateChanged += (s, args) =>
        {
            if (e.DownloadOperation.State == CoreWebView2DownloadState.Completed)
            {
                downloadItem.IsComplete = true;
                downloadItem.FullPath = e.DownloadOperation.ResultFilePath;
                DownloadProgressHandler?.Invoke(this, downloadItem);
                _downloadTaskCompletionSource?.TrySetResult(true);
            }
            else if (e.DownloadOperation.State == CoreWebView2DownloadState.Interrupted)
            {
                downloadItem.IsCancelled = true;
                _downloadTaskCompletionSource?.TrySetResult(false);
            }
        };
    }

    private int _downloadIdCounter = 1;

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
        _navigationCompletedTaskCompletionSource?.TrySetResult(false);
        _navigationCompletedTaskCompletionSource = new TaskCompletionSource<bool>();
        _downloadTaskCompletionSource?.TrySetResult(false);
        _downloadTaskCompletionSource = new TaskCompletionSource<bool>();
    }

    public async Task Load(string url)
    {
        PrepareLoadEvents();
        WebView2.CoreWebView2.Navigate(url);
        await Task.Delay(100); // Give navigation time to start
    }

    public async Task LoadUrlAsync(string url)
    {
        await Load(url);
    }

    public async Task<bool> TryClick(string xpath, string frameName, int count, int interval)
    {
        var success = false;
        for (var i = 0; i < count; i++)
        {
            success = await Click(xpath, frameName);
            if (success)
                break;

            await Task.Delay(interval);
        }

        return success;
    }

    public async Task<bool> Click(string xpath, string frameName = "")
    {
        xpath = xpath.Replace('\"', '\'');
        var js =
            $"""document.evaluate("{xpath}", document, null, XPathResult.FIRST_ORDERED_NODE_TYPE, null).singleNodeValue.click()""";
        return await EvaluateJavascript(js, frameName);
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
                    Logger.Debug($"Frame unregistered: {key}");
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
            Logger.Warning(
                $"Failed to execute script in tracked frame '{frameName}': {ex.Message}. Falling back to JavaScript wrapper."
            );
            return false;
        }
    }

    /// <summary>
    /// Get all available frame names for debugging
    /// </summary>
    public async Task<List<string>> GetAllFrameNames()
    {
        var frameNames = new List<string>();

        // Get from tracked frames
        lock (_frames)
        {
            frameNames.AddRange(_frames.Keys);
        }

        // Also try to get from JavaScript
        try
        {
            var script = """
                (function() {
                    var frames = [];

                    // Get frames from window.frames
                    for (var i = 0; i < window.frames.length; i++) {
                        if (window.frames[i].name) {
                            frames.push(window.frames[i].name);
                        }
                    }

                    // Get iframe elements
                    var iframes = document.getElementsByTagName('iframe');
                    for (var i = 0; i < iframes.length; i++) {
                        if (iframes[i].name && frames.indexOf(iframes[i].name) === -1) {
                            frames.push(iframes[i].name);
                        }
                        if (iframes[i].id && frames.indexOf(iframes[i].id) === -1) {
                            frames.push(iframes[i].id);
                        }
                    }

                    return JSON.stringify(frames);
                })()
                """;

            var result = await WebView2.CoreWebView2.ExecuteScriptAsync(script);

            // Parse JSON array result
            var jsFrameNames = Newtonsoft.Json.JsonConvert.DeserializeObject<List<string>>(result);
            if (jsFrameNames != null)
            {
                foreach (var name in jsFrameNames)
                {
                    if (!frameNames.Contains(name))
                    {
                        frameNames.Add(name);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to get frame names from JavaScript: {ex.Message}");
        }

        return frameNames;
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

    public async Task ClearCookies()
    {
        var cookieManager = WebView2.CoreWebView2.CookieManager;
        var cookies = await cookieManager.GetCookiesAsync(null);
        foreach (var cookie in cookies)
        {
            cookieManager.DeleteCookie(cookie);
        }
    }
}

public class DownloadItem
{
    public int Id { get; set; }
    public string Url { get; set; } = string.Empty;
    public string SuggestedFileName { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public long TotalBytes { get; set; }
    public long ReceivedBytes { get; set; }
    public long CurrentSpeed { get; set; }
    public int PercentComplete { get; set; }
    public bool IsComplete { get; set; }
    public bool IsCancelled { get; set; }
    public bool IsInProgress => !IsComplete && !IsCancelled;
    public DateTime? EndTime { get; set; }

    // For speed calculation
    internal long LastReceivedBytes { get; set; }
    internal DateTime LastUpdateTime { get; set; } = DateTime.Now;
}
