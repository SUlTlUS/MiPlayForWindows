using DLNACast.Core.Audio;
using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayCaptureDiscontinuityRecoveryTests
{
    [Fact]
    public void OrdinaryCaptureCadenceDoesNotReplaceAudioWithSilence()
    {
        var recovery = new MiPlayCaptureDiscontinuityRecovery(PcmFrameFormat.MiPlayAac);

        Assert.All(recovery.Recover(Frame(1_000_000, 0x11)).Bytes, value => Assert.Equal(0x11, value));
        Assert.All(recovery.Recover(Frame(1_021_333, 0x22)).Bytes, value => Assert.Equal(0x22, value));
        Assert.Equal(0, recovery.DiscontinuityCount);
        Assert.Equal(0, recovery.ReplacedSilenceFrameCount);
    }

    [Fact]
    public void CaptureGapReplacesCurrentAndNextTwoFramesWithoutAddingFrames()
    {
        var recovery = new MiPlayCaptureDiscontinuityRecovery(PcmFrameFormat.MiPlayAac);
        recovery.Recover(Frame(1_000_000, 0x11));

        var first = recovery.Recover(Frame(
            1_200_000,
            0x5A,
            captureDiscontinuity: true));
        var second = recovery.Recover(Frame(1_221_333, 0x5B));
        var third = recovery.Recover(Frame(1_242_666, 0x5C));
        var fourth = recovery.Recover(Frame(1_264_000, 0x5D));

        Assert.All(first.Bytes, value => Assert.Equal(0, value));
        Assert.All(second.Bytes, value => Assert.Equal(0, value));
        Assert.All(third.Bytes, value => Assert.Equal(0, value));
        Assert.All(fourth.Bytes, value => Assert.Equal(0x5D, value));
        Assert.Equal(1_200_000UL, first.PresentationTimeMicroseconds);
        Assert.Equal(1_221_333UL, second.PresentationTimeMicroseconds);
        Assert.Equal(1_242_666UL, third.PresentationTimeMicroseconds);
        Assert.Equal(1_264_000UL, fourth.PresentationTimeMicroseconds);
        Assert.Equal(1, recovery.DiscontinuityCount);
        Assert.Equal(3, recovery.ReplacedSilenceFrameCount);
    }

    [Fact]
    public void ASecondGapRestartsTheThreeFrameReplacementWindow()
    {
        var recovery = new MiPlayCaptureDiscontinuityRecovery(PcmFrameFormat.MiPlayAac);
        recovery.Recover(Frame(1_000_000, 0x11));

        recovery.Recover(Frame(1_200_000, 0x22, captureDiscontinuity: true));
        recovery.Recover(Frame(1_221_333, 0x23));
        recovery.Recover(Frame(1_500_000, 0x24, captureDiscontinuity: true));
        recovery.Recover(Frame(1_521_333, 0x25));
        recovery.Recover(Frame(1_542_666, 0x26));
        var audible = recovery.Recover(Frame(1_564_000, 0x27));

        Assert.All(audible.Bytes, value => Assert.Equal(0x27, value));
        Assert.Equal(2, recovery.DiscontinuityCount);
        Assert.Equal(5, recovery.ReplacedSilenceFrameCount);
    }

    [Fact]
    public void BufferUnderrunSilencePassesThroughWithoutStartingCaptureRecovery()
    {
        var recovery = new MiPlayCaptureDiscontinuityRecovery(PcmFrameFormat.MiPlayAac);
        var underrun = new PcmFrameRead(
            new byte[PcmFrameFormat.MiPlayAac.BytesPerFrame],
            1_000_000,
            IsBufferUnderrunSilence: true);

        Assert.True(recovery.Recover(underrun).IsBufferUnderrunSilence);
        Assert.Equal(0, recovery.DiscontinuityCount);
        Assert.All(recovery.Recover(Frame(2_000_000, 0x33)).Bytes, value => Assert.Equal(0x33, value));
    }

    [Fact]
    public void InternalQueueGapDoesNotCreateRecoverySilence()
    {
        var recovery = new MiPlayCaptureDiscontinuityRecovery(PcmFrameFormat.MiPlayAac);

        Assert.All(recovery.Recover(Frame(1_000_000, 0x11)).Bytes, value => Assert.Equal(0x11, value));
        Assert.All(recovery.Recover(Frame(2_000_000, 0x22)).Bytes, value => Assert.Equal(0x22, value));
        Assert.Equal(0, recovery.DiscontinuityCount);
        Assert.Equal(0, recovery.ReplacedSilenceFrameCount);
    }

    private static PcmFrameRead Frame(
        ulong presentationTimeMicroseconds,
        byte value,
        bool captureDiscontinuity = false) => new(
        Enumerable.Repeat(value, PcmFrameFormat.MiPlayAac.BytesPerFrame).ToArray(),
        presentationTimeMicroseconds,
        CaptureDiscontinuity: captureDiscontinuity);
}
