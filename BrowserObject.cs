global using static SoftwareCrawler.BrowserObject;
using CefSharp;
using CefSharp.Handler;
using System.Globalization;
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

    public IWebBrowser WebBrowser = null!;

    public async Task Init(Control? parentForm = null)
    {
        if (!Cef.IsInitialized)
        {
            var cefSettings = new CefSharp.WinForms.CefSettings();
            cefSettings.CefCommandLineArgs.Add("disable-gpu", "1");
            cefSettings.CefCommandLineArgs.Add("disable-image-loading", "1");
            cefSettings.CachePath = Path.Combine(Path.GetFullPath("Cache"));
            cefSettings.PersistSessionCookies = true;
            await Cef.InitializeAsync(cefSettings);
        }

        _hasDownloadCancelled = false;

        _frameLoadEndTaskCompletionSource = null;
        _downloadTaskCompletionSource = null;

        var webBrowser = new CefSharp.WinForms.ChromiumWebBrowser("about:blank");

        if (parentForm != null)
        {
            webBrowser.Parent = parentForm;
            webBrowser.Dock = DockStyle.Fill;
            parentForm.Show();
        }

        WebBrowser = webBrowser;

        WebBrowser.FrameLoadEnd += WebBrowserOnFrameLoadEnd;
        WebBrowser.LifeSpanHandler = new MyLifeSpanHandler(this);
        WebBrowser.RequestHandler = new MyRequestHandler(this);
        WebBrowser.DownloadHandler = new MyDownloadHandler(this);

        await WebBrowser.WaitForInitialLoadAsync();
    }

    #region Load events

    private TaskCompletionSource<bool>? _frameLoadEndTaskCompletionSource;

    private void WebBrowserOnFrameLoadEnd(object? sender, FrameLoadEndEventArgs e)
    {
        if (e.Frame.IsMain && e.Url != "about:blank")
            _frameLoadEndTaskCompletionSource?.TrySetResult(true);
    }

    private static Task<bool> WithTimeout(Task<bool> task, TimeSpan timeout)
    {
        var result = new TaskCompletionSource<bool>(task.AsyncState);
        var timer = new Timer(
            state => ((TaskCompletionSource<bool>)state!).TrySetResult(false),
            result, timeout, TimeSpan.FromMilliseconds(-1));
        task.ContinueWith(_ =>
        {
            timer.Dispose();
            result.TrySetResult(task.Result);
        }, TaskContinuationOptions.ExecuteSynchronously);
        return result.Task;
    }

    public async Task<bool> WaitForMainFrameLoadEnd(TimeSpan timeout)
    {
        if (_frameLoadEndTaskCompletionSource != null)
            return await WithTimeout(_frameLoadEndTaskCompletionSource.Task, timeout);

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
            // https://obsproject.com/ won't start download if I redirect the popup window.
            // Pass referer doesn't solve the problem.
            // Can only be solve by get and open download url directly.
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

        protected override bool OnCertificateError(IWebBrowser chromiumWebBrowser, IBrowser browser, CefErrorCode errorCode, string requestUrl, ISslInfo sslInfo, IRequestCallback callback)
        {
            callback.Continue(true);
            return true;
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
        private IDownloadItemCallback? _downloadItemCallback;

        public MyDownloadHandler(BrowserObject owner)
        {
            _owner = owner;
        }

        public bool CanDownload(IWebBrowser chromiumWebBrowser, IBrowser browser, string url, string requestMethod)
        {
            return true;
        }

        public bool OnBeforeDownload(IWebBrowser chromiumWebBrowser, IBrowser browser, DownloadItem downloadItem, IBeforeDownloadCallback callback)
        {
            _latestDownloadID = downloadItem.Id;
            _suggestedFileName = downloadItem.SuggestedFileName;

            if (_owner._hasDownloadCancelled)
            {
                if (_downloadItemCallback is { IsDisposed: false })
                    _downloadItemCallback?.Cancel();
                return false;
            }

            if (_owner.BeginDownloadHandler == null)
            {
                if (_downloadItemCallback is { IsDisposed: false })
                    _downloadItemCallback.Cancel();
                return false;
            }

            downloadItem.EndTime = _owner._lastRespondTime;
            _owner.BeginDownloadHandler?.Invoke(this, downloadItem);

            using (callback)
            {
                if (downloadItem.IsCancelled)
                {
                    if (_downloadItemCallback is { IsDisposed: false })
                        _downloadItemCallback?.Cancel();
                }
                else
                    callback.Continue(downloadItem.FullPath, false);
            }

            _downloadItemCallback = null;

            return true;
        }

        public void OnDownloadUpdated(IWebBrowser chromiumWebBrowser, IBrowser browser, DownloadItem downloadItem,
            IDownloadItemCallback callback)
        {
            if (_owner._hasDownloadCancelled)
            {
                if (callback is { IsDisposed: false })
                    callback.Cancel();
                return;
            }

            // Will be called once before OnBeforeDownload, keep callback to use in OnBeforeDownload.
            if (_downloadItemCallback == null && downloadItem.Id > _latestDownloadID)
            {
                _downloadItemCallback = callback;
                return;
            }

            // If no download listener is registered then cancel the download.
            if (_owner._downloadTaskCompletionSource == null || _owner._downloadTaskCompletionSource.Task.IsCompleted)
            {
                if (callback is { IsDisposed: false })
                    callback.Cancel();
                return;
            }

            // Only keep the latest download.
            if (downloadItem.Id < _latestDownloadID)
            {
                if (callback is { IsDisposed: false })
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
            else // Download is interrupted unexpectedly
            {
                _owner._downloadTaskCompletionSource.TrySetResult(false);
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
        _frameLoadEndTaskCompletionSource?.TrySetResult(false);
        _frameLoadEndTaskCompletionSource = new TaskCompletionSource<bool>();
        _downloadTaskCompletionSource?.TrySetResult(false);
        _downloadTaskCompletionSource = new TaskCompletionSource<bool>();
    }

    public async Task Load(string url)
    {
        PrepareLoadEvents();
        await WebBrowser.LoadUrlAsync(url);
    }

    public async Task<bool> TryClick(string xpath, string frameName, int count, int interval)
    {
        var success = false;
        for (var i = 0; i < count; i++)
        {
            if (WebBrowser.CanExecuteJavascriptInMainFrame)
                if (string.IsNullOrWhiteSpace(frameName) || WebBrowser.GetBrowser().GetFrameByName(frameName) != null)
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
        xpath = xpath.Replace('\"', '\'');
        var js = $"""document.evaluate("{xpath}", document, null, XPathResult.FIRST_ORDERED_NODE_TYPE, null).singleNodeValue.click()""";
        return await EvaluateJavascript(js, frameName);
    }

    public async Task<bool> TryEvaluateJavascript(string script, string frameName = "", int count = 10, int interval = 500)
    {
        var success = false;
        for (var i = 0; i < count; i++)
        {
            if (WebBrowser.CanExecuteJavascriptInMainFrame)
                if (string.IsNullOrWhiteSpace(frameName) || WebBrowser.GetBrowser().GetFrameByName(frameName) != null)
                {
                    success = await EvaluateJavascript(script, frameName);
                    if (success)
                        break;
                }

            await Task.Delay(interval);
        }

        return success;
    }

    public string LastJavascriptError { get; private set; } = "";

    public async Task<bool> EvaluateJavascript(string script, string frameName = "")
    {
        JavascriptResponse? response = null;

        if (string.IsNullOrWhiteSpace(frameName))
            response = await WebBrowser.EvaluateScriptAsync(script);

        var frame = WebBrowser.GetBrowser().GetFrameByName(frameName);
        if (frame != null)
            response = await frame.EvaluateScriptAsync(script);

        if (response != null)
        {
            if (!response.Success)
                LastJavascriptError = response.Message;
            return response.Success;
        }

        return false;
    }

    public void Cancel()
    {
        _frameLoadEndTaskCompletionSource?.TrySetResult(false);
        _frameLoadEndTaskCompletionSource = null;
        _downloadTaskCompletionSource?.TrySetResult(false);
        _downloadTaskCompletionSource = null;

        _hasDownloadCancelled = true;

        WebBrowser.LoadUrl("about:blank");
    }
}
