using DLNACast.Core.MiPlay;
using DLNACast.Tests.References;

namespace DLNACast.Tests;

public sealed class MiPlayTabletOutputTimelineTests
{
    [Fact]
    public void StartsAtZeroAndUsesActualEncoderInputTimes()
    {
        var timeline = new MiPlayTabletOutputTimeline();

        Assert.Equal(0, timeline.Map(1_000_000, 2_000_000));
        Assert.Equal(21_337, timeline.Map(1_021_337, 2_021_333));
        Assert.Equal(42_671, timeline.Map(1_042_671, 2_042_666));
        Assert.Equal(0, timeline.CorrectionCount);
    }

    [Theory]
    [InlineData(49, 21_333, 0)]
    [InlineData(50, 21_333, 0)]
    [InlineData(51, 21_384, 1)]
    public void FixThresholdIsStrictlyFiftyMicroseconds(long lag, long expected, long fixes)
    {
        var timeline = new MiPlayTabletOutputTimeline();
        timeline.Map(1_000_000, 2_000_000);

        Assert.Equal(expected, timeline.Map(1_021_333, 2_021_333 + lag));
        Assert.Equal(fixes, timeline.CorrectionCount);
    }

    [Fact]
    public void OutputDelayMovesTheAnchorForSubsequentFrames()
    {
        var timeline = new MiPlayTabletOutputTimeline();
        timeline.Map(1_000_000, 2_000_000);

        Assert.Equal(100_000, timeline.Map(1_021_333, 2_100_000));
        Assert.Equal(121_333, timeline.Map(1_042_666, 2_100_000));
        Assert.Equal(1, timeline.CorrectionCount);
        Assert.Equal(78_667, timeline.TotalCorrectionMicroseconds);
    }

    [Fact]
    public void PrewarmCaptureGapIsNotCompressedByTabletFixPts()
    {
        var timeline = new MiPlayTabletOutputTimeline();
        timeline.Map(1_000_000, 4_000_000);
        var previous = timeline.Map(1_021_333, 4_021_333);

        // A bounded Windows PCM source can advance during the RTSP/prewarm
        // wait. Tablet fix pts only catches up a lagging timeline; it does not
        // remove a capture jump or shorten a timeline that is already ahead.
        var afterGap = timeline.Map(4_000_000, 4_042_666);

        Assert.Equal(3_000_000, afterGap);
        Assert.Equal(2_978_667, afterGap - previous);
        Assert.Equal(0, timeline.CorrectionCount);
    }

    [Fact]
    public void OneSharedMapKeepsAllRouteTimestampsIdentical()
    {
        var timeline = new MiPlayTabletOutputTimeline();
        timeline.Map(1_000_000, 2_000_000);
        var pts = MiPlayTabletOutputTimeline.To90Khz(timeline.Map(1_021_333, 2_100_000));
        var accessUnit = MiPlayAdtsStreamParserTests.CreateMpeg4AccessUnit(946, 0x5a);
        var streams = Enumerable.Range(0, 3)
            .Select(_ => new MiPlayWfdAudioPacketizer().PacketizeAccessUnit(
                accessUnit, 900_000, checked((uint)pts)))
            .ToArray();

        Assert.Equal(9_000UL, pts);
        Assert.All(streams, packets =>
        {
            Assert.Equal(2, packets.Count);
            Assert.All(packets, packet => Assert.Equal((uint)pts, packet.Timestamp90Khz));
        });
        Assert.Equal(streams[0][0].WireFrame, streams[1][0].WireFrame);
        Assert.Equal(streams[0][0].WireFrame, streams[2][0].WireFrame);
    }

    [Fact]
    public void PcrUsesNativeNowIndependentlyOfThePresentationTimestamp()
    {
        const long nativeNow = 9_632_364_444;
        const long pts = 213_333;
        var pcr = MiPlayTabletOutputTimeline.CreatePcr27Mhz(nativeNow);
        var offset = MiPlayTabletOutputTimeline.CreateTimeOffset(nativeNow, pts, 1_000_000);

        Assert.Equal((ulong)nativeNow * 27, pcr);
        Assert.Equal(MiPlayTabletOutputTimeline.To90Khz(nativeNow), pcr / 300);
        Assert.Equal(288UL, pcr % 300);
        Assert.Equal(nativeNow + 1_000_000, offset + pts);
    }
}
