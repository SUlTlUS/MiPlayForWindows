using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using DLNACast.Core.Abstractions;
using DLNACast.Core.MiPlay;
using DLNACast.Core.Models;

namespace DLNACast.Tests;

public sealed class MiPlayRuntimeControlLoopTests
{
    [Fact]
    public async Task SharedLoopKeepsSendingAfterMissingAckAndPreservesFragmentedLateReplies()
    {
        await using var harness = await Harness.StartAsync(tabletHeartbeat: true);
        var firstHeartbeat = await harness.Read();
        Assert.Equal(MiPlayProtocolConstants.HeartbeatCommand, firstHeartbeat.Command);
        Assert.Equal((ushort)16, firstHeartbeat.Sequence);

        // No heartbeat ACK: volume must still be sent and acknowledged now.
        var change = harness.ChangeVolume(27);
        var volume = await harness.Read();
        Assert.Equal(MiPlayProtocolConstants.SetVolumeCommand, volume.Command);
        Assert.Equal((ushort)17, volume.Sequence);
        for (ushort index = 0; index < 32; index++)
        {
            await harness.Send(MiPlayProtocolConstants.NotifyCommand, index, [1]);
        }
        await harness.Send(MiPlayProtocolConstants.SetVolumeAcknowledgementCommand, volume.Sequence, volume.Payload);
        await change.WaitAsync(harness.Token);
        Assert.Equal(27, harness.Runner.ReceiverVolume);

        // Leave an old ACK half-read across the previous three-second limit.
        // The same persistent reader must finish it, without losing framing.
        var oldAck = MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.HeartbeatAcknowledgementCommand, firstHeartbeat.Sequence, []);
        await harness.PeerStream.WriteAsync(oldAck.AsMemory(0, 4), harness.Token);
        var secondHeartbeat = await harness.Read();
        Assert.Equal(MiPlayProtocolConstants.HeartbeatCommand, secondHeartbeat.Command);
        Assert.Equal((ushort)18, secondHeartbeat.Sequence);
        Assert.False(harness.Loop.IsCompleted);
        await harness.PeerStream.WriteAsync(oldAck.AsMemory(4), harness.Token);
        await harness.Send(MiPlayProtocolConstants.HeartbeatCommand, 0xabcd);
        var reply = await harness.Read();
        Assert.Equal(MiPlayProtocolConstants.HeartbeatAcknowledgementCommand, reply.Command);
        Assert.Equal((ushort)0xabcd, reply.Sequence);
        Assert.Empty(reply.Payload);

        var nextChange = harness.ChangeVolume(28);
        var nextVolume = await harness.Read();
        Assert.Equal((ushort)19, nextVolume.Sequence);
        await harness.Send(MiPlayProtocolConstants.HeartbeatAcknowledgementCommand, secondHeartbeat.Sequence);
        await harness.Send(MiPlayProtocolConstants.SetVolumeAcknowledgementCommand, nextVolume.Sequence, nextVolume.Payload);
        await nextChange.WaitAsync(harness.Token);
        Assert.Equal(28, harness.Runner.ReceiverVolume);
    }

    [Fact]
    public async Task LegacySingleSpeakerStillRequiresTheIndividualHeartbeatAck()
    {
        await using var harness = await Harness.StartAsync(tabletHeartbeat: false);
        var heartbeat = await harness.Read();
        Assert.Equal(MiPlayProtocolConstants.HeartbeatCommand, heartbeat.Command);
        var exception = await Assert.ThrowsAsync<TimeoutException>(() => harness.Loop.WaitAsync(harness.Token));
        Assert.Contains("0x001A sequence 0x0010 was not acknowledged", exception.Message);
    }

    [Fact]
    public async Task SocketClosureWhileTheWriterIsIdleIsObservedByThePersistentReader()
    {
        await using var harness = await Harness.StartAsync(tabletHeartbeat: true);
        await harness.Read();
        harness.Peer.Dispose();
        await Assert.ThrowsAsync<EndOfStreamException>(() => harness.Loop.WaitAsync(harness.Token));
    }

    [Fact]
    public async Task RealFifteenSecondInactivityWindowStillEndsAnUnresponsiveConnection()
    {
        await using var harness = await Harness.StartAsync(tabletHeartbeat: true);
        var started = Stopwatch.GetTimestamp();
        await harness.Read();
        var exception = await Assert.ThrowsAsync<TimeoutException>(() => harness.Loop.WaitAsync(harness.Token));
        Assert.Contains("fifteen seconds", exception.Message);
        Assert.True(Stopwatch.GetElapsedTime(started) >= TimeSpan.FromSeconds(14));
    }

    private sealed class Harness : IAsyncDisposable
    {
        public CancellationTokenSource Lifetime { get; } = new(TimeSpan.FromSeconds(25));
        public CancellationToken Token => Lifetime.Token;
        public TcpClient Source { get; }
        public TcpClient Peer { get; }
        public NetworkStream PeerStream => Peer.GetStream();
        public MiPlayLegacySystemAudioSessionRunner Runner { get; } = new(new UnusedAudioCatalog());
        public Task Loop { get; }
        private readonly object volume;

        private Harness(TcpClient source, TcpClient peer, bool tabletHeartbeat)
        {
            Source = source;
            Peer = peer;
            var runnerType = typeof(MiPlayLegacySystemAudioSessionRunner);
            object CreateNested(string name) => Activator.CreateInstance(
                runnerType.GetNestedType(name, BindingFlags.NonPublic)!, nonPublic: true)!;
            volume = CreateNested("ActiveVolumeControl");
            var playback = CreateNested("ActiveSourcePlaybackControl");
            var timeline = CreateNested("ActiveMediaTimeline");
            var run = runnerType.GetMethod("RunRuntimeControlsAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Loop = (Task)run.Invoke(Runner,
                [source.GetStream(), Stopwatch.GetTimestamp() - 5 * Stopwatch.Frequency, volume, playback,
                    new MiPlayRuntimeTimeOffsetControl(), timeline, Token, tabletHeartbeat,
                    (Action<string>)(_ => { }), null, null, null, null, null])!;
        }

        public static async Task<Harness> StartAsync(bool tabletHeartbeat)
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var peer = new TcpClient { NoDelay = true };
            await peer.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
            var source = await listener.AcceptTcpClientAsync();
            source.NoDelay = true;
            return new(source, peer, tabletHeartbeat);
        }

        public Task ChangeVolume(int value) => (Task)volume.GetType().GetMethod("EnqueueAsync")!
            .Invoke(volume, [value, Token])!;

        public async Task<MiPlayCommandFrame> Read()
        {
            var header = new byte[9];
            await PeerStream.ReadExactlyAsync(header, Token);
            var length = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(5, 4));
            Assert.InRange(length, 0u, 4096u);
            var frameBytes = new byte[header.Length + length];
            header.CopyTo(frameBytes, 0);
            await PeerStream.ReadExactlyAsync(frameBytes.AsMemory(9), Token);
            Assert.True(MiPlayCommandFrameCodec.TryDecode(frameBytes, out var frame, out var consumed));
            Assert.Equal(frameBytes.Length, consumed);
            return Assert.IsType<MiPlayCommandFrame>(frame);
        }

        public Task Send(ushort command, ushort sequence, byte[]? payload = null) =>
            PeerStream.WriteAsync(MiPlayCommandFrameCodec.Encode(command, sequence, payload ?? []), Token).AsTask();

        public async ValueTask DisposeAsync()
        {
            Lifetime.Cancel();
            try { await Loop; }
            catch { /* Assertions above own the expected failure. Always drain the loop before closing sockets. */ }
            Source.Dispose();
            Peer.Dispose();
            Lifetime.Dispose();
        }
    }

    private sealed class UnusedAudioCatalog : IAudioSourceCatalog
    {
        public IReadOnlyList<AudioSourceItem> GetOutputDevices() => [];
        public IReadOnlyList<AudioSourceItem> GetCandidateProcesses() => [];
        public IAudioCaptureSource CreateCapture(CaptureSelection selection) => throw new NotSupportedException();
    }
}
