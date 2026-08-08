using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayWfdMediaClockTests
{
    [Fact]
    public void CapturedFirstPcrIsAboutOneSecondBeforeRtspTimeOffset()
    {
        const ulong capturedTimeOffsetMicroseconds = 9_633_364_443;
        const ulong capturedInitialPcr90Khz = 866_913_276;

        var observedDelay = MiPlayWfdMediaClock.MeasurePlaybackDelayMicroseconds(
            capturedTimeOffsetMicroseconds,
            capturedInitialPcr90Khz);
        var derivedInitialPcr = MiPlayWfdMediaClock.CreateInitialProgramClockReference90Khz(
            capturedTimeOffsetMicroseconds,
            MiPlayProtocolConstants.OtherNetworkPlaybackDelayMicroseconds);

        Assert.InRange(observedDelay, 994_700, 994_720);
        Assert.Equal(866_912_799UL, derivedInitialPcr);
        Assert.InRange(
            Math.Abs((long)capturedInitialPcr90Khz - (long)derivedInitialPcr),
            0,
            MiPlayWfdAudioPacketizer.TimestampStep90Khz);
    }

    [Fact]
    public void ConversionAvoidsMultiplicationOverflowAndRejectsInvalidOrigins()
    {
        var nearMaximum = MiPlayWfdMediaClock.CreateInitialProgramClockReference90Khz(
            ulong.MaxValue,
            playbackDelayMicroseconds: 0);

        Assert.True(nearMaximum > 0);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MiPlayWfdMediaClock.CreateInitialProgramClockReference90Khz(999, 1_000));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MiPlayWfdMediaClock.CreateInitialProgramClockReference90Khz(1_000, -1));
    }

    [Fact]
    public void StopwatchConversionAvoidsTheLiveSignedMultiplicationOverflow()
    {
        const long frequency = 10_000_000;
        const long ticks = 4_364_306_548_810;

        var microseconds = MiPlayWfdMediaClock.ConvertStopwatchTicksToMicroseconds(
            ticks,
            frequency);
        var initialPcr = MiPlayWfdMediaClock.CreateInitialProgramClockReference90Khz(
            microseconds,
            MiPlayProtocolConstants.OtherNetworkPlaybackDelayMicroseconds);

        Assert.Equal(436_430_654_881UL, microseconds);
        Assert.Equal(4_918_930_571UL, initialPcr);
        Assert.InRange(initialPcr, 0UL, MiPlayWfdMediaClock.ProgramClockModulus - 1);
        Assert.Equal(
            1_000_000UL,
            MiPlayWfdMediaClock.ConvertStopwatchTicksToMicroseconds(long.MaxValue, long.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MiPlayWfdMediaClock.ConvertStopwatchTicksToMicroseconds(-1, frequency));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MiPlayWfdMediaClock.ConvertStopwatchTicksToMicroseconds(ticks, 0));
    }
}
