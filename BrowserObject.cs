global using static SoftwareCrawler.BrowserObject;
using System.Globalization;
using CefSharp;
using CefSharp.Handler;
using CefSharp.OffScreen;

namespace SoftwareCrawler;

public class BrowserObject
{
    public static BrowserObject Browser { get; } = new();

    public ChromiumWebBrowser WebBrowser = null!;

    public async Task Init()
    {
        var settings = new CefSettings();
        settings.CefCommandLineArgs.Add("disable-gpu", "1");
        settings.CefCommandLineArgs.Add("disable-image-loading", "1");
        settings.CachePath = Path.Combine(Path.GetFullPath("Cache"));
        settings.PersistSessionCookies = true;
        await Cef.InitializeAsync(settings);

        WebBrowser = new ChromiumWebBrowser("about:blank");
        WebBrowser.LoadingStateChanged += WebBrowserOnLoadingStateChanged;
        WebBrowser.FrameLoadStart += WebBrowserOnFrameLoadStart;
        WebBrowser.LifeSpanHandler = new MyLifeSpanHandler();
        WebBrowser.RequestHandler = new MyRequestHandler(this);
        WebBrowser.DownloadHandler = new MyDownloadHandler(this);

        await WebBrowser.WaitForInitialLoadAsync();
    }

    #region Load events

    private TaskCompletionSource<bool>? _loadStartTaskCompletionSource;
    private TaskCompletionSource<bool>? _loadEndTaskCompletionSource;

    private void WebBrowserOnFrameLoadStart(object? sender, FrameLoadStartEventArgs e)
    {
        if (e.Frame.IsValid && e.Frame.IsMain)
            _loadStartTaskCompletionSource?.TrySetResult(true);
    }

    private void WebBrowserOnLoadingStateChanged(object? sender, LoadingStateChangedEventArgs e)
    {
        if (!e.IsLoading)
            _loadEndTaskCompletionSource?.TrySetResult(true);
    }

    public async Task WaitForLoadStart()
    {
        if (_loadStartTaskCompletionSource != null)
            await _loadStartTaskCompletionSource.Task;
    }

    public async Task WaitForLoadEnd()
    {
        if (_loadEndTaskCompletionSource != null)
            await _loadEndTaskCompletionSource.Task;
    }

    #endregion

    #region Prevent popup window

    private class MyLifeSpanHandler : LifeSpanHandler
    {
        protected override bool OnBeforePopup(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, string targetUrl, string targetFrameName, WindowOpenDisposition targetDisposition, bool userGesture, IPopupFeatures popupFeatures, IWindowInfo windowInfo, IBrowserSettings browserSettings, ref bool noJavascriptAccess, out IWebBrowser? newBrowser)
        {
            // Prevent popup windows.
            newBrowser = null;
            chromiumWebBrowser.LoadUrl(targetUrl);
            return true;
        }
    }

    #endregion

    #region Get download header

    private DateTime? _lastRespondTime;

    private class MyRequestHandler : RequestHandler
    {
        public MyRequestHandler(BrowserObject owner)
        {
            _headersProcessingResourceRequestHandler = new HeadersProcessingResourceRequestHandler(owner);
        }

        private readonly HeadersProcessingResourceRequestHandler _headersProcessingResourceRequestHandler;

        protected override IResourceRequestHandler GetResourceRequestHandler(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IRequest request, bool isNavigation, bool isDownload, string requestInitiator, ref bool disableDefaultHandling)
        {
            return _headersProcessingResourceRequestHandler;
        }
    }

    private class HeadersProcessingResourceRequestHandler : ResourceRequestHandler
    {
        private readonly BrowserObject _owner;

        public HeadersProcessingResourceRequestHandler(BrowserObject owner)
        {
            _owner = owner;
        }

        protected override bool OnResourceResponse(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IRequest request, IResponse response)
        {
            var dateString = response.GetHeaderByName("last-modified");
            if (DateTime.TryParse(dateString, CultureInfo.InvariantCulture.DateTimeFormat, DateTimeStyles.AssumeUniversal, out var date))
            {
                _owner._lastRespondTime = date;
            }
            else
            {
                Logger.Warning($"Failed to parse last-modified header \"{dateString}\" of {request.Url}");
                _owner._lastRespondTime = null;
            }

            return false;
        }
    }

    #endregion

    #region Download events

    public EventHandler<DownloadItem>? BeginDownloadHandler;
    public EventHandler<DownloadItem>? DownloadProgressHandler;

    private class MyDownloadHandler : IDownloadHandler
    {
        private readonly BrowserObject _owner;
        private int _latestDownloadID;
        private string _suggestedFileName = string.Empty;
        private IDownloadItemCallback? _callback;

        public MyDownloadHandler(BrowserObject owner)
        {
            _owner = owner;
        }

        public void OnBeforeDownload(IWebBrowser chromiumWebBrowser, IBrowser browser, DownloadItem downloadItem,
            IBeforeDownloadCallback callback)
        {
            if (callback.IsDisposed)
                return;

            _latestDownloadID = downloadItem.Id;
            _suggestedFileName = downloadItem.SuggestedFileName;

            downloadItem.EndTime = _owner._lastRespondTime;
            _owner.BeginDownloadHandler?.Invoke(this, downloadItem);

            using (callback)
            {
                if (downloadItem.IsCancelled)
                    _callback?.Cancel();
                else
                    callback.Continue(downloadItem.FullPath, false);
            }

            _callback = null;
        }

        public void OnDownloadUpdated(IWebBrowser chromiumWebBrowser, IBrowser browser, DownloadItem downloadItem,
            IDownloadItemCallback callback)
        {
            // Will be called once before OnBeforeDownload, keep callback to use in OnBeforeDownload.
            if (_callback == null && downloadItem.Id > _latestDownloadID)
            {
                _callback = callback;
                return;
            }

            // If no download listener is registered then cancel the download.
            if (_owner._downloadTaskCompletionSource == null || _owner._downloadTaskCompletionSource.Task.IsCompleted)
            {
                callback.Cancel();
                return;
            }

            // Only keep the latest download.
            if (downloadItem.Id < _latestDownloadID)
            {
                callback.Cancel();
                return;
            }

            if (string.IsNullOrEmpty(downloadItem.SuggestedFileName))
                downloadItem.SuggestedFileName = _suggestedFileName;

            if (downloadItem.IsComplete)
            {
                _owner.DownloadProgressHandler?.Invoke(this, downloadItem);
                _owner._downloadTaskCompletionSource.TrySetResult(true);
            }
            else if (downloadItem.IsInProgress)
            {
                _owner.DownloadProgressHandler?.Invoke(this, downloadItem);
            }
            else if (downloadItem.IsCancelled)
            {
                _owner._downloadTaskCompletionSource.TrySetResult(false);
            }
            else
            {
                throw new Exception("Unknown download state");
            }
        }
    }

    private TaskCompletionSource<bool>? _downloadTaskCompletionSource;

    public async Task<bool> WaitForDownloaded()
    {
        if (_downloadTaskCompletionSource != null)
            return await _downloadTaskCompletionSource.Task;

        return false;
    }

    #endregion

    public void PrepareLoadEvents()
    {
        _loadStartTaskCompletionSource = new TaskCompletionSource<bool>();
        _loadEndTaskCompletionSource = new TaskCompletionSource<bool>();
        _downloadTaskCompletionSource = new TaskCompletionSource<bool>();
    }

    public void Load(string url)
    {
        PrepareLoadEvents();
        WebBrowser.Load(url);
    }

    public async Task<bool> TryClick(string xpath, string frameName = "", int count = 10, int interval = 500)
    {
        var success = false;
        for (var i = 0; i < count; i++)
        {
            if (WebBrowser.CanExecuteJavascriptInMainFrame)
                if (string.IsNullOrWhiteSpace(frameName) || WebBrowser.GetBrowser().GetFrame(frameName) != null)
                {
                    success = await Click(xpath, frameName);
                    if (success)
                        break;
                }

            await Task.Delay(interval);
        }

        return success;
    }

    public async Task<bool> Click(string xpath, string frameName = "")
    {
        var js = "document.evaluate(\"" + xpath + "\", document, null, XPathResult.ANY_TYPE, null).iterateNext().click()";
        return await EvaluateJavascript(js, frameName);
    }

    public async Task<bool> TryEvaluateJavascript(string script, string frameName = "", int count = 10, int interval = 500)
    {
        var success = false;
        for (var i = 0; i < count; i++)
        {
            if (WebBrowser.CanExecuteJavascriptInMainFrame)
                if (string.IsNullOrWhiteSpace(frameName) || WebBrowser.GetBrowser().GetFrame(frameName) != null)
                {
                    success = await EvaluateJavascript(script, frameName);
                    if (success)
                        break;
                }

            await Task.Delay(interval);
        }

        return success;
    }

    public async Task<bool> EvaluateJavascript(string script, string frameName = "")
    {
        if (string.IsNullOrWhiteSpace(frameName))
            return (await WebBrowser.EvaluateScriptAsync(script)).Success;

        var frame = WebBrowser.GetBrowser().GetFrame(frameName);
        if (frame != null)
            return (await frame.EvaluateScriptAsync(script)).Success;

        return false;
    }
}
