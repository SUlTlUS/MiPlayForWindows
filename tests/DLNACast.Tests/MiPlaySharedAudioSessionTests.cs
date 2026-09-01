using DLNACast.Core.Audio;
using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlaySharedAudioSessionTests
{
    [Fact]
    public async Task FanOutDeliversTheSamePcmFrameToEveryReceiverBuffer()
    {
        await using var first = new PcmFrameBuffer();
        await using var second = new PcmFrameBuffer();
        await using var third = new PcmFrameBuffer();
        var frame = Enumerable.Repeat((byte)0x5A, PcmFrameBuffer.BytesPerFrame).ToArray();

        MiPlaySharedAudioSession.FanOutFrame(
            new PcmFrameRead(
                frame,
                1_234_567,
                ReanchorPresentationTimeline: true),
            [first, second, third]);

        var firstRead = await first.ReadFrameWithTimingOrSilenceAsync(CancellationToken.None);
        Assert.Equal(frame, firstRead.Bytes);
        Assert.Equal(1_234_567UL, firstRead.PresentationTimeMicroseconds);
        Assert.True(firstRead.ReanchorPresentationTimeline);
        var secondRead = await second.ReadFrameWithTimingOrSilenceAsync(CancellationToken.None);
        var thirdRead = await third.ReadFrameWithTimingOrSilenceAsync(CancellationToken.None);
        Assert.Equal(frame, secondRead.Bytes);
        Assert.Equal(frame, thirdRead.Bytes);
        Assert.True(secondRead.ReanchorPresentationTimeline);
        Assert.True(thirdRead.ReanchorPresentationTimeline);
    }

    [Fact]
    public async Task FanOutKeepsReceiverBuffersIndependent()
    {
        await using var first = new PcmFrameBuffer();
        await using var second = new PcmFrameBuffer();
        var frame = Enumerable.Repeat((byte)0x33, PcmFrameBuffer.BytesPerFrame).ToArray();

        MiPlaySharedAudioSession.FanOutFrame(
            new PcmFrameRead(frame, 1_234_567),
            [first, second]);
        var firstFrame = await first.ReadFrameOrSilenceAsync(CancellationToken.None);
        firstFrame[0] = 0;

        var secondFrame = await second.ReadFrameOrSilenceAsync(CancellationToken.None);
        Assert.Equal(0x33, secondFrame[0]);
    }

    [Fact]
    public async Task FanOutRoutesTheSameCaptureFrameForLeftAndRightGroups()
    {
        await using var leftFirst = new PcmFrameBuffer(AudioChannelRoute.LeftAsMono);
        await using var leftSecond = new PcmFrameBuffer(AudioChannelRoute.LeftAsMono);
        await using var rightFirst = new PcmFrameBuffer(AudioChannelRoute.RightAsMono);
        await using var rightSecond = new PcmFrameBuffer(AudioChannelRoute.RightAsMono);
        var input = new byte[PcmFrameBuffer.BytesPerFrame];
        for (var offset = 0; offset < input.Length; offset += 4)
        {
            BitConverter.TryWriteBytes(input.AsSpan(offset, 2), (short)1200);
            BitConverter.TryWriteBytes(input.AsSpan(offset + 2, 2), (short)-2300);
        }

        MiPlaySharedAudioSession.FanOutFrame(
            new PcmFrameRead(input, 1_234_567),
            [leftFirst, leftSecond, rightFirst, rightSecond]);

        foreach (var buffer in new[] { leftFirst, leftSecond })
        {
            var frame = await buffer.ReadFrameOrSilenceAsync(CancellationToken.None);
            Assert.Equal((short)1200, BitConverter.ToInt16(frame, 0));
            Assert.Equal((short)1200, BitConverter.ToInt16(frame, 2));
        }
        foreach (var buffer in new[] { rightFirst, rightSecond })
        {
            var frame = await buffer.ReadFrameOrSilenceAsync(CancellationToken.None);
            Assert.Equal((short)-2300, BitConverter.ToInt16(frame, 0));
            Assert.Equal((short)-2300, BitConverter.ToInt16(frame, 2));
        }
    }

    [Fact]
    public async Task FanOutPreservingSequenceKeepsAnUnderrunSilenceFrameInEveryRoute()
    {
        await using var left = new PcmFrameBuffer(
            AudioChannelRoute.LeftAsMono,
            PcmFrameFormat.MiPlayAac);
        await using var right = new PcmFrameBuffer(
            AudioChannelRoute.RightAsMono,
            PcmFrameFormat.MiPlayAac);
        var silence = new byte[PcmFrameFormat.MiPlayAac.BytesPerFrame];

        await MiPlaySharedAudioSession.FanOutFramePreservingSequenceAsync(
            new PcmFrameRead(
                silence,
                1_234_567,
                IsBufferUnderrunSilence: true),
            [left, right],
            CancellationToken.None);

        var leftRead = await left.ReadFrameWithTimingOrSilenceAsync(CancellationToken.None);
        var rightRead = await right.ReadFrameWithTimingOrSilenceAsync(CancellationToken.None);
        Assert.Equal(silence, leftRead.Bytes);
        Assert.Equal(silence, rightRead.Bytes);
        Assert.Equal(1_234_567UL, leftRead.PresentationTimeMicroseconds);
        Assert.Equal(1_234_567UL, rightRead.PresentationTimeMicroseconds);
        Assert.Equal(0, left.Underruns);
        Assert.Equal(0, right.Underruns);
    }
}
