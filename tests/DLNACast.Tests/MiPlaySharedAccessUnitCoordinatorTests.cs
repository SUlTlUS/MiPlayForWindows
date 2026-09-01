using DLNACast.Core.Audio;
using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlaySharedAccessUnitCoordinatorTests
{
    [Fact]
    public async Task LeftAndRightGroupsShareOnlyTheirOwnEncodedAccessUnit()
    {
        var coordinator = new MiPlaySharedAccessUnitCoordinator(
            new Dictionary<AudioChannelRoute, int>
            {
                [AudioChannelRoute.LeftAsMono] = 2,
                [AudioChannelRoute.RightAsMono] = 2,
            });
        var leftLeader = coordinator.Register(AudioChannelRoute.LeftAsMono);
        var leftFollower = coordinator.Register(AudioChannelRoute.LeftAsMono);
        var rightLeader = coordinator.Register(AudioChannelRoute.RightAsMono);
        var rightFollower = coordinator.Register(AudioChannelRoute.RightAsMono);
        var leftBytes = new byte[] { 0x11, 0x12, 0x13 };
        var rightBytes = new byte[] { 0x21, 0x22, 0x23 };
        var leftAccessUnit = new MiPlayEncodedAccessUnit(leftBytes, 103);
        var rightAccessUnit = new MiPlayEncodedAccessUnit(rightBytes, 104);

        var leftFollowerTask = coordinator.SynchronizeAsync(
            leftFollower, 0, null, CancellationToken.None);
        var rightFollowerTask = coordinator.SynchronizeAsync(
            rightFollower, 0, null, CancellationToken.None);
        var leftLeaderTask = coordinator.SynchronizeAsync(
            leftLeader, 0, leftAccessUnit, CancellationToken.None);
        var rightLeaderTask = coordinator.SynchronizeAsync(
            rightLeader, 0, rightAccessUnit, CancellationToken.None);

        var synchronized = await Task.WhenAll(
            leftLeaderTask,
            leftFollowerTask,
            rightLeaderTask,
            rightFollowerTask);
        Assert.Same(leftBytes, synchronized[0].Bytes);
        Assert.Same(leftBytes, synchronized[1].Bytes);
        Assert.Same(rightBytes, synchronized[2].Bytes);
        Assert.Same(rightBytes, synchronized[3].Bytes);
        Assert.All(synchronized, frame =>
            Assert.Equal(104UL, frame.PresentationTimeMicroseconds));
    }

    [Fact]
    public async Task LeftAndRightChannelsWaitForTheSameFrameBoundary()
    {
        var coordinator = new MiPlaySharedAccessUnitCoordinator(
            new Dictionary<AudioChannelRoute, int>
            {
                [AudioChannelRoute.LeftAsMono] = 1,
                [AudioChannelRoute.RightAsMono] = 1,
            });
        var left = coordinator.Register(AudioChannelRoute.LeftAsMono);
        var right = coordinator.Register(AudioChannelRoute.RightAsMono);

        var leftBytes = new byte[] { 0x31 };
        var rightBytes = new byte[] { 0x41 };
        var leftUnit = new MiPlayEncodedAccessUnit(leftBytes, 201);
        var rightUnit = new MiPlayEncodedAccessUnit(rightBytes, 202);

        var leftFrame = coordinator.SynchronizeAsync(
            left, 0, leftUnit, CancellationToken.None);
        Assert.False(leftFrame.IsCompleted);
        var rightFrame = coordinator.SynchronizeAsync(
            right, 0, rightUnit, CancellationToken.None);

        var synchronized = await Task.WhenAll(leftFrame, rightFrame);
        Assert.Same(leftBytes, synchronized[0].Bytes);
        Assert.Same(rightBytes, synchronized[1].Bytes);
        Assert.All(synchronized, frame =>
            Assert.Equal(202UL, frame.PresentationTimeMicroseconds));
    }

    [Fact]
    public async Task ReceiversShareSendCompletionAndMeasureSameFrameSkew()
    {
        var coordinator = new MiPlaySharedAccessUnitCoordinator(
            new Dictionary<AudioChannelRoute, int>
            {
                [AudioChannelRoute.Stereo] = 2,
            });
        var first = coordinator.Register(AudioChannelRoute.Stereo);
        var second = coordinator.Register(AudioChannelRoute.Stereo);

        var firstCompletion = coordinator.SynchronizeSendCompletionAsync(
            first,
            accessUnitIndex: 0,
            mediaWriteCompletedAt: 1_000_000,
            sendGapMilliseconds: 0,
            mediaStartedAt: 100_000,
            stopwatchFrequency: 1_000_000,
            captureTimelineReanchored: false,
            CancellationToken.None);
        Assert.False(firstCompletion.IsCompleted);
        var secondCompletion = coordinator.SynchronizeSendCompletionAsync(
            second,
            accessUnitIndex: 0,
            mediaWriteCompletedAt: 1_004_000,
            sendGapMilliseconds: 0,
            mediaStartedAt: 100_000,
            stopwatchFrequency: 1_000_000,
            captureTimelineReanchored: false,
            CancellationToken.None);

        var synchronized = await Task.WhenAll(firstCompletion, secondCompletion);
        Assert.All(synchronized, result =>
        {
            Assert.Equal(100_000, result.MediaStartedAt);
            Assert.Equal(4, result.SendCompletionSkewMilliseconds, 3);
            Assert.False(result.TimelineRebased);
            Assert.Equal(0U, result.TimelineAdvance90Khz);
            Assert.Equal(1_004_000UL, result.ReferenceMonotonicMicroseconds);
        });
    }

    [Fact]
    public async Task OneReceiverStallRebasesTheWholeGroupOnce()
    {
        var coordinator = new MiPlaySharedAccessUnitCoordinator(
            new Dictionary<AudioChannelRoute, int>
            {
                [AudioChannelRoute.Stereo] = 2,
            });
        var first = coordinator.Register(AudioChannelRoute.Stereo);
        var second = coordinator.Register(AudioChannelRoute.Stereo);
        const long frequency = 1_000_000;
        long mediaStartedAt = 100_000;

        for (var index = 0L; index <= MiPlayMediaStallRecoveryPlan.MinimumAccessUnitIndex; index++)
        {
            var nominalCompletion = mediaStartedAt + (long)Math.Round(
                MiPlayWfdStartupPacingPlan.GetDueAfterMilliseconds(index) *
                frequency /
                1_000d);
            var stalledFrame = index == MiPlayMediaStallRecoveryPlan.MinimumAccessUnitIndex;
            var firstCompletion = coordinator.SynchronizeSendCompletionAsync(
                first,
                index,
                nominalCompletion,
                stalledFrame ? 300 : MiPlayWfdStartupPacingPlan.NominalAccessUnitMilliseconds,
                mediaStartedAt,
                frequency,
                captureTimelineReanchored: false,
                CancellationToken.None);
            var secondCompletion = coordinator.SynchronizeSendCompletionAsync(
                second,
                index,
                nominalCompletion + (stalledFrame ? 300_000 : 2_000),
                MiPlayWfdStartupPacingPlan.NominalAccessUnitMilliseconds,
                mediaStartedAt,
                frequency,
                captureTimelineReanchored: false,
                CancellationToken.None);
            var synchronized = await Task.WhenAll(firstCompletion, secondCompletion);
            Assert.Equal(synchronized[0], synchronized[1]);
            if (stalledFrame)
            {
                Assert.True(synchronized[0].TimelineRebased);
                Assert.True(synchronized[0].TimelineAdvance90Khz > 0);
                Assert.Equal(300, synchronized[0].SendCompletionSkewMilliseconds, 3);
                Assert.True(synchronized[0].MediaStartedAt > mediaStartedAt);
                Assert.Equal(
                    (ulong)(nominalCompletion + 300_000),
                    synchronized[0].ReferenceMonotonicMicroseconds);
            }
            else
            {
                Assert.False(synchronized[0].TimelineRebased);
            }
            mediaStartedAt = synchronized[0].MediaStartedAt;
        }
    }
}
