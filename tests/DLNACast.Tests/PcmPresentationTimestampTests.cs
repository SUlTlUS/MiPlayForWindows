using DLNACast.Core.Audio;

namespace DLNACast.Tests;

public sealed class PcmPresentationTimestampTests
{
    [Fact]
    public void SubtractsActualReadDurationFromElapsedRealtimeNanoseconds()
    {
        var presentationTime = PcmPresentationTimestamp.CalculateMicroseconds(
            elapsedRealtimeNanoseconds: 10_000_000_000,
            actualBytesRead: 4_096,
            PcmFrameFormat.MiPlayAac);

        Assert.Equal(9_978_666UL, presentationTime);
    }

    [Fact]
    public async Task AssemblerKeepsFirstSampleTimeAcrossPartialReads()
    {
        await using var buffer = new PcmFrameBuffer(
            AudioChannelRoute.Stereo,
            PcmFrameFormat.MiPlayAac);
        var assembler = new PcmFrameAssembler(buffer);
        var first = new byte[2_048];
        var second = new byte[2_048];

        assembler.Push(first, elapsedRealtimeNanoseconds: 1_010_666_666);
        assembler.Push(second, elapsedRealtimeNanoseconds: 1_021_333_333);
        var frame = await buffer.ReadFrameWithTimingOrSilenceAsync(CancellationToken.None);

        Assert.Equal(4_096, frame.Bytes.Length);
        Assert.Equal(1_000_000UL, frame.PresentationTimeMicroseconds);
    }

    [Fact]
    public async Task AssemblerUsesTheTabletsConservativeCaptureRecoveryWindow()
    {
        await using var buffer = new PcmFrameBuffer(
            AudioChannelRoute.Stereo,
            PcmFrameFormat.MiPlayAac);
        var assembler = new PcmFrameAssembler(buffer);
        var pcm = new byte[PcmFrameFormat.MiPlayAac.BytesPerFrame];

        assembler.Push(pcm, elapsedRealtimeNanoseconds: 1_021_333_333);
        assembler.Push(pcm, elapsedRealtimeNanoseconds: 1_121_333_333);
        assembler.Push(pcm, elapsedRealtimeNanoseconds: 1_281_333_333);
        var first = await buffer.ReadFrameWithTimingOrSilenceAsync(CancellationToken.None);
        var second = await buffer.ReadFrameWithTimingOrSilenceAsync(CancellationToken.None);
        var third = await buffer.ReadFrameWithTimingOrSilenceAsync(CancellationToken.None);

        Assert.False(first.CaptureDiscontinuity);
        Assert.False(second.CaptureDiscontinuity);
        Assert.True(third.CaptureDiscontinuity);
    }
}
