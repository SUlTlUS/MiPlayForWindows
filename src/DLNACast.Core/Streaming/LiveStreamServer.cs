using System.Net;
using System.Net.Sockets;
using System.Text;
using DLNACast.Core.Abstractions;
using DLNACast.Core.Audio;
using DLNACast.Core.Models;

namespace DLNACast.Core.Streaming;

public sealed class LiveStreamServer : ILiveStreamServer
{
    public const int FirstPort = 49_555;
    public const int LastPort = 49_565;

    private readonly SemaphoreSlim _sessionLock = new(1, 1);
    private LiveStreamSession? _activeSession;

    public async Task<LiveStreamSession> StartSessionAsync(
        RendererDevice renderer,
        PcmFrameBuffer frames,
        StreamProfile profile,
        CancellationToken cancellationToken)
    {
        await _sessionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_activeSession is not null)
            {
                await _activeSession.DisposeAsync().ConfigureAwait(false);
            }

            var localAddress = ResolveRouteAddress(renderer.Address);
            var (listener, port) = BindFirstAvailable(localAddress);
            var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var token = Convert.ToHexString(Guid.NewGuid().ToByteArray()).ToLowerInvariant();
            var extension = profile == StreamProfile.PcmWave ? "wav" : "mp3";
            var uri = new UriBuilder(Uri.UriSchemeHttp, localAddress.ToString(), port, $"stream/{token}.{extension}").Uri;

            LiveStreamSession? session = null;
            Task? acceptLoop = null;
            session = new LiveStreamSession(uri, profile, lifetime, async () =>
            {
                lifetime.Cancel();
                listener.Stop();
                if (acceptLoop is not null)
                {
                    try { await acceptLoop.ConfigureAwait(false); }
                    catch (OperationCanceledException) { }
                    catch (ObjectDisposedException) { }
                    catch (SocketException) when (lifetime.IsCancellationRequested) { }
                }
                lifetime.Dispose();
                if (ReferenceEquals(_activeSession, session))
                {
                    _activeSession = null;
                }
            });

            _activeSession = session;
            acceptLoop = AcceptLoopAsync(listener, renderer.Address, frames, session, lifetime.Token);
            return session;
        }
        finally
        {
            _sessionLock.Release();
        }
    }

    private static IPAddress ResolveRouteAddress(IPAddress destination)
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Connect(new IPEndPoint(destination, 9));
        return ((IPEndPoint)socket.LocalEndPoint!).Address;
    }

    private static (TcpListener Listener, int Port) BindFirstAvailable(IPAddress address)
    {
        for (var port = FirstPort; port <= LastPort; port++)
        {
            var listener = new TcpListener(address, port);
            try
            {
                listener.Start(4);
                return (listener, port);
            }
            catch (SocketException)
            {
                listener.Stop();
            }
        }

        throw new InvalidOperationException($"端口 {FirstPort}–{LastPort} 均被占用。");
    }

    private static async Task AcceptLoopAsync(
        TcpListener listener,
        IPAddress rendererAddress,
        PcmFrameBuffer frames,
        LiveStreamSession session,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            _ = HandleClientAsync(client, rendererAddress, frames, session, cancellationToken);
        }
    }

    private static async Task HandleClientAsync(
        TcpClient client,
        IPAddress rendererAddress,
        PcmFrameBuffer frames,
        LiveStreamSession session,
        CancellationToken cancellationToken)
    {
        using (client)
        {
            client.NoDelay = true;
            client.SendBufferSize = 16 * 1024;
            var remote = (IPEndPoint?)client.Client.RemoteEndPoint;
            if (remote is null || !remote.Address.Equals(rendererAddress))
            {
                await WriteSimpleResponseAsync(client.GetStream(), "403 Forbidden", cancellationToken).ConfigureAwait(false);
                return;
            }

            var stream = client.GetStream();
            var request = await ReadHeadersAsync(stream, cancellationToken).ConfigureAwait(false);
            if (request is null)
            {
                return;
            }

            var requestLine = request.Split("\r\n", 2, StringSplitOptions.None)[0];
            var method = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToUpperInvariant();
            if (method is not ("HEAD" or "GET"))
            {
                await WriteSimpleResponseAsync(stream, "405 Method Not Allowed", cancellationToken).ConfigureAwait(false);
                return;
            }

            var contentType = session.Profile == StreamProfile.PcmWave ? "audio/wav" : "audio/mpeg";
            var protocolInfo = session.Profile == StreamProfile.PcmWave
                ? "DLNA.ORG_OP=00;DLNA.ORG_CI=0"
                : "DLNA.ORG_OP=00;DLNA.ORG_CI=0";
            var response = new StringBuilder()
                .Append("HTTP/1.1 200 OK\r\n")
                .Append("Content-Type: ").Append(contentType).Append("\r\n")
                .Append("Connection: close\r\n")
                .Append("transferMode.dlna.org: Streaming\r\n")
                .Append("contentFeatures.dlna.org: ").Append(protocolInfo).Append("\r\n")
                .Append("Cache-Control: no-store, no-cache\r\n\r\n")
                .ToString();
            await stream.WriteAsync(Encoding.ASCII.GetBytes(response), cancellationToken).ConfigureAwait(false);

            if (method == "HEAD")
            {
                return;
            }

            session.MarkClientConnected();
            frames.TrimToLatest(PcmFrameBuffer.TargetBufferFrames);
            await frames.PrepareForPlaybackAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            var connectedAt = DateTimeOffset.UtcNow;
            try
            {
                if (session.Profile == StreamProfile.PcmWave)
                {
                    await stream.WriteAsync(WaveStreamHeader.CreateIndefinitePcmHeader(), cancellationToken).ConfigureAwait(false);
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        var frame = await frames.ReadFrameOrSilenceAsync(cancellationToken).ConfigureAwait(false);
                        await stream.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
                    }
                }
                else
                {
                    await using var encoder = new Mp3StreamingEncoder(frames, cancellationToken);
                    await encoder.EncodeToAsync(stream).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
            {
                if (DateTimeOffset.UtcNow - connectedAt < TimeSpan.FromSeconds(2))
                {
                    session.EarlyDisconnects++;
                }
            }
        }
    }

    private static async Task<string?> ReadHeadersAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[1024];
        using var collected = new MemoryStream();
        while (collected.Length < 16 * 1024)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return null;
            }

            collected.Write(buffer, 0, read);
            var span = collected.GetBuffer().AsSpan(0, (int)collected.Length);
            if (span.IndexOf("\r\n\r\n"u8) >= 0)
            {
                return Encoding.ASCII.GetString(span);
            }
        }

        return null;
    }

    private static Task WriteSimpleResponseAsync(NetworkStream stream, string status, CancellationToken cancellationToken)
    {
        var response = Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nConnection: close\r\nContent-Length: 0\r\n\r\n");
        return stream.WriteAsync(response, cancellationToken).AsTask();
    }

    public async ValueTask DisposeAsync()
    {
        await _sessionLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_activeSession is not null)
            {
                await _activeSession.DisposeAsync().ConfigureAwait(false);
                _activeSession = null;
            }
        }
        finally
        {
            _sessionLock.Release();
            _sessionLock.Dispose();
        }
    }
}
