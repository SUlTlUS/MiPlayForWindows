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
        var leftAccessUnit = new byte[] { 0x11, 0x12, 0x13 };
        var rightAccessUnit = new byte[] { 0x21, 0x22, 0x23 };

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
        Assert.Same(leftAccessUnit, synchronized[0]);
        Assert.Same(leftAccessUnit, synchronized[1]);
        Assert.Same(rightAccessUnit, synchronized[2]);
        Assert.Same(rightAccessUnit, synchronized[3]);
    }

    [Fact]
    public async Task SingleSpeakerChannelGroupDoesNotWaitForAnotherReceiver()
    {
        var coordinator = new MiPlaySharedAccessUnitCoordinator(
            new Dictionary<AudioChannelRoute, int>
            {
                [AudioChannelRoute.LeftAsMono] = 1,
                [AudioChannelRoute.RightAsMono] = 1,
            });
        var left = coordinator.Register(AudioChannelRoute.LeftAsMono);
        var right = coordinator.Register(AudioChannelRoute.RightAsMono);

        var leftUnit = new byte[] { 0x31 };
        var rightUnit = new byte[] { 0x41 };

        Assert.Same(
            leftUnit,
            await coordinator.SynchronizeAsync(left, 0, leftUnit, CancellationToken.None));
        Assert.Same(
            rightUnit,
            await coordinator.SynchronizeAsync(right, 0, rightUnit, CancellationToken.None));
    }
}
