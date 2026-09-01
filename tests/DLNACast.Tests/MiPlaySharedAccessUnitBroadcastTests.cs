using DLNACast.Core.Audio;
using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlaySharedAccessUnitBroadcastTests
{
    [Fact]
    public async Task SameRouteReceiversTakeTheSameEncodedObjectBeforePublisherAdvances()
    {
        var broadcast = new MiPlaySharedAccessUnitBroadcast(2);
        var first = broadcast.Register(AudioChannelRoute.Stereo);
        var second = broadcast.Register(AudioChannelRoute.Stereo);
        var bytes = new byte[] { 0x11, 0x12, 0x13 };

        var publish = broadcast.PublishAsync(
            new Dictionary<AudioChannelRoute, MiPlayEncodedAccessUnit>
            {
                [AudioChannelRoute.Stereo] = new(bytes, 123_456),
            },
            CancellationToken.None);

        var firstRead = await broadcast.ReadAsync(first, CancellationToken.None);
        Assert.Same(bytes, firstRead.Bytes);
        Assert.False(publish.IsCompleted);

        var secondRead = await broadcast.ReadAsync(second, CancellationToken.None);
        Assert.Same(bytes, secondRead.Bytes);
        await publish;
        Assert.Equal(0, broadcast.BufferedCount(first));
        Assert.Equal(0, broadcast.BufferedCount(second));
    }

    [Fact]
    public async Task LeftAndRightReceiversTakeTheirOwnRouteFromOneFrameBoundary()
    {
        var broadcast = new MiPlaySharedAccessUnitBroadcast(2);
        var left = broadcast.Register(AudioChannelRoute.LeftAsMono);
        var right = broadcast.Register(AudioChannelRoute.RightAsMono);
        var leftBytes = new byte[] { 0x21 };
        var rightBytes = new byte[] { 0x31 };

        var publish = broadcast.PublishAsync(
            new Dictionary<AudioChannelRoute, MiPlayEncodedAccessUnit>
            {
                [AudioChannelRoute.LeftAsMono] = new(leftBytes, 200_000),
                [AudioChannelRoute.RightAsMono] = new(rightBytes, 200_000),
            },
            CancellationToken.None);

        var frames = await Task.WhenAll(
            broadcast.ReadAsync(left, CancellationToken.None).AsTask(),
            broadcast.ReadAsync(right, CancellationToken.None).AsTask());
        await publish;

        Assert.Same(leftBytes, frames[0].Bytes);
        Assert.Same(rightBytes, frames[1].Bytes);
        Assert.All(frames, frame =>
            Assert.Equal(200_000UL, frame.PresentationTimeMicroseconds));
    }

    [Fact]
    public async Task DepartedReceiverReleasesCurrentFrameAndRemainingReceiverContinues()
    {
        var broadcast = new MiPlaySharedAccessUnitBroadcast(2);
        var remaining = broadcast.Register(AudioChannelRoute.Stereo);
        var departed = broadcast.Register(AudioChannelRoute.Stereo);
        var firstBytes = new byte[] { 0x41 };

        var firstPublish = broadcast.PublishAsync(
            StereoFrame(firstBytes, 300_000),
            CancellationToken.None);
        var firstRead = await broadcast.ReadAsync(remaining, CancellationToken.None);
        Assert.False(firstPublish.IsCompleted);

        broadcast.Unregister(departed);

        await firstPublish;
        Assert.Same(firstBytes, firstRead.Bytes);

        var secondBytes = new byte[] { 0x42 };
        var secondPublish = broadcast.PublishAsync(
            StereoFrame(secondBytes, 321_333),
            CancellationToken.None);
        var secondRead = await broadcast.ReadAsync(remaining, CancellationToken.None);
        await secondPublish;
        Assert.Same(secondBytes, secondRead.Bytes);
    }

    [Fact]
    public async Task PublisherCannotQueueASecondFrameAheadOfReceivers()
    {
        var broadcast = new MiPlaySharedAccessUnitBroadcast(2);
        var first = broadcast.Register(AudioChannelRoute.Stereo);
        var second = broadcast.Register(AudioChannelRoute.Stereo);
        var firstPublish = broadcast.PublishAsync(
            StereoFrame([0x51], 400_000),
            CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            broadcast.PublishAsync(
                StereoFrame([0x52], 421_333),
                CancellationToken.None));

        _ = await broadcast.ReadAsync(first, CancellationToken.None);
        _ = await broadcast.ReadAsync(second, CancellationToken.None);
        await firstPublish;
    }

    [Fact]
    public async Task LateReceiverReplaysRetainedRunwayThenJoinsTheNextLiveBoundary()
    {
        var broadcast = new MiPlaySharedAccessUnitBroadcast(2);
        var existing = broadcast.Register(AudioChannelRoute.Stereo);

        for (var index = 0; index < 55; index++)
        {
            var publish = broadcast.PublishAsync(
                StereoFrame([(byte)index], (ulong)index * 21_333),
                CancellationToken.None);
            var live = await broadcast.ReadIndexedAsync(existing, CancellationToken.None);
            Assert.Equal(index, live.AccessUnitIndex);
            Assert.True(live.ParticipatesInSendBarrier);
            await publish;
        }

        var retained = await broadcast.SnapshotHistoryAsync(
            AudioChannelRoute.Stereo,
            MiPlaySharedStartupAccessUnitBuffer.LateJoinRequiredAccessUnitCount,
            CancellationToken.None);
        Assert.Equal(Enumerable.Range(8, 47).Select(index => (long)index),
            retained.Select(frame => frame.AccessUnitIndex));
        Assert.All(retained, frame => Assert.False(frame.ParticipatesInSendBarrier));

        long exposedJoinIndex = -1;
        var late = broadcast.Register(
            AudioChannelRoute.Stereo,
            index => exposedJoinIndex = index);
        Assert.Equal(55, exposedJoinIndex);
        Assert.Equal(55, late.StartAccessUnitIndex);

        var nextPublish = broadcast.PublishAsync(
            StereoFrame([0x20], 55 * 21_333UL),
            CancellationToken.None);
        var next = await Task.WhenAll(
            broadcast.ReadIndexedAsync(existing, CancellationToken.None).AsTask(),
            broadcast.ReadIndexedAsync(late, CancellationToken.None).AsTask());
        await nextPublish;

        Assert.All(next, frame =>
        {
            Assert.Equal(55, frame.AccessUnitIndex);
            Assert.True(frame.ParticipatesInSendBarrier);
        });
    }

    [Fact]
    public void CapacityLimitsActiveReceiversNotLifetimeRegistrations()
    {
        var broadcast = new MiPlaySharedAccessUnitBroadcast(2);
        var permanent = broadcast.Register(AudioChannelRoute.Stereo);
        var replaced = broadcast.Register(AudioChannelRoute.Stereo);
        Assert.Throws<InvalidOperationException>(() =>
            broadcast.Register(AudioChannelRoute.Stereo));

        broadcast.Unregister(replaced);
        var firstReplacement = broadcast.Register(AudioChannelRoute.Stereo);
        broadcast.Unregister(firstReplacement);
        var secondReplacement = broadcast.Register(AudioChannelRoute.Stereo);

        Assert.True(firstReplacement.ParticipantId > permanent.ParticipantId);
        Assert.True(secondReplacement.ParticipantId > firstReplacement.ParticipantId);
    }

    private static IReadOnlyDictionary<AudioChannelRoute, MiPlayEncodedAccessUnit> StereoFrame(
        byte[] bytes,
        ulong presentationTimeMicroseconds) =>
        new Dictionary<AudioChannelRoute, MiPlayEncodedAccessUnit>
        {
            [AudioChannelRoute.Stereo] = new(bytes, presentationTimeMicroseconds),
        };
}
