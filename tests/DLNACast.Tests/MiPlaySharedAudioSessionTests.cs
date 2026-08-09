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
        var frame = Enumerable.Repeat((byte)0x5A, PcmFrameBuffer.BytesPerFrame).ToArray();

        MiPlaySharedAudioSession.FanOutFrame(frame, [first, second]);

        Assert.Equal(frame, await first.ReadFrameOrSilenceAsync(CancellationToken.None));
        Assert.Equal(frame, await second.ReadFrameOrSilenceAsync(CancellationToken.None));
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
}
