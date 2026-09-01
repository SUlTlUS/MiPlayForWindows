using System.Numerics;
using DLNACast.Core.MiPlay;
using DLNACast.Tests.References;

namespace DLNACast.Tests;

public sealed class MiPlayWfdMediaClockTests
{
    [Theory]
    [InlineData(58_878_859_721UL, 0UL, 1_000_000, 58_879_859_721UL)]
    [InlineData(58_878_859_721UL, 21_333UL, 1_000_000, 58_879_838_388UL)]
    [InlineData(58_878_859_721UL, 58_878_859_721UL, 0, 0UL)]
    public void TabletTimeOffsetUsesServerClockMinusFirstAudioPtsPlusDelay(
        ulong serverNow, ulong firstAudioPts, int delay, ulong expected)
    {
        Assert.Equal(expected, MiPlayWfdMediaClock.CreateTabletTimeOffsetMicroseconds(
            serverNow, firstAudioPts, delay));
    }

    [Fact]
    public void TabletTimeOffsetChecksClockOrderDelayAndOverflow()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MiPlayWfdMediaClock.CreateTabletTimeOffsetMicroseconds(99, 100, 1_000_000));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MiPlayWfdMediaClock.CreateTabletTimeOffsetMicroseconds(100, 0, -1));
        Assert.Throws<OverflowException>(() =>
            MiPlayWfdMediaClock.CreateTabletTimeOffsetMicroseconds(ulong.MaxValue, 0, 1));
    }

    [Theory]
    [InlineData(0UL, 0UL)]
    [InlineData(1UL, 27UL)]
    [InlineData(58_878_863_788UL, 1_589_729_322_276UL)]
    public void TabletPcrUsesRawPacketizationClockWithoutSubtractingDelay(
        ulong nativeNow, ulong expected)
    {
        Assert.Equal(expected,
            MiPlayWfdMediaClock.CreateTabletProgramClockReference27Mhz(nativeNow));
    }

    [Theory]
    [InlineData(0UL, 0UL)]
    [InlineData(1UL, 27UL)]
    [InlineData(10UL, 270UL)]
    [InlineData(11UL, 297UL)]
    [InlineData(12UL, 324UL)]
    public void RetainsNative27MhzFractionAfterTheExistingDelay(ulong elapsedUs, ulong expectedPcr)
    {
        Assert.Equal(expectedPcr, MiPlayWfdMediaClock.CreateProgramClockReference27Mhz(1_000_000 + elapsedUs, 1_000_000));
    }

    [Fact]
    public void FullPcrMatchesNativeConversionWithoutChangingAnyLegacyBaseTick()
    {
        for (ulong i = 0; i < 1000; i++)
        {
            var captureUs = 58_878_863_788 + i;
            var pcr = MiPlayWfdMediaClock.CreateProgramClockReference27Mhz(captureUs, 1_000_000);
            Assert.Equal(MiPlayTabletOutputTimeline.CreatePcr27Mhz((long)captureUs - 1_000_000), pcr);
            Assert.Equal(MiPlayWfdMediaClock.CreateInitialProgramClockReference90Khz(captureUs, 1_000_000), pcr / 300);
            Assert.InRange(pcr % 300, 0UL, 299UL);
        }
    }

    [Fact]
    public void FullPcrWrapsThe33BitBaseAndCarriesTheExtension()
    {
        var before = MiPlayWfdMediaClock.CreateProgramClockReference27Mhz(95_443_717_688, 0);
        var after = MiPlayWfdMediaClock.CreateProgramClockReference27Mhz(95_443_717_689, 0);

        Assert.Equal(2_576_980_377_576UL, before);
        Assert.Equal(8_589_934_591UL, before / 300);
        Assert.Equal(276UL, before % 300);
        Assert.Equal(3UL, after);
        Assert.Equal(27UL, MiPlayWfdMediaClock.ProgramClockModulus27Mhz - before + after);
    }

    [Fact]
    public void FullPcrConversionAvoidsUptimeOverflowAndRejectsInvalidOrigins()
    {
        var pcr = MiPlayWfdMediaClock.CreateProgramClockReference27Mhz(ulong.MaxValue, 0);
        var expected = (ulong)((new BigInteger(ulong.MaxValue) * 27) % ((BigInteger.One << 33) * 300));
        Assert.Equal(expected, pcr);
        Assert.Equal(MiPlayWfdMediaClock.CreateInitialProgramClockReference90Khz(ulong.MaxValue, 0), pcr / 300);
        Assert.Throws<ArgumentOutOfRangeException>(() => MiPlayWfdMediaClock.CreateProgramClockReference27Mhz(999, 1000));
        Assert.Throws<ArgumentOutOfRangeException>(() => MiPlayWfdMediaClock.CreateProgramClockReference27Mhz(1000, -1));
    }

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
    public void TabletFormulaReconstructsCapturedTimeOffsetFromRawPcrClock()
    {
        const ulong capturedTimeOffsetMicroseconds = 9_633_364_443;
        const ulong capturedInitialPcr90Khz = 866_913_276;
        var packetizationNowMicroseconds = checked(
            capturedInitialPcr90Khz * MiPlayWfdMediaClock.MicrosecondsPerSecond /
            MiPlayWfdMediaClock.ProgramClockRate);

        var reconstructed = MiPlayWfdMediaClock.CreateTabletTimeOffsetMicroseconds(
            packetizationNowMicroseconds,
            firstAudioPresentationTimeMicroseconds: 0,
            playbackDelayMicroseconds: MiPlayProtocolConstants.OtherNetworkPlaybackDelayMicroseconds);

        // The capture contains only the 90-kHz PCR base and RTSP/PCR were
        // recorded several milliseconds apart, so exact equality is neither
        // available nor expected. It still lands inside one AAC frame.
        Assert.InRange(
            Math.Abs((long)reconstructed - (long)capturedTimeOffsetMicroseconds),
            0,
            21_333);
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

    [Fact]
    public void NativePairPcrTracksLiveMonotonicTimeInsteadOfRtpAccessUnitCount()
    {
        const ulong capturedTimeOffsetMicroseconds = 58_878_859_721;
        const ulong firstMediaMonotonicMicroseconds = 58_878_863_788;
        const ulong lastMediaMonotonicMicroseconds = 58_910_782_339;

        var firstPcr = MiPlayWfdMediaClock.CreateInitialProgramClockReference90Khz(
            firstMediaMonotonicMicroseconds,
            MiPlayProtocolConstants.OtherNetworkPlaybackDelayMicroseconds);
        var lastPcr = MiPlayWfdMediaClock.CreateInitialProgramClockReference90Khz(
            lastMediaMonotonicMicroseconds,
            MiPlayProtocolConstants.OtherNetworkPlaybackDelayMicroseconds);

        Assert.Equal(5_299_007_740UL, firstPcr);
        Assert.Equal(5_301_880_410UL, lastPcr);
        Assert.InRange(
            Math.Abs(
                (long)(lastPcr - firstPcr) -
                (long)((lastMediaMonotonicMicroseconds - firstMediaMonotonicMicroseconds) * 90 / 1_000)),
            0,
            1);
        Assert.Equal(4_067UL, firstMediaMonotonicMicroseconds - capturedTimeOffsetMicroseconds);
    }
}
