using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayEncoderOutputGateTests
{
    [Fact]
    public void ArmsOnlyOnceAfterPrePlayDrainCompletes()
    {
        var gate = new MiPlayEncoderOutputGate();

        Assert.False(gate.IsArmed);
        Assert.False(gate.ShouldPublish(999_999));
        Assert.True(gate.TryArm(1_000_000));
        Assert.True(gate.IsArmed);
        Assert.False(gate.ShouldPublish(999_999));
        Assert.True(gate.ShouldPublish(1_000_000));
        Assert.True(gate.ShouldPublish(1_021_333));
        Assert.False(gate.TryArm(2_000_000));
        Assert.True(gate.IsArmed);
        Assert.True(gate.ShouldPublish(1_500_000));
    }
}
