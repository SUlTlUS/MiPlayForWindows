using DLNACast.Core.Audio;
using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class PcmFrameBufferSequenceTests
{
    [Fact]
    public async Task EmptySharedRouteWaitsForProducerInsteadOfCreatingSilence()
    {
        await using var buffer = CreateBuffer();
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var pending = buffer.ReadFramePreservingSequenceAsync(lifetime.Token).AsTask();
        Assert.False(pending.IsCompleted);
        Assert.Equal(0, buffer.Underruns);

        await buffer.WritePreservingSequenceAsync(Frame(1, 1_234_567), lifetime.Token);
        var result = await pending;
        Assert.Equal(1, result.Bytes[0]);
        Assert.Equal(1_234_567UL, result.PresentationTimeMicroseconds);
        Assert.False(result.IsBufferUnderrunSilence);
        Assert.Equal(0, buffer.Underruns);
    }

    [Fact]
    public async Task AlreadyPacedProducerFramesDoNotAcquireAnotherReadCadence()
    {
        await using var buffer = CreateBuffer();
        for (byte i = 1; i <= 4; i++)
            await buffer.WritePreservingSequenceAsync(Frame(i, i * 21_333UL), CancellationToken.None);
        for (byte i = 1; i <= 4; i++)
        {
            var read = buffer.ReadFramePreservingSequenceAsync(CancellationToken.None);
            Assert.True(read.IsCompletedSuccessfully);
            Assert.Equal(i, (await read).Bytes[0]);
        }
        Assert.Equal(0, buffer.BufferedMilliseconds);
        Assert.Equal(0, buffer.Underruns);
    }

    [Fact]
    public async Task FasterRouteCannotInsertAnExtraFrameWhileTheOtherRouteIsLate()
    {
        await using var left = CreateBuffer(AudioChannelRoute.LeftAsMono);
        await using var right = CreateBuffer(AudioChannelRoute.RightAsMono);
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await MiPlaySharedAudioSession.FanOutFramePreservingSequenceAsync(
            Frame(1, 1_000_000), [left, right], lifetime.Token);
        Assert.Equal(1, (await left.ReadFramePreservingSequenceAsync(lifetime.Token)).Bytes[0]);

        var nextLeft = left.ReadFramePreservingSequenceAsync(lifetime.Token).AsTask();
        Assert.False(nextLeft.IsCompleted);
        Assert.Equal(1, (await right.ReadFramePreservingSequenceAsync(lifetime.Token)).Bytes[0]);
        var nextRight = right.ReadFramePreservingSequenceAsync(lifetime.Token).AsTask();
        Assert.False(nextRight.IsCompleted);
        await MiPlaySharedAudioSession.FanOutFramePreservingSequenceAsync(
            Frame(2, 1_021_333), [left, right], lifetime.Token);

        var results = await Task.WhenAll(nextLeft, nextRight);
        Assert.All(results, frame => Assert.Equal(2, frame.Bytes[0]));
        Assert.All(results, frame => Assert.Equal(1_021_333UL, frame.PresentationTimeMicroseconds));
        Assert.Equal(0, left.Underruns);
        Assert.Equal(0, right.Underruns);
    }

    [Fact]
    public async Task SharedRecoverySilenceAndItsTimestampAreNotSkipped()
    {
        await using var buffer = CreateBuffer();
        var recovery = Frame(0, 5_000_000) with { ReanchorPresentationTimeline = true };
        await buffer.WritePreservingSequenceAsync(recovery, CancellationToken.None);
        await buffer.WritePreservingSequenceAsync(Frame(7, 5_021_333), CancellationToken.None);
        var first = await buffer.ReadFramePreservingSequenceAsync(CancellationToken.None);
        var next = await buffer.ReadFramePreservingSequenceAsync(CancellationToken.None);
        Assert.All(first.Bytes, value => Assert.Equal(0, value));
        Assert.Equal(5_000_000UL, first.PresentationTimeMicroseconds);
        Assert.True(first.ReanchorPresentationTimeline);
        Assert.Equal(7, next.Bytes[0]);
        Assert.Equal(5_021_333UL, next.PresentationTimeMicroseconds);
    }

    [Fact]
    public async Task ReadingAQueuedFrameReleasesExactlyOneBackpressureSlot()
    {
        await using var buffer = CreateBuffer(maximumMilliseconds: 22);
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await buffer.WritePreservingSequenceAsync(Frame(1, 1), lifetime.Token);
        var secondWrite = buffer.WritePreservingSequenceAsync(Frame(2, 2), lifetime.Token).AsTask();
        Assert.False(secondWrite.IsCompleted);
        Assert.Equal(1, (await buffer.ReadFramePreservingSequenceAsync(lifetime.Token)).Bytes[0]);
        await secondWrite;
        Assert.Equal(2, (await buffer.ReadFramePreservingSequenceAsync(lifetime.Token)).Bytes[0]);
        Assert.Equal(0, buffer.Overruns);
    }

    [Fact]
    public async Task CancelledWaitDoesNotLoseTheNextFrameOrBlockAnotherReader()
    {
        await using var buffer = CreateBuffer();
        using var cancelled = new CancellationTokenSource();
        var pending = buffer.ReadFramePreservingSequenceAsync(cancelled.Token).AsTask();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        await buffer.WritePreservingSequenceAsync(Frame(3, 3), CancellationToken.None);
        var next = await buffer.ReadFramePreservingSequenceAsync(CancellationToken.None);
        Assert.Equal(3, next.Bytes[0]);
        Assert.Equal(0, buffer.Underruns);
    }

    [Fact]
    public async Task CompletionWakesAnEmptyReadWithoutFabricatingMoreFrames()
    {
        await using var buffer = CreateBuffer();
        var pending = buffer.ReadFramePreservingSequenceAsync(CancellationToken.None).AsTask();
        Assert.False(pending.IsCompleted);
        buffer.Complete();
        await Assert.ThrowsAsync<EndOfStreamException>(() => pending);
        await Assert.ThrowsAsync<EndOfStreamException>(
            () => buffer.ReadFramePreservingSequenceAsync(CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task DefaultCaptureBufferRetainsItsTimedSilenceBehavior()
    {
        await using var buffer = new PcmFrameBuffer(AudioChannelRoute.Stereo, PcmFrameFormat.MiPlayAac);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => buffer.ReadFramePreservingSequenceAsync(CancellationToken.None).AsTask());
        var silence = await buffer.ReadFrameWithTimingOrSilenceAsync(CancellationToken.None);
        Assert.True(silence.IsBufferUnderrunSilence);
        Assert.Equal(1, buffer.Underruns);
    }

    [Fact]
    public async Task GroupShutdownKeepsCancellationSemantics()
    {
        await using var buffer = CreateBuffer();
        using var lifetime = new CancellationTokenSource();
        var pending = buffer.ReadFramePreservingSequenceAsync(lifetime.Token).AsTask();
        lifetime.Cancel();
        buffer.Complete();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    [Fact]
    public async Task RepeatedProducerConsumerWakeupsKeepEveryFrameInOrder()
    {
        await using var buffer = CreateBuffer(maximumMilliseconds: 22);
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        const int frameCount = 1_000;
        var producer = Task.Run(async () =>
        {
            for (var i = 0; i < frameCount; i++)
            {
                await buffer.WritePreservingSequenceAsync(Frame((byte)(i % 256), (ulong)i), lifetime.Token);
                if (i % 7 == 0) await Task.Yield();
            }
        }, lifetime.Token);
        for (var i = 0; i < frameCount; i++)
        {
            var frame = await buffer.ReadFramePreservingSequenceAsync(lifetime.Token);
            Assert.Equal((byte)(i % 256), frame.Bytes[0]);
            Assert.Equal((ulong)i, frame.PresentationTimeMicroseconds);
            if (i % 11 == 0) await Task.Yield();
        }
        await producer;
        Assert.Equal(0, buffer.BufferedMilliseconds);
        Assert.Equal(0, buffer.Overruns);
        Assert.Equal(0, buffer.Underruns);
    }

    [Fact]
    public async Task TrimmedFramesDoNotLeaveAStaleWakeupOrConsumeTheNextFrame()
    {
        await using var buffer = CreateBuffer(maximumMilliseconds: 22);
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await buffer.WritePreservingSequenceAsync(Frame(1, 1), lifetime.Token);
        Assert.Equal(1, buffer.TrimToLatest(0));
        var pending = buffer.ReadFramePreservingSequenceAsync(lifetime.Token).AsTask();
        Assert.False(pending.IsCompleted);
        await buffer.WritePreservingSequenceAsync(Frame(2, 2), lifetime.Token);
        var frame = await pending;
        Assert.Equal(2, frame.Bytes[0]);
        Assert.Equal(2UL, frame.PresentationTimeMicroseconds);
        Assert.Equal(0, buffer.Underruns);
    }

    private static PcmFrameBuffer CreateBuffer(
        AudioChannelRoute route = AudioChannelRoute.Stereo, int maximumMilliseconds = 100) =>
        new(route, PcmFrameFormat.MiPlayAac, maximumMilliseconds, preserveFrameSequence: true);

    private static PcmFrameRead Frame(byte value, ulong pts) =>
        new(Enumerable.Repeat(value, PcmFrameFormat.MiPlayAac.BytesPerFrame).ToArray(), pts);
}
