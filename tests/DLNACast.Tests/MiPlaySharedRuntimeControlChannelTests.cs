using System.Threading.Channels;
using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlaySharedRuntimeControlChannelTests
{
    [Fact]
    public void RetainsNativeHeartbeatDefaultsAndLegacyCommandPolicy()
    {
        Assert.Equal(TimeSpan.FromSeconds(15), MiPlaySharedRuntimeControlChannel.HeartbeatInactivityTimeout);
        Assert.Equal(TimeSpan.FromSeconds(1), MiPlaySharedRuntimeControlChannel.HeartbeatCheckInterval);
        Assert.Equal(TimeSpan.FromSeconds(3), MiPlaySharedRuntimeControlChannel.CommandAcknowledgementTimeout);
        Assert.Equal(5_000, MiPlayLegacyStreamingHeartbeatPlan.IntervalMilliseconds);
        Assert.True(new MiPlayLegacyRuntimeControlSequence().PrepareHeartbeat().WaitForAcknowledgement);
    }

    [Fact]
    public async Task MissingHeartbeatAckDoesNotGateTheNextCommand()
    {
        await using var harness = new Harness();
        var sequence = new MiPlayLegacyRuntimeControlSequence(0x22);
        await harness.Control.ExecuteAsync(sequence.PrepareHeartbeat(), harness.Token);
        Assert.Equal((ushort)0x22, (await harness.NextWritten()).Sequence);
        var volume = sequence.PrepareSetVolume(36);
        var update = harness.Control.ExecuteAsync(volume, harness.Token);
        Assert.Equal(volume.Sequence, (await harness.NextWritten()).Sequence);
        Assert.False(update.IsCompleted);
        await harness.Receive(volume.AcknowledgementCommand, volume.Sequence, volume.Payload);
        await update.WaitAsync(harness.Token);
        Assert.False(harness.Control.Completion.IsCompleted);
    }

    [Fact]
    public async Task HeartbeatStillWaitsForSuccessfulWriteAndPropagatesWriteFailure()
    {
        var write = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var harness = new Harness(writeOverride: (_, token) => write.Task.WaitAsync(token));
        var send = harness.Control.ExecuteAsync(new MiPlayLegacyRuntimeControlSequence().PrepareHeartbeat(), harness.Token);
        Assert.False(send.IsCompleted);
        var failure = new IOException("control write failed");
        write.SetException(failure);
        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => send));
        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => harness.Control.Completion));
    }

    [Fact]
    public async Task LateHeartbeatAcksDoNotAcknowledgeVolumeAndNotificationsHaveNoEightFrameCap()
    {
        await using var harness = new Harness();
        var sequence = new MiPlayLegacyRuntimeControlSequence(0x22);
        await harness.Control.ExecuteAsync(sequence.PrepareHeartbeat(), harness.Token);
        await harness.NextWritten();
        var volume = sequence.PrepareSetVolume(18);
        var update = harness.Control.ExecuteAsync(volume, harness.Token);
        await harness.NextWritten();
        await harness.Receive(MiPlayProtocolConstants.HeartbeatAcknowledgementCommand, 0x22);
        await harness.Receive(MiPlayProtocolConstants.HeartbeatAcknowledgementCommand, 0x20);
        for (ushort index = 0; index < 32; index++)
        {
            await harness.Receive(MiPlayProtocolConstants.NotifyCommand, index, [1, 2, 3]);
        }
        await harness.Receive(volume.AcknowledgementCommand, (ushort)(volume.Sequence + 1), volume.Payload);
        await harness.Receive(volume.AcknowledgementCommand, volume.Sequence, MiPlaySetVolumePayloadCodec.Encode(19));
        Assert.False(update.IsCompleted);
        await harness.Receive(volume.AcknowledgementCommand, volume.Sequence, volume.Payload);
        await update.WaitAsync(harness.Token);
    }

    [Fact]
    public async Task PeerHeartbeatIsAcknowledgedWhileIdleWithoutConsumingSourceSequence()
    {
        await using var harness = new Harness();
        var sequence = new MiPlayLegacyRuntimeControlSequence(0xffff);
        await harness.Receive(MiPlayProtocolConstants.HeartbeatCommand, 0x1234);
        var ack = await harness.NextWritten();
        Assert.Equal(MiPlayProtocolConstants.HeartbeatAcknowledgementCommand, ack.Command);
        Assert.Equal((ushort)0x1234, ack.Sequence);
        Assert.Empty(ack.Payload);
        await harness.Control.ExecuteAsync(sequence.PrepareHeartbeat(), harness.Token);
        Assert.Equal(ushort.MaxValue, (await harness.NextWritten()).Sequence);
        await harness.Control.ExecuteAsync(sequence.PrepareHeartbeat(), harness.Token);
        Assert.Equal((ushort)0, (await harness.NextWritten()).Sequence);
    }

    [Fact]
    public async Task GenuineInactivityStillFailsEvenWithUnrelatedNotifications()
    {
        var clock = new TestClock();
        await using var harness = new Harness(clock);
        clock.Advance(TimeSpan.FromSeconds(16));
        await harness.Receive(MiPlayProtocolConstants.NotifyCommand, 7, [1]);
        var failure = await Assert.ThrowsAsync<TimeoutException>(
            () => harness.Control.Completion.WaitAsync(harness.Token));
        Assert.Contains("fifteen seconds", failure.Message);
    }

    [Fact]
    public async Task DecodedReceiverStateNotificationsUpdatePlaybackStateWithoutRefreshingHeartbeat()
    {
        var notifications = new List<MiPlayNotifyPayload>();
        await using var harness = new Harness(notificationReceived: notifications.Add);

        await harness.Receive(
            MiPlayProtocolConstants.NotifyCommand,
            0x33,
            NotifyScalar("state", 3));

        var notification = Assert.Single(notifications);
        Assert.Equal("state", notification.Label);
        Assert.Equal(3, notification.IntegerValue);
        Assert.False(harness.Control.Completion.IsCompleted);
    }

    [Theory]
    [InlineData(MiPlayProtocolConstants.HeartbeatCommand)]
    [InlineData(MiPlayProtocolConstants.HeartbeatAcknowledgementCommand)]
    public async Task EitherHeartbeatDirectionRefreshesInactivityWithoutMatchingAnOutstandingSequence(ushort command)
    {
        var clock = new TestClock();
        await using var harness = new Harness(clock);
        clock.Advance(TimeSpan.FromSeconds(14));
        await harness.Receive(command, 999);
        clock.Advance(TimeSpan.FromSeconds(2));
        await Task.Delay(1_100, harness.Token);
        Assert.False(harness.Control.Completion.IsCompleted);
    }

    [Theory]
    [InlineData(MiPlayProtocolConstants.HeartbeatCommand)]
    [InlineData(MiPlayProtocolConstants.HeartbeatAcknowledgementCommand)]
    public async Task RejectsMalformedHeartbeatInsteadOfRefreshingLiveness(ushort command)
    {
        await using var harness = new Harness();
        harness.Inbound.Writer.TryWrite(MiPlayCommandFrameCodec.Encode(command, 1, [1]));
        await Assert.ThrowsAsync<InvalidDataException>(() => harness.Control.Completion.WaitAsync(harness.Token));
    }

    [Fact]
    public async Task ReaderFailureEndsPendingCommandWithItsOriginalCause()
    {
        await using var harness = new Harness();
        var update = harness.Control.ExecuteAsync(new MiPlayLegacyRuntimeControlSequence().PrepareSetVolume(18), harness.Token);
        await harness.NextWritten();
        harness.Inbound.Writer.TryComplete(new IOException("peer disconnected"));
        var failure = await Assert.ThrowsAsync<ChannelClosedException>(() => harness.Control.Completion.WaitAsync(harness.Token));
        Assert.Same(failure, await Assert.ThrowsAsync<ChannelClosedException>(() => update));
        Assert.Equal("peer disconnected", failure.InnerException?.Message);
    }

    [Fact]
    public async Task BadFrameIsFatalWhileIdle()
    {
        await using var harness = new Harness();
        harness.Inbound.Writer.TryWrite([0, 1, 2]);
        await Assert.ThrowsAsync<InvalidDataException>(() => harness.Control.Completion.WaitAsync(harness.Token));
    }

    [Fact]
    public async Task MissingVolumeAckStillFailsTheCommandAfterThreeSeconds()
    {
        await using var harness = new Harness();
        var command = new MiPlayLegacyRuntimeControlSequence(0x22).PrepareSetVolume(18);
        var update = harness.Control.ExecuteAsync(command, harness.Token);
        await harness.NextWritten();
        var failure = await Assert.ThrowsAsync<TimeoutException>(() => update);
        Assert.Contains("0x000C sequence 0x0022", failure.Message);
        Assert.Same(failure, await Assert.ThrowsAsync<TimeoutException>(() => harness.Control.Completion));
    }

    [Fact]
    public async Task HeartbeatReplyCannotInterleaveAnInFlightCommandWrite()
    {
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondWritten = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        var active = 0;
        await using var harness = new Harness(writeOverride: async (_, token) =>
        {
            Assert.Equal(1, Interlocked.Increment(ref active));
            try
            {
                var index = Interlocked.Increment(ref count);
                if (index == 1)
                {
                    firstEntered.SetResult();
                    await releaseFirst.Task.WaitAsync(token);
                }
                else
                {
                    secondWritten.SetResult();
                }
            }
            finally
            {
                Interlocked.Decrement(ref active);
            }
        });
        var send = harness.Control.ExecuteAsync(new MiPlayLegacyRuntimeControlSequence().PrepareHeartbeat(), harness.Token);
        await firstEntered.Task.WaitAsync(harness.Token);
        await harness.Receive(MiPlayProtocolConstants.HeartbeatCommand, 50);
        Assert.Equal(1, Volatile.Read(ref count));
        releaseFirst.SetResult();
        await send.WaitAsync(harness.Token);
        await secondWritten.Task.WaitAsync(harness.Token);
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task WatchdogUsesNativeStrictGreaterThanTimeoutBoundary()
    {
        var clock = new TestClock();
        await using var harness = new Harness(clock);
        clock.Advance(TimeSpan.FromSeconds(15));
        await Task.Delay(1_100, harness.Token);
        Assert.False(harness.Control.Completion.IsCompleted);
        clock.Advance(TimeSpan.FromTicks(1));
        await Assert.ThrowsAsync<TimeoutException>(() => harness.Control.Completion.WaitAsync(harness.Token));
    }

    [Fact]
    public async Task CallerCancellationStopsReaderAndPendingCommand()
    {
        await using var harness = new Harness();
        var update = harness.Control.ExecuteAsync(new MiPlayLegacyRuntimeControlSequence().PrepareSetVolume(18), harness.Token);
        await harness.NextWritten();
        harness.Lifetime.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => update);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => harness.Control.Completion);
    }

    private sealed class TestClock : TimeProvider
    {
        private long ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref ticks);
        public void Advance(TimeSpan duration) => Interlocked.Add(ref ticks, duration.Ticks);
    }

    private sealed class Harness : IAsyncDisposable
    {
        public CancellationTokenSource Lifetime { get; } = new(TimeSpan.FromSeconds(8));
        public CancellationToken Token => Lifetime.Token;
        public Channel<byte[]> Inbound { get; } = Channel.CreateUnbounded<byte[]>();
        private readonly Channel<byte[]> written = Channel.CreateUnbounded<byte[]>();
        private readonly Channel<string> observed = Channel.CreateUnbounded<string>();
        public MiPlaySharedRuntimeControlChannel Control { get; }

        public Harness(TimeProvider? clock = null,
            Func<ReadOnlyMemory<byte>, CancellationToken, Task>? writeOverride = null,
            Action<MiPlayNotifyPayload>? notificationReceived = null)
        {
            Control = new(
                token => Inbound.Reader.ReadAsync(token).AsTask(),
                writeOverride ?? ((bytes, token) => written.Writer.WriteAsync(bytes.ToArray(), token).AsTask()),
                evidence => observed.Writer.TryWrite(evidence),
                Token,
                clock,
                notificationReceived);
        }

        public async Task Receive(ushort command, ushort sequence, byte[]? payload = null)
        {
            Inbound.Writer.TryWrite(MiPlayCommandFrameCodec.Encode(command, sequence, payload ?? []));
            var marker = $"command=0x{command:X4}, sequence=0x{sequence:X4}";
            while (!(await observed.Reader.ReadAsync(Token)).Contains(marker, StringComparison.Ordinal)) { }
        }

        public async Task<MiPlayCommandFrame> NextWritten()
        {
            var bytes = await written.Reader.ReadAsync(Token);
            Assert.True(MiPlayCommandFrameCodec.TryDecode(bytes, out var frame, out var consumed));
            Assert.Equal(bytes.Length, consumed);
            return Assert.IsType<MiPlayCommandFrame>(frame);
        }

        public async ValueTask DisposeAsync()
        {
            await Control.DisposeAsync();
            Lifetime.Dispose();
        }
    }

    private static byte[] NotifyScalar(string label, byte value)
    {
        var labelBytes = System.Text.Encoding.ASCII.GetBytes(label);
        var payload = new byte[1 + labelBytes.Length + 2];
        payload[0] = checked((byte)labelBytes.Length);
        labelBytes.CopyTo(payload, 1);
        payload[^2] = MiPlayNotifyPayloadCodec.ByteValueType;
        payload[^1] = value;
        return payload;
    }
}
