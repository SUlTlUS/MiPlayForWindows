using DLNACast.Core.MiPlay;
using DLNACast.Tests.References;

namespace DLNACast.Tests;

public sealed class MiPlaySharedOutputTimelineTests
{
    [Fact]
    public void EveryReceiverSharesOneRawPacketizationClockPerAccessUnit()
    {
        var timeline = new MiPlaySharedOutputTimeline();

        var firstReceiver = timeline.GetTimestamp(0, 4_000_000);
        var laterReceiver = timeline.GetTimestamp(0, 4_900_000);
        var nextAccessUnit = timeline.GetTimestamp(1, 4_021_333);

        Assert.Equal(108_000_000UL, firstReceiver.ProgramClockReference27Mhz);
        Assert.Equal(firstReceiver, laterReceiver);
        Assert.Equal(108_575_991UL, nextAccessUnit.ProgramClockReference27Mhz);
        Assert.NotEqual(
            MiPlayWfdMediaClock.CreateProgramClockReference27Mhz(4_021_333, 1_000_000),
            nextAccessUnit.ProgramClockReference27Mhz);
    }

    [Fact]
    public void StartsAtFirstMediaHandoffNotAtEncoderPrewarm()
    {
        var timeline = new MiPlaySharedOutputTimeline();

        Assert.Equal(0UL, timeline.GetTimestamp(0, 4_000_000).PresentationTimestamp90Khz);
        Assert.Equal(1_919UL, timeline.GetTimestamp(1, 4_000_000).PresentationTimestamp90Khz);
        Assert.Equal(3_839UL, timeline.GetTimestamp(2, 4_000_812).PresentationTimestamp90Khz);
        // Advancing capture age is deliberately not an input to this clock.
        Assert.Equal(5_760UL, timeline.GetTimestamp(3, 4_000_812).PresentationTimestamp90Khz);
    }

    [Fact]
    public void EveryReceiverGetsTheSameCorrectionEvenWhenItsThreadRunsLate()
    {
        var timeline = new MiPlaySharedOutputTimeline();
        timeline.GetTimestamp(0, 1_000_000);

        var first = timeline.GetTimestamp(1, 1_100_000);
        var second = timeline.GetTimestamp(1, 1_125_000);
        var third = timeline.GetTimestamp(1, 1_130_000);

        Assert.Equal(first, second);
        Assert.Equal(first, third);
        Assert.True(first.Corrected);
        Assert.Equal(9_000UL, first.PresentationTimestamp90Khz);
        Assert.Equal(1, first.CorrectionCount);
        Assert.Equal(78_667UL, first.TotalCorrectionMicroseconds);
    }

    [Theory]
    [InlineData(49, false)]
    [InlineData(50, false)]
    [InlineData(51, true)]
    public void MatchesTabletStrictFiftyMicrosecondThreshold(ulong lag, bool corrected)
    {
        var timeline = new MiPlaySharedOutputTimeline();
        var reference = new MiPlayTabletOutputTimeline();
        timeline.GetTimestamp(0, 1_000_000);
        reference.Map(0, 1_000_000);

        var actual = timeline.GetTimestamp(1, 1_021_333 + lag);
        var expected = reference.Map(21_333, checked((long)(1_021_333 + lag)));

        Assert.Equal(corrected, actual.Corrected);
        Assert.Equal(MiPlayTabletOutputTimeline.To90Khz(expected), actual.PresentationTimestamp90Khz);
    }

    [Fact]
    public void DoesNotCorrectTheSameTransportStallTwice()
    {
        var timeline = new MiPlaySharedOutputTimeline();
        timeline.GetTimestamp(0, 1_000_000);
        // A socket blocked after this timestamp was calculated. Existing
        // transport recovery advances the packetizers by 500 ms.
        Assert.Equal(45_000U, timeline.RecoverAfterSend(0, 1_500_000));

        var next = timeline.GetTimestamp(1, 1_521_333);
        Assert.False(next.Corrected);
        Assert.Equal(1_919UL, next.PresentationTimestamp90Khz);
        Assert.Equal(0, next.CorrectionCount);

        // A slower receiver can finish its recovery after the first one has
        // already obtained the next AU. The shared advance is still single-use.
        Assert.Equal(45_000U, timeline.RecoverAfterSend(0, 1_500_000));
        Assert.False(timeline.GetTimestamp(2, 1_542_666).Corrected);
        Assert.Throws<InvalidOperationException>(() => timeline.RecoverAfterSend(0, 2_000_000));
    }

    [Fact]
    public void CorrectedOutputSuppressesTheExistingAdditionalTimelineAdvance()
    {
        var timeline = new MiPlaySharedOutputTimeline();
        timeline.GetTimestamp(0, 1_000_000);
        var recovered = timeline.GetTimestamp(1, 1_500_000);

        Assert.True(recovered.Corrected);
        Assert.Equal(0U, timeline.RecoverAfterSend(1, 1_500_000));
        Assert.Equal(46_919UL, timeline.GetTimestamp(2, 1_521_333).PresentationTimestamp90Khz);
    }

    [Fact]
    public void APreWriteCorrectionDoesNotHideASecondStallInsideTheSocketWrite()
    {
        var timeline = new MiPlaySharedOutputTimeline();
        timeline.GetTimestamp(0, 1_000_000);
        Assert.True(timeline.GetTimestamp(1, 1_500_000).Corrected);

        // Output was already corrected, but the actual write then took 350 ms.
        var advance = timeline.RecoverAfterSend(1, 1_850_000);
        Assert.Equal(31_500U, advance);
        var next = timeline.GetTimestamp(2, 1_871_333);
        Assert.False(next.Corrected);
        Assert.Equal(78_419UL, next.PresentationTimestamp90Khz + advance);
    }

    [Fact]
    public void BlockedSendConsumesExistingPtsHeadroomBeforeAdvancingTheTimeline()
    {
        var timeline = new MiPlaySharedOutputTimeline();
        timeline.GetTimestamp(0, 1_000_000);
        timeline.GetTimestamp(1, 1_000_000);

        Assert.Equal(16_080U, timeline.RecoverAfterSend(1, 1_200_000));
        Assert.False(timeline.GetTimestamp(2, 1_221_333).Corrected);
    }

    [Fact]
    public void DoesNotAccumulateTruncatedMicrosecondsDuringThirtyFiveMinutes()
    {
        var timeline = new MiPlaySharedOutputTimeline();
        MiPlayOutputTimestamp last = default;
        for (var index = 0; index <= 100_000; index++)
        {
            var encoderTime = MiPlaySharedOutputTimeline.CalculateEncoderPresentationTimeMicroseconds(index);
            last = timeline.GetTimestamp(index, 10_000_000 + encoderTime);
        }

        Assert.Equal(191_999_999UL, last.PresentationTimestamp90Khz);
        Assert.Equal(0, last.CorrectionCount);
    }

    [Fact]
    public void AcceptsOnlyOrderedAccessUnitsAndAMonotonicOutputClock()
    {
        var timeline = new MiPlaySharedOutputTimeline();
        Assert.Throws<ArgumentOutOfRangeException>(() => timeline.GetTimestamp(-1, 0));
        Assert.Throws<InvalidOperationException>(() => timeline.GetTimestamp(1, 0));
        timeline.GetTimestamp(0, 1_000_000);
        Assert.Throws<InvalidOperationException>(() => timeline.GetTimestamp(2, 1_050_000));
        Assert.Throws<ArgumentOutOfRangeException>(() => timeline.GetTimestamp(1, 999_999));
        Assert.Throws<InvalidOperationException>(() => timeline.RecoverAfterSend(1, 1_000_000));
        Assert.Throws<ArgumentOutOfRangeException>(() => timeline.RecoverAfterSend(0, 999_999));
        Assert.Equal(1_919UL, timeline.GetTimestamp(1, 1_021_333).PresentationTimestamp90Khz);
    }

    [Fact]
    public void LateReceiverGetsExactRetainedPtsAndPcrWithoutRewindingTheClock()
    {
        var timeline = new MiPlaySharedOutputTimeline();
        MiPlayOutputTimestamp retained = default;
        for (var index = 0; index <= 70; index++)
        {
            var output = 4_000_000UL +
                MiPlaySharedOutputTimeline.CalculateEncoderPresentationTimeMicroseconds(index);
            var timestamp = timeline.GetTimestamp(index, output);
            if (index == 20)
            {
                retained = timestamp;
            }
        }

        Assert.Equal(retained, timeline.GetTimestamp(20, 99_000_000));
        Assert.Throws<InvalidOperationException>(() =>
            timeline.GetTimestamp(6, 99_000_000));
        Assert.Equal(
            136_319UL,
            timeline.GetTimestamp(71, 5_514_666).PresentationTimestamp90Khz);
    }
}
