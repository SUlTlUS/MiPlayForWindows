using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayWfdStartupPacingPlanTests
{
    [Fact]
    public void PinsCleanPhoneStartupBurstThenNominalCadence()
    {
        Assert.Equal(19, MiPlayWfdStartupPacingPlan.CapturedAccessUnitCount);
        Assert.Equal(0, MiPlayWfdStartupPacingPlan.GetDueAfterMilliseconds(0));
        Assert.Equal(0, MiPlayWfdStartupPacingPlan.GetDueAfterMilliseconds(1));
        Assert.Equal(20.360, MiPlayWfdStartupPacingPlan.GetDueAfterMilliseconds(6), 3);
        Assert.Equal(84.784, MiPlayWfdStartupPacingPlan.GetDueAfterMilliseconds(12), 3);
        Assert.Equal(211.654, MiPlayWfdStartupPacingPlan.GetDueAfterMilliseconds(18), 3);
        Assert.Equal(
            211.654 + 2 * MiPlayWfdStartupPacingPlan.NominalAccessUnitMilliseconds,
            MiPlayWfdStartupPacingPlan.GetDueAfterMilliseconds(20),
            6);
    }

    [Fact]
    public void RejectsNegativeAccessUnitIndex()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MiPlayWfdStartupPacingPlan.GetDueAfterMilliseconds(-1));
    }
}
