global using static SoftwareCrawler.BrowserObject;
using System.Globalization;
using CefSharp;
using CefSharp.Handler;
using CefSharp.Internals;
using CefSharp.OffScreen;
using Timer = System.Threading.Timer;

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
        WebBrowser.LifeSpanHandler = new MyLifeSpanHandler(this);
        WebBrowser.RequestHandler = new MyRequestHandler(this);
        WebBrowser.DownloadHandler = new MyDownloadHandler(this);

        await WebBrowser.WaitForInitialLoadAsync();
    }

    #region Load events

    private TaskCompletionSource<bool>? _loadStartTaskCompletionSource;
    private TaskCompletionSource<bool>? _loadEndTaskCompletionSource;

    private void WebBrowserOnFrameLoadStart(object? sender, FrameLoadStartEventArgs e)
    {
        _loadStartTaskCompletionSource?.TrySetResult(true);
    }

    private void WebBrowserOnLoadingStateChanged(object? sender, LoadingStateChangedEventArgs e)
    {
        if (!e.IsLoading)
            _loadEndTaskCompletionSource?.TrySetResult(true);
    }

    private static Task<bool> WithTimeout(Task<bool> task, TimeSpan timeout)
    {
        var result = new TaskCompletionSource<bool>(task.AsyncState);
        var timer = new Timer(
            state => ((TaskCompletionSource<bool>) state!).TrySetResult(false),
            result, timeout, TimeSpan.FromMilliseconds(-1));
        task.ContinueWith(_ =>
        {
            timer.Dispose();
            result.TrySetFromTask(task);
        }, TaskContinuationOptions.ExecuteSynchronously);
        return result.Task;
    }

    public async Task<bool> WaitForLoadStart(TimeSpan timeout)
    {
        if (_loadStartTaskCompletionSource != null)
            return await WithTimeout(_loadStartTaskCompletionSource.Task, timeout);

        return false;
    }

    public async Task<bool> WaitForLoadEnd(TimeSpan timeout)
    {
        if (_loadEndTaskCompletionSource != null)
            return await WithTimeout(_loadEndTaskCompletionSource.Task, timeout);

        return false;
    }

    #endregion

    #region Prevent popup window / get referer

    private string _referer = string.Empty;

    private class MyLifeSpanHandler : LifeSpanHandler
    {
        private readonly BrowserObject _owner;

        public MyLifeSpanHandler(BrowserObject owner)
        {
            _owner = owner;
        }

        protected override bool OnBeforePopup(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, string targetUrl, string targetFrameName, WindowOpenDisposition targetDisposition, bool userGesture, IPopupFeatures popupFeatures, IWindowInfo windowInfo, IBrowserSettings browserSettings, ref bool noJavascriptAccess, out IWebBrowser? newBrowser)
        {
            // Prevent popup windows.
            newBrowser = null;
            _owner._referer = frame.Url;
            chromiumWebBrowser.LoadUrl(targetUrl);
            return true;
        }
    }

    #endregion

    #region Get download header / set referer

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
                _owner._lastRespondTime = date;
            else
                _owner._lastRespondTime = null;

            return false;
        }

        protected override CefReturnValue OnBeforeResourceLoad(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IRequest request, IRequestCallback callback)
        {
            if (_owner._referer != string.Empty)
            {
                request.SetReferrer(_owner._referer, ReferrerPolicy.Origin);
                _owner._referer = string.Empty;
            }

            return base.OnBeforeResourceLoad(chromiumWebBrowser, browser, frame, request, callback);
        }
    }

    #endregion

    #region Download events

    public EventHandler<DownloadItem>? BeginDownloadHandler;
    public EventHandler<DownloadItem>? DownloadProgressHandler;

    private bool _hasDownloadCancelled;

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

            if (_owner._hasDownloadCancelled)
            {
                _callback?.Cancel();
                return;
            }

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
            if (_owner._hasDownloadCancelled)
            {
                callback.Cancel();
                return;
            }

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

    public void Cancel()
    {
        _loadStartTaskCompletionSource?.TrySetResult(false);
        _loadStartTaskCompletionSource = null;
        _loadEndTaskCompletionSource?.TrySetResult(false);
        _loadEndTaskCompletionSource = null;
        _downloadTaskCompletionSource?.TrySetResult(false);
        _downloadTaskCompletionSource = null;

        _hasDownloadCancelled = true;

        WebBrowser.LoadUrl("about:blank");
    }
}
