using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace SoftwareCrawler.Tests;

/// <summary>
/// A reset handshake leaves a TLS session ticket in the process. The next direct
/// download must not present it, or the same host resets the new connection too.
/// </summary>
public class TransportResumeTests
{
    [Fact]
    public void DirectDownloadDoesNotResumeTls()
    {
        using var direct = DownloadPipeline.CreateDirectDownloadHandler(null);
        Assert.False(direct.SslOptions.AllowTlsResume);
        Assert.False(direct.UseProxy);

        using var proxied = DownloadPipeline.CreateDirectDownloadHandler("127.0.0.1:1980");
        Assert.True(proxied.UseProxy);
        Assert.NotNull(proxied.Proxy);

        Assert.Null(DownloadPipeline.ProxyForTransportAttempt(1, null, "127.0.0.1:1980"));
        Assert.Equal(
            "127.0.0.1:1980",
            DownloadPipeline.ProxyForTransportAttempt(2, null, "127.0.0.1:1980")
        );
        Assert.Equal(
            "127.0.0.1:9",
            DownloadPipeline.ProxyForTransportAttempt(2, "127.0.0.1:9", "127.0.0.1:1980")
        );
        Assert.Null(DownloadPipeline.ProxyForTransportAttempt(2, null, null));
    }

    [Fact]
    public async Task DirectDownloadOmitsTheSessionTicket()
    {
        using var certificate = CreateCertificate();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var url = $"https://127.0.0.1:{port}/";

        var sawTicket = 0;
        var fullHandshakes = 0;
        using var serverStopped = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var server = Task.Run(
            () =>
                AcceptAsync(
                    listener,
                    certificate,
                    () => Interlocked.Increment(ref sawTicket),
                    () => Interlocked.Increment(ref fullHandshakes),
                    serverStopped.Token
                ),
            serverStopped.Token
        );

        try
        {
            // The second resumed handshake must show a ticket, or this test cannot see
            // the cache and a green result would prove nothing. The download handler
            // then has to connect without one.
            await GetAsync(url, allowResume: true);
            await GetAsync(url, allowResume: true);
            await GetAsync(url, allowResume: false);

            Assert.Equal(1, sawTicket);
            Assert.Equal(2, fullHandshakes);
        }
        finally
        {
            serverStopped.Cancel();
            listener.Stop();
            await server.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private static async Task GetAsync(string url, bool allowResume)
    {
        var handler = allowResume
            ? new SocketsHttpHandler()
            : DownloadPipeline.CreateDirectDownloadHandler(null);
        handler.SslOptions.RemoteCertificateValidationCallback = static (_, _, _, _) => true;
        handler.SslOptions.CertificateRevocationCheckMode = X509RevocationMode.NoCheck;
        if (allowResume)
            handler.SslOptions.AllowTlsResume = true;

        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestVersion = HttpVersion.Version11;
        client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact;
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task AcceptAsync(
        TcpListener listener,
        X509Certificate2 certificate,
        Action sawTicket,
        Action fullHandshake,
        CancellationToken cancellationToken
    )
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient tcp;
            try
            {
                tcp = await listener.AcceptTcpClientAsync(cancellationToken);
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                using (tcp)
                {
                    var inspect = new ClientHelloStream(tcp.GetStream());
                    using var ssl = new SslStream(inspect, leaveInnerStreamOpen: false);
                    await ssl.AuthenticateAsServerAsync(
                        new SslServerAuthenticationOptions
                        {
                            ServerCertificate = certificate,
                            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                            CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                        },
                        cancellationToken
                    );
                    if (inspect.OfferedSessionTicket)
                        sawTicket();
                    else
                        fullHandshake();

                    using var reader = new StreamReader(ssl, leaveOpen: true);
                    while (await reader.ReadLineAsync(cancellationToken) is { Length: > 0 }) { }

                    var body = "ok"u8.ToArray();
                    var header =
                        $"HTTP/1.1 200 OK\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n";
                    await ssl.WriteAsync(
                        System.Text.Encoding.ASCII.GetBytes(header),
                        cancellationToken
                    );
                    await ssl.WriteAsync(body, cancellationToken);
                }
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // A client that went away mid-handshake is the next iteration's problem.
            }
        }
    }

    private static X509Certificate2 CreateCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=127.0.0.1",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1
        );
        var san = new SubjectAlternativeNameBuilder();
        san.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(san.Build());
        using var issued = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(2)
        );
        return X509CertificateLoader.LoadPkcs12(
            issued.Export(X509ContentType.Pfx),
            password: null,
            X509KeyStorageFlags.Exportable
        );
    }

    /// <summary>
    /// Records whether the ClientHello offers a resumed session. The handshake
    /// bytes stay available to <see cref="SslStream"/>; only the cleartext
    /// ClientHello is inspected.
    /// </summary>
    private sealed class ClientHelloStream : Stream
    {
        private readonly Stream _inner;
        private readonly MemoryStream _seen = new();
        private bool _parsed;

        public ClientHelloStream(Stream inner) => _inner = inner;

        public bool OfferedSessionTicket { get; private set; }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => _inner.Flush();

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = _inner.Read(buffer, offset, count);
            Note(buffer.AsSpan(offset, read));
            return read;
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default
        )
        {
            var read = await _inner.ReadAsync(buffer, cancellationToken);
            Note(buffer.Span[..read]);
            return read;
        }

        public override void Write(byte[] buffer, int offset, int count) =>
            _inner.Write(buffer, offset, count);

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default
        ) => _inner.WriteAsync(buffer, cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        private void Note(ReadOnlySpan<byte> chunk)
        {
            if (_parsed || chunk.IsEmpty)
                return;

            _seen.Write(chunk);
            var bytes = _seen.GetBuffer().AsSpan(0, (int)_seen.Length);
            if (!TrySeeSessionTicket(bytes, out var offered))
                return;

            _parsed = true;
            OfferedSessionTicket = offered;
        }
    }

    /// <summary>
    /// True once <paramref name="record"/> holds a whole ClientHello record.
    /// <paramref name="offered"/> is true when that hello carries a pre-shared
    /// key (TLS 1.3 resumption) or a non-empty session id (TLS 1.2 resumption).
    /// An empty session-ticket extension only advertises support and does not count.
    /// </summary>
    private static bool TrySeeSessionTicket(ReadOnlySpan<byte> record, out bool offered)
    {
        offered = false;
        if (record.Length < 5 || record[0] != 0x16)
            return false;

        var recordLength = (record[3] << 8) | record[4];
        if (record.Length < 5 + recordLength)
            return false;

        var hello = record.Slice(5, recordLength);
        if (hello.Length < 4 || hello[0] != 0x01)
            return false;

        var index = 4 + 2 + 32;
        if (index >= hello.Length)
            return false;

        var sessionLength = hello[index];
        index += 1;
        if (index + sessionLength > hello.Length)
            return false;

        // TLS 1.2 resumption puts the session id here. A new hello uses an empty one,
        // or a 32-byte random id that TLS 1.3 sends for middleboxes and does not resume.
        var sessionId = hello.Slice(index, sessionLength);
        index += sessionLength;
        if (index + 2 > hello.Length)
            return false;

        var cipherLength = (hello[index] << 8) | hello[index + 1];
        index += 2 + cipherLength;
        if (index >= hello.Length)
            return false;

        var compressionLength = hello[index];
        index += 1 + compressionLength;
        if (index + 2 > hello.Length)
            return false;

        var extensionsLength = (hello[index] << 8) | hello[index + 1];
        index += 2;
        var extensionsEnd = index + extensionsLength;
        if (extensionsEnd > hello.Length)
            return false;

        var offeredTicket = false;
        while (index + 4 <= extensionsEnd)
        {
            var type = (hello[index] << 8) | hello[index + 1];
            var length = (hello[index + 2] << 8) | hello[index + 3];
            index += 4;
            if (index + length > extensionsEnd)
                return false;

            // pre_shared_key: the client is resuming a TLS 1.3 session.
            if (type == 41)
                offeredTicket = true;

            index += length;
        }

        // A TLS 1.3 ClientHello carries a 32-byte fake session id. Only a shorter
        // non-empty id is a TLS 1.2 resume. 32 bytes is the compatibility id.
        if (sessionLength is > 0 and < 32)
            offeredTicket = true;

        offered = offeredTicket;
        return true;
    }
}
