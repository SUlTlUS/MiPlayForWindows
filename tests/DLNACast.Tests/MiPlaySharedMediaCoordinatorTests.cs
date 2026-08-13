using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlaySharedMediaCoordinatorTests
{
    [Fact]
    public async Task PairUsesOneTimeOffsetOneStartClockAndOneAacStream()
    {
        var coordinator = new MiPlaySharedMediaCoordinator(participantCount: 2);

        var firstTimeOffset = coordinator.SynchronizeTimeOffsetAsync(
            participantId: 1,
            candidate: 58_878_859_700,
            CancellationToken.None);
        Assert.False(firstTimeOffset.IsCompleted);
        var secondTimeOffset = coordinator.SynchronizeTimeOffsetAsync(
            participantId: 2,
            candidate: 58_878_859_721,
            CancellationToken.None);
        var synchronizedTimeOffsets = await Task.WhenAll(firstTimeOffset, secondTimeOffset);
        Assert.Equal([58_878_859_721UL, 58_878_859_721UL], synchronizedTimeOffsets);

        var firstMediaStart = coordinator.SynchronizeMediaAsync(
            participantId: 1,
            synchronizedTimeOffsets[0],
            CancellationToken.None);
        Assert.False(firstMediaStart.IsCompleted);
        var secondMediaStart = coordinator.SynchronizeMediaAsync(
            participantId: 2,
            synchronizedTimeOffsets[1],
            CancellationToken.None);
        var mediaStarts = await Task.WhenAll(firstMediaStart, secondMediaStart);
        Assert.Equal(mediaStarts[0], mediaStarts[1]);

        var accessUnit = new byte[] { 0xff, 0xf9, 0x50, 0x80 };
        var follower = coordinator.SynchronizeAccessUnitAsync(
            participantId: 2,
            accessUnitIndex: 0,
            leaderAccessUnit: null,
            CancellationToken.None);
        Assert.False(follower.IsCompleted);
        var leader = coordinator.SynchronizeAccessUnitAsync(
            participantId: 1,
            accessUnitIndex: 0,
            leaderAccessUnit: accessUnit,
            CancellationToken.None);
        var synchronizedAccessUnits = await Task.WhenAll(leader, follower);
        Assert.Same(accessUnit, synchronizedAccessUnits[0]);
        Assert.Same(accessUnit, synchronizedAccessUnits[1]);
    }

    [Fact]
    public async Task PairKeepsTheSameAccessUnitIndexAcrossAThirtyFiveMinuteRun()
    {
        var coordinator = await CreateReadyCoordinatorAsync();
        var accessUnit = new byte[] { 0xff, 0xf9, 0x50, 0x80 };

        for (var index = 0; index < 100_000; index++)
        {
            var follower = coordinator.SynchronizeAccessUnitAsync(
                participantId: 2,
                index,
                leaderAccessUnit: null,
                CancellationToken.None);
            var leader = coordinator.SynchronizeAccessUnitAsync(
                participantId: 1,
                index,
                leaderAccessUnit: accessUnit,
                CancellationToken.None);
            var synchronized = await Task.WhenAll(leader, follower);
            Assert.Same(accessUnit, synchronized[0]);
            Assert.Same(accessUnit, synchronized[1]);
        }
    }

    [Fact]
    public async Task CancelingOneReceiverFailsTheOtherInsteadOfLeavingItBlocked()
    {
        var coordinator = await CreateReadyCoordinatorAsync();
        using var canceledReceiver = new CancellationTokenSource();
        var follower = coordinator.SynchronizeAccessUnitAsync(
            participantId: 2,
            accessUnitIndex: 0,
            leaderAccessUnit: null,
            canceledReceiver.Token);

        canceledReceiver.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => follower);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.SynchronizeAccessUnitAsync(
                participantId: 1,
                accessUnitIndex: 0,
                leaderAccessUnit: new byte[] { 0xff, 0xf9 },
                CancellationToken.None));
    }

    private static async Task<MiPlaySharedMediaCoordinator> CreateReadyCoordinatorAsync()
    {
        var coordinator = new MiPlaySharedMediaCoordinator(participantCount: 2);
        const ulong timeOffset = 58_878_859_721;
        await Task.WhenAll(
            coordinator.SynchronizeTimeOffsetAsync(1, timeOffset, CancellationToken.None),
            coordinator.SynchronizeTimeOffsetAsync(2, timeOffset, CancellationToken.None));
        await Task.WhenAll(
            coordinator.SynchronizeMediaAsync(1, timeOffset, CancellationToken.None),
            coordinator.SynchronizeMediaAsync(2, timeOffset, CancellationToken.None));
        return coordinator;
    }
}
