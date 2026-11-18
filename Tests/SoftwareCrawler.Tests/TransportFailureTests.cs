using System.Net.Http;
using System.Net.Sockets;

namespace SoftwareCrawler.Tests;

/// <summary>
/// Direct downloads fail the item when the peer resets the TLS handshake. The
/// frequent check does not retry the item, so the header request retries only
/// this kind of failure, and the logged text has to name the reset.
/// </summary>
public class TransportFailureTests
{
    [Fact]
    public void SslResetIsRetried()
    {
        var reset = new HttpRequestException(
            HttpRequestError.SecureConnectionError,
            "The SSL connection could not be established, see inner exception.",
            new IOException(
                "Unable to read data from the transport connection: An existing connection was forcibly closed by the remote host.",
                new SocketException((int)SocketError.ConnectionReset)
            )
        );

        Assert.True(DownloadPipeline.IsTransientTransportFailure(reset));
        Assert.Equal(
            "The SSL connection could not be established: An existing connection was forcibly closed by the remote host.",
            DownloadPipeline.DescribeTransportFailure(reset)
        );
    }

    [Fact]
    public void WrappedConnectionErrorIsRetried()
    {
        var dropped = new HttpRequestException(
            HttpRequestError.ConnectionError,
            "The connection was closed."
        );

        Assert.True(DownloadPipeline.IsTransientTransportFailure(new IOException("send", dropped)));
        Assert.True(
            DownloadPipeline.IsTransientTransportFailure(
                new HttpRequestException(HttpRequestError.ResponseEnded, "ended early")
            )
        );
    }

    [Fact]
    public void CancellationAndOtherHttpErrorsAreNotRetried()
    {
        Assert.False(
            DownloadPipeline.IsTransientTransportFailure(
                new TaskCanceledException("The operation was canceled.")
            )
        );
        Assert.False(
            DownloadPipeline.IsTransientTransportFailure(
                new TaskCanceledException(
                    "canceled",
                    new HttpRequestException(
                        HttpRequestError.SecureConnectionError,
                        "The SSL connection could not be established, see inner exception."
                    )
                )
            )
        );
        Assert.False(
            DownloadPipeline.IsTransientTransportFailure(
                new HttpRequestException(HttpRequestError.NameResolutionError, "no such host")
            )
        );
        Assert.Equal(
            "The operation was canceled.",
            DownloadPipeline.DescribeTransportFailure(new TaskCanceledException("The operation was canceled."))
        );
    }
}
