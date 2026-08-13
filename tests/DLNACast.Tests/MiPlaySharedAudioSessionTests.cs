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

        MiPlaySharedAudioSession.FanOutFrame(frame, [first, second, third]);

        Assert.Equal(frame, await first.ReadFrameOrSilenceAsync(CancellationToken.None));
        Assert.Equal(frame, await second.ReadFrameOrSilenceAsync(CancellationToken.None));
        Assert.Equal(frame, await third.ReadFrameOrSilenceAsync(CancellationToken.None));
    }

    [Fact]
    public async Task FanOutKeepsReceiverBuffersIndependent()
    {
        await using var first = new PcmFrameBuffer();
        await using var second = new PcmFrameBuffer();
        var frame = Enumerable.Repeat((byte)0x33, PcmFrameBuffer.BytesPerFrame).ToArray();

        MiPlaySharedAudioSession.FanOutFrame(frame, [first, second]);
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
            input,
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
}
