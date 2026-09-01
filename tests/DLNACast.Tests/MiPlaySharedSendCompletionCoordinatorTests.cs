using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlaySharedSendCompletionCoordinatorTests
{
    [Fact]
    public async Task ReceiverDepartureReleasesRemainingReceiverAndFutureFrames()
    {
        var coordinator = new MiPlaySharedSendCompletionCoordinator(2);
        var remaining = coordinator.Register();
        var departed = coordinator.Register();

        var firstFrame = coordinator.SynchronizeAsync(
            remaining,
            accessUnitIndex: 0,
            mediaWriteCompletedAt: 1_000_000,
            sendGapMilliseconds: 0,
            mediaStartedAt: 100_000,
            stopwatchFrequency: 1_000_000,
            captureTimelineReanchored: false,
            CancellationToken.None);
        Assert.False(firstFrame.IsCompleted);

        coordinator.Unregister(departed);

        var firstResult = await firstFrame;
        Assert.Equal(0, firstResult.SendCompletionSkewMilliseconds);
        Assert.False(firstResult.TimelineRebased);

        var secondResult = await coordinator.SynchronizeAsync(
            remaining,
            accessUnitIndex: 1,
            mediaWriteCompletedAt: 1_021_333,
            sendGapMilliseconds: MiPlayWfdStartupPacingPlan.NominalAccessUnitMilliseconds,
            mediaStartedAt: firstResult.MediaStartedAt,
            stopwatchFrequency: 1_000_000,
            captureTimelineReanchored: false,
            CancellationToken.None);
        Assert.Equal(0, secondResult.SendCompletionSkewMilliseconds);
        Assert.False(secondResult.TimelineRebased);
    }

    [Fact]
    public async Task DepartedReceiverObservationIsExcludedFromGroupSkew()
    {
        var coordinator = new MiPlaySharedSendCompletionCoordinator(3);
        var first = coordinator.Register();
        var second = coordinator.Register();
        var departed = coordinator.Register();
        using var departedLifetime = new CancellationTokenSource();

        var firstCompletion = coordinator.SynchronizeAsync(
            first,
            accessUnitIndex: 0,
            mediaWriteCompletedAt: 1_000_000,
            sendGapMilliseconds: 0,
            mediaStartedAt: 100_000,
            stopwatchFrequency: 1_000_000,
            captureTimelineReanchored: false,
            CancellationToken.None);
        var departedCompletion = coordinator.SynchronizeAsync(
            departed,
            accessUnitIndex: 0,
            mediaWriteCompletedAt: 1_900_000,
            sendGapMilliseconds: 900,
            mediaStartedAt: 100_000,
            stopwatchFrequency: 1_000_000,
            captureTimelineReanchored: false,
            departedLifetime.Token);

        coordinator.Unregister(departed);
        departedLifetime.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => departedCompletion);

        var secondCompletion = coordinator.SynchronizeAsync(
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
            Assert.Equal(4, result.SendCompletionSkewMilliseconds, 3);
            Assert.False(result.TimelineRebased);
            Assert.Equal(1_004_000UL, result.ReferenceMonotonicMicroseconds);
        });
    }

    [Fact]
    public async Task ThreeReceiversCompleteOnlyAfterEveryActiveMemberArrives()
    {
        var coordinator = new MiPlaySharedSendCompletionCoordinator(3);
        var first = coordinator.Register();
        var second = coordinator.Register();
        var third = coordinator.Register();

        var firstCompletion = Synchronize(coordinator, first, 1_000_000);
        var secondCompletion = Synchronize(coordinator, second, 1_002_000);
        Assert.False(firstCompletion.IsCompleted);
        Assert.False(secondCompletion.IsCompleted);

        var thirdCompletion = Synchronize(coordinator, third, 1_005_000);
        var synchronized = await Task.WhenAll(
            firstCompletion,
            secondCompletion,
            thirdCompletion);

        Assert.All(synchronized, result =>
            Assert.Equal(5, result.SendCompletionSkewMilliseconds, 3));
    }

    [Fact]
    public async Task FutureMemberDoesNotBlockOldFrameAndJoinsItsDeclaredBoundary()
    {
        var coordinator = new MiPlaySharedSendCompletionCoordinator(2);
        var existing = coordinator.Register();
        var late = coordinator.Register(startAccessUnitIndex: 1);

        var oldFrame = await coordinator.SynchronizeAsync(
            existing,
            accessUnitIndex: 0,
            mediaWriteCompletedAt: 1_000_000,
            sendGapMilliseconds: 0,
            mediaStartedAt: 100_000,
            stopwatchFrequency: 1_000_000,
            captureTimelineReanchored: false,
            CancellationToken.None);
        Assert.Equal(0, oldFrame.SendCompletionSkewMilliseconds);

        var existingNext = coordinator.SynchronizeAsync(
            existing,
            accessUnitIndex: 1,
            mediaWriteCompletedAt: 1_021_333,
            sendGapMilliseconds: MiPlayWfdStartupPacingPlan.NominalAccessUnitMilliseconds,
            mediaStartedAt: oldFrame.MediaStartedAt,
            stopwatchFrequency: 1_000_000,
            captureTimelineReanchored: false,
            CancellationToken.None);
        Assert.False(existingNext.IsCompleted);

        var lateNext = coordinator.SynchronizeAsync(
            late,
            accessUnitIndex: 1,
            mediaWriteCompletedAt: 1_024_333,
            sendGapMilliseconds: MiPlayWfdStartupPacingPlan.NominalAccessUnitMilliseconds,
            mediaStartedAt: oldFrame.MediaStartedAt,
            stopwatchFrequency: 1_000_000,
            captureTimelineReanchored: false,
            CancellationToken.None);
        var joined = await Task.WhenAll(existingNext, lateNext);
        Assert.All(joined, result =>
            Assert.Equal(3, result.SendCompletionSkewMilliseconds, 3));
    }

    [Fact]
    public void CapacityLimitsConcurrentMembersAndAllowsRepeatedReplacement()
    {
        var coordinator = new MiPlaySharedSendCompletionCoordinator(2);
        var permanent = coordinator.Register();
        var replaced = coordinator.Register();
        Assert.Throws<InvalidOperationException>(() => coordinator.Register());

        coordinator.Unregister(replaced);
        var firstReplacement = coordinator.Register(startAccessUnitIndex: 4);
        coordinator.Unregister(firstReplacement);
        var secondReplacement = coordinator.Register(startAccessUnitIndex: 9);

        Assert.True(firstReplacement.ParticipantId > permanent.ParticipantId);
        Assert.True(secondReplacement.ParticipantId > firstReplacement.ParticipantId);
    }

    private static Task<MiPlaySharedSendCompletion> Synchronize(
        MiPlaySharedSendCompletionCoordinator coordinator,
        MiPlaySharedSendParticipant participant,
        long completedAt) =>
        coordinator.SynchronizeAsync(
            participant,
            accessUnitIndex: 0,
            mediaWriteCompletedAt: completedAt,
            sendGapMilliseconds: 0,
            mediaStartedAt: 100_000,
            stopwatchFrequency: 1_000_000,
            captureTimelineReanchored: false,
            CancellationToken.None);
}
