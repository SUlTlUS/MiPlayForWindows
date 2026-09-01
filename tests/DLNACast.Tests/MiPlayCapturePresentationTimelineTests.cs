using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayCapturePresentationTimelineTests
{
    [Fact]
    public void UsesCapturedMonotonicPresentationTimesForNormalFrames()
    {
        var timeline = new MiPlayCapturePresentationTimeline();

        Assert.Equal(0U, timeline.Map(1_000_000));
        Assert.Equal(1_919U, timeline.Map(1_021_333));
        Assert.Equal(3_839U, timeline.Map(1_042_666));
    }

    [Fact]
    public void ReanchorsRecoveryWithoutJumpingTheReceiverTimeline()
    {
        var timeline = new MiPlayCapturePresentationTimeline();

        Assert.Equal(0U, timeline.Map(1_000_000));
        Assert.Equal(1_919U, timeline.Map(1_021_333));
        Assert.Equal(3_839U, timeline.Map(1_136_000, reanchorPresentationTimeline: true));
        Assert.Equal(5_758U, timeline.Map(1_157_333));
        Assert.Equal(7_678U, timeline.Map(1_178_666));
        Assert.Equal(9_599U, timeline.Map(1_200_000));
    }

    [Fact]
    public void InternalDroppedFramesReanchorWithoutAddingRecoverySilence()
    {
        var timeline = new MiPlayCapturePresentationTimeline();

        Assert.Equal(0U, timeline.Map(1_000_000));
        Assert.Equal(1_920U, timeline.Map(1_200_000));
        Assert.Equal(3_839U, timeline.Map(1_221_333));
    }
}
