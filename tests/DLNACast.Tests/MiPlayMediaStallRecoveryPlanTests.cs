using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayMediaStallRecoveryPlanTests
{
    [Fact]
    public void DoesNotActivateDuringStartupOrOrdinaryJitter()
    {
        Assert.False(MiPlayMediaStallRecoveryPlan.ShouldActivate(249, 906.512));
        Assert.False(MiPlayMediaStallRecoveryPlan.ShouldActivate(250, 249.999));
        Assert.False(MiPlayMediaStallRecoveryPlan.ShouldActivate(10_000, 110.957));
    }

    [Fact]
    public void ActivatesForTheObservedLongRunningSendStall()
    {
        Assert.True(MiPlayMediaStallRecoveryPlan.ShouldActivate(11_675, 906.512));
    }

    [Fact]
    public void RebasesOneAacFrameAheadWithoutPermanentPerFrameDelay()
    {
        const long frequency = 1_000_000;
        const long writeCompletedAt = 9_000_000;
        const long accessUnitsSent = 11_676;
        var dueAfterAccessUnits = (long)Math.Round(
            MiPlayWfdStartupPacingPlan.GetDueAfterMilliseconds(accessUnitsSent) *
            frequency /
            1_000d);

        Assert.Equal(
            writeCompletedAt + 21_333 - dueAfterAccessUnits,
            MiPlayMediaStallRecoveryPlan.RebaseMediaStartedAt(
                writeCompletedAt,
                accessUnitsSent,
                frequency));
    }

    [Fact]
    public void RejectsInvalidRebaseInputs()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MiPlayMediaStallRecoveryPlan.RebaseMediaStartedAt(-1, 1, 1_000_000));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MiPlayMediaStallRecoveryPlan.RebaseMediaStartedAt(1, 0, 1_000_000));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MiPlayMediaStallRecoveryPlan.RebaseMediaStartedAt(1, 1, 0));
    }

    [Fact]
    public void ConvertsACommonPacingRebaseToACommonRtpTimelineAdvance()
    {
        Assert.Equal(
            81_000U,
            MiPlayMediaStallRecoveryPlan.CalculateTimelineAdvance90Khz(
                previousMediaStartedAt: 1_000_000,
                rebasedMediaStartedAt: 1_900_000,
                stopwatchFrequency: 1_000_000));
        Assert.Equal(
            0U,
            MiPlayMediaStallRecoveryPlan.CalculateTimelineAdvance90Khz(
                previousMediaStartedAt: 1_900_000,
                rebasedMediaStartedAt: 1_900_000,
                stopwatchFrequency: 1_000_000));
    }

    [Fact]
    public void CaptureReanchorRebasesPacingWithoutAdvancingRtpTwice()
    {
        Assert.Equal(
            0U,
            MiPlayMediaStallRecoveryPlan.CalculateTimelineAdvance90Khz(
                previousMediaStartedAt: 1_000_000,
                rebasedMediaStartedAt: 1_900_000,
                stopwatchFrequency: 1_000_000,
                captureTimelineReanchored: true));
    }

    [Fact]
    public void RebuildsRuntimeTimeOffsetFromTheCurrentMediaPosition()
    {
        Assert.Equal(
            9_000_000UL,
            MiPlayMediaStallRecoveryPlan.CalculateRuntimeTimeOffsetMicroseconds(
                currentMonotonicMicroseconds: 10_000_000,
                mediaPosition90Khz: 180_000,
                playbackDelayMicroseconds: 1_000_000));
    }

    [Fact]
    public void RuntimeTimeOffsetRejectsAClockBeforeTheMediaPosition()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MiPlayMediaStallRecoveryPlan.CalculateRuntimeTimeOffsetMicroseconds(
                currentMonotonicMicroseconds: 999_999,
                mediaPosition90Khz: 90_000,
                playbackDelayMicroseconds: 1_000_000));
    }
}
