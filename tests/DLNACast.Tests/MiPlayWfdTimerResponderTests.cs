using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayWfdTimerResponderTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(39)]
    [InlineData(41)]
    [InlineData(512)]
    [InlineData(65_507)]
    public async Task SharedResponderAnswersEveryPositiveDatagramLikeNative(int length)
    {
        await using var harness = new Harness(tabletBehavior: true);
        await harness.CheckValidExchange(1);
        for (uint sequence = 2; sequence <= 5; sequence++)
        {
            var request = Enumerable.Range(1, length).Select(value => (byte)value).ToArray();
            await harness.Receiver.SendAsync(request, harness.Endpoint, harness.Token);
            var result = await harness.Receiver.ReceiveAsync(harness.Token);
            Assert.Equal(40, result.Buffer.Length);
            Assert.Equal(request[..Math.Min(length, 16)], result.Buffer[..Math.Min(length, 16)]);
        }
        await harness.CheckValidExchange(6);
        Assert.False(harness.Responder.IsCompleted);
    }

    [Fact]
    public async Task SharedResponderIgnoresZeroLengthDatagramAndKeepsSyncing()
    {
        await using var harness = new Harness(tabletBehavior: true);
        await harness.Receiver.SendAsync(ReadOnlyMemory<byte>.Empty, harness.Endpoint, harness.Token);
        await harness.CheckValidExchange(1);
        Assert.False(harness.Responder.IsCompleted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WrongSourceAddressNeverGetsAResponse(bool tabletBehavior)
    {
        await using var harness = new Harness(tabletBehavior);
        using var foreign = new UdpClient(new IPEndPoint(IPAddress.Parse("127.0.0.2"), 0));
        await harness.CheckValidExchange(1);
        await foreign.SendAsync(new byte[40], harness.Endpoint, harness.Token);
        using var noReply = CancellationTokenSource.CreateLinkedTokenSource(harness.Token);
        noReply.CancelAfter(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await foreign.ReceiveAsync(noReply.Token));

        if (tabletBehavior)
        {
            await harness.CheckValidExchange(2);
            Assert.False(harness.Responder.IsCompleted);
        }
        else
        {
            var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Responder.WaitAsync(harness.Token));
            Assert.Contains("unexpected peer or packet", failure.Message);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(39)]
    [InlineData(41)]
    public async Task LegacySingleSpeakerStillFailsOnAnUnexpectedLength(int length)
    {
        await using var harness = new Harness(tabletBehavior: false);
        await harness.Receiver.SendAsync(new byte[length], harness.Endpoint, harness.Token);
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Responder.WaitAsync(harness.Token));
        Assert.Contains("unexpected peer or packet", failure.Message);
    }

    [Fact]
    public async Task MixedPacketsDoNotEndEitherSharedClockResponderOrPinTheReceiverPort()
    {
        await using var first = new Harness(tabletBehavior: true);
        await using var second = new Harness(tabletBehavior: true);
        using var changedPort = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        for (uint sequence = 1; sequence <= 32; sequence++)
        {
            var probeLength = sequence % 2 == 0 ? 0 : 41;
            await first.Receiver.SendAsync(new byte[probeLength], first.Endpoint, first.Token);
            if (probeLength > 0)
            {
                var probeResponse = await first.Receiver.ReceiveAsync(first.Token);
                Assert.Equal(40, probeResponse.Buffer.Length);
            }
            await second.CheckValidExchange(sequence);
            await first.CheckValidExchange(sequence, sequence % 2 == 0 ? changedPort : null);
            MiPlayLegacySystemAudioSessionRunner.ThrowIfBackgroundFailed(first.Responder, second.Responder);
        }
        Assert.False(first.Responder.IsCompleted);
        Assert.False(second.Responder.IsCompleted);
    }

    [Fact]
    public async Task CancellationStillStopsTheResponderWithInvalidDatagramsQueued()
    {
        await using var harness = new Harness(tabletBehavior: true);
        for (var index = 0; index < 64; index++)
        {
            await harness.Receiver.SendAsync(new byte[1], harness.Endpoint, harness.Token);
        }
        harness.Lifetime.Cancel();
        try
        {
            await harness.Responder.WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch (OperationCanceledException) when (harness.Lifetime.IsCancellationRequested) { }
        Assert.True(harness.Responder.IsCanceled || harness.Responder.IsCompletedSuccessfully);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClosingTheSocketIsNotMistakenForADiscardableDatagram(bool tabletBehavior)
    {
        await using var harness = new Harness(tabletBehavior);
        await harness.CheckValidExchange(1);
        harness.Source.Dispose();
        var failure = await Record.ExceptionAsync(() => harness.Responder.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.NotNull(failure);
        Assert.IsNotType<TimeoutException>(failure);
    }

    [Fact]
    public void SharedResponderIgnoresOnlyAnUncancelledUdpSendFailure()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var socketFailure = new SocketException((int)SocketError.NetworkUnreachable);

        Assert.True(MiPlayLegacySystemAudioSessionRunner.ShouldIgnoreTimerSendFailure(
            socketFailure,
            useTabletTimerBehavior: true,
            CancellationToken.None));
        Assert.False(MiPlayLegacySystemAudioSessionRunner.ShouldIgnoreTimerSendFailure(
            socketFailure,
            useTabletTimerBehavior: false,
            CancellationToken.None));
        Assert.False(MiPlayLegacySystemAudioSessionRunner.ShouldIgnoreTimerSendFailure(
            socketFailure,
            useTabletTimerBehavior: true,
            cancelled.Token));
        Assert.False(MiPlayLegacySystemAudioSessionRunner.ShouldIgnoreTimerSendFailure(
            new IOException("not a UDP socket failure"),
            useTabletTimerBehavior: true,
            CancellationToken.None));
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly bool tabletBehavior;
        public CancellationTokenSource Lifetime { get; } = new(TimeSpan.FromSeconds(5));
        public CancellationToken Token => Lifetime.Token;
        public UdpClient Source { get; } = new(new IPEndPoint(IPAddress.Loopback, 0));
        public UdpClient Receiver { get; } = new(new IPEndPoint(IPAddress.Loopback, 0));
        public IPEndPoint Endpoint { get; }
        public Task Responder { get; }

        public Harness(bool tabletBehavior)
        {
            this.tabletBehavior = tabletBehavior;
            Endpoint = (IPEndPoint)Source.Client.LocalEndPoint!;
            var run = typeof(MiPlayLegacySystemAudioSessionRunner).GetMethod(
                "RunTimerResponderAsync", BindingFlags.Static | BindingFlags.NonPublic)!;
            Responder = (Task)run.Invoke(null, [Source, IPAddress.Loopback, Token, tabletBehavior])!;
        }

        public async Task CheckValidExchange(uint sequence, UdpClient? changedPort = null)
        {
            var receiver = changedPort ?? Receiver;
            var request = Enumerable.Range(1, 40).Select(value => (byte)value).ToArray();
            BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(32), sequence);
            var before = Microseconds();
            await receiver.SendAsync(request, Endpoint, Token);
            var result = await receiver.ReceiveAsync(Token);
            var after = Microseconds();
            Assert.Equal(Endpoint, result.RemoteEndPoint);
            Assert.Equal(40, result.Buffer.Length);
            Assert.Equal(request[..16], result.Buffer[..16]);
            Assert.Equal(request[32..], result.Buffer[32..]);
            var received = BinaryPrimitives.ReadUInt64LittleEndian(result.Buffer.AsSpan(tabletBehavior ? 24 : 16, 8));
            var sent = BinaryPrimitives.ReadUInt64LittleEndian(result.Buffer.AsSpan(tabletBehavior ? 16 : 24, 8));
            Assert.InRange(received, before, after);
            Assert.InRange(sent, received, after);
        }

        public async ValueTask DisposeAsync()
        {
            Lifetime.Cancel();
            try { await Responder; }
            catch { /* Each test owns the failure assertions; always drain the responder. */ }
            Source.Dispose();
            Receiver.Dispose();
            Lifetime.Dispose();
        }

        private static ulong Microseconds() => MiPlayWfdMediaClock.ConvertStopwatchTicksToMicroseconds(
            Stopwatch.GetTimestamp(), Stopwatch.Frequency);
    }
}
