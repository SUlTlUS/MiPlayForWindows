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
}
