using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayLegacyResumeRepetitionPlanTests
{
    [Fact]
    public void ReproducesTheThreeCapturedPostStateTwoResumeWrites()
    {
        var plan = MiPlayLegacyResumeRepetitionPlan.Create();

        Assert.Equal([69, 98, 105], plan.Select(item => item.DueAfterInitialResumeMilliseconds));
        Assert.Equal([(ushort)18, (ushort)19, (ushort)20], plan.Select(item => item.Sequence));
        Assert.All(plan, item =>
        {
            Assert.True(MiPlayCommandFrameCodec.TryDecode(
                item.CommandFrame,
                out var frame,
                out var consumed));
            Assert.NotNull(frame);
            Assert.Equal(item.CommandFrame.Length, consumed);
            Assert.Equal(MiPlayProtocolConstants.ResumeCommand, frame.Command);
            Assert.Equal(item.Sequence, frame.Sequence);
            Assert.Empty(frame.Payload);
        });
    }
}
