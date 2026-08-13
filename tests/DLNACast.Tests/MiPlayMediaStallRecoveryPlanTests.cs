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
    public void PreservesOneAacFrameGapWithoutMovingTheTimeline()
    {
        const long frequency = 1_000_000;

        Assert.Equal(
            21_333,
            MiPlayMediaStallRecoveryPlan.PreserveNominalGap(-900_000, frequency));
        Assert.Equal(
            21_333,
            MiPlayMediaStallRecoveryPlan.PreserveNominalGap(5_000, frequency));
        Assert.Equal(
            40_000,
            MiPlayMediaStallRecoveryPlan.PreserveNominalGap(40_000, frequency));
    }
}
