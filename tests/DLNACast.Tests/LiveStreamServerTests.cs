using System.Net;
using DLNACast.Core.Audio;
using DLNACast.Core.Models;
using DLNACast.Core.Streaming;

namespace DLNACast.Tests;

public sealed class LiveStreamServerTests
{
    [Fact]
    public async Task ServesWaveHeaderAndContinuousPcmToSelectedRenderer()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var frames = new PcmFrameBuffer();
        await using var server = new LiveStreamServer();
        var renderer = CreateLoopbackRenderer();
        await using var session = await server.StartSessionAsync(renderer, frames, StreamProfile.PcmWave, timeout.Token);
        frames.Write(Enumerable.Repeat((byte)0x5A, PcmFrameBuffer.BytesPerFrame).ToArray());

        using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false });
        using var response = await client.GetAsync(session.StreamUri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        var bytes = new byte[44 + PcmFrameBuffer.BytesPerFrame];
        await stream.ReadExactlyAsync(bytes, timeout.Token);

        Assert.Equal("audio/wav", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.Equal(0x5A, bytes[44]);
        Assert.True(await session.WaitForClientAsync(TimeSpan.FromMilliseconds(100), timeout.Token));
    }

    [Fact]
    public async Task LameMp3ProducesBytesOnNonSeekableNetworkStream()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        await using var frames = new PcmFrameBuffer();
        await using var server = new LiveStreamServer();
        var renderer = CreateLoopbackRenderer();
        await using var session = await server.StartSessionAsync(renderer, frames, StreamProfile.Mp3Cbr320, timeout.Token);
        for (var i = 0; i < 20; i++) frames.Write(CreateSineFrame(i));

        using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false });
        using var response = await client.GetAsync(session.StreamUri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        var bytes = new byte[32];
        await stream.ReadExactlyAsync(bytes, timeout.Token);

        Assert.Equal("audio/mpeg", response.Content.Headers.ContentType!.MediaType);
        Assert.Contains(bytes, value => value != 0);
    }

    private static RendererDevice CreateLoopbackRenderer()
    {
        var baseUri = new Uri("http://127.0.0.1:1/");
        var av = new UpnpServiceEndpoint("urn:schemas-upnp-org:service:AVTransport:1", baseUri, baseUri, baseUri);
        var connection = new UpnpServiceEndpoint("urn:schemas-upnp-org:service:ConnectionManager:1", baseUri, baseUri, baseUri);
        return new RendererDevice("uuid:test", "Fake renderer", "Tests", "Loopback", IPAddress.Loopback,
            baseUri, av, connection, null, "http-get:*:*:*");
    }

    private static byte[] CreateSineFrame(int frameIndex)
    {
        var frame = new byte[PcmFrameBuffer.BytesPerFrame];
        var samples = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, short>(frame.AsSpan());
        for (var sample = 0; sample < samples.Length / 2; sample++)
        {
            var time = (frameIndex * (PcmFrameBuffer.SampleRate * PcmFrameBuffer.FrameMilliseconds / 1000) + sample)
                       / (double)PcmFrameBuffer.SampleRate;
            var value = (short)(Math.Sin(2 * Math.PI * 440 * time) * short.MaxValue * 0.25);
            samples[sample * 2] = value;
            samples[sample * 2 + 1] = value;
        }
        return frame;
    }
}
