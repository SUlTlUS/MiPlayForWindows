using DLNACast.Core.Audio;
using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayDynamicLateJoinReplayTests
{
    [Fact]
    public async Task OneSecondHistoryFeedsANewSenderBeforeTheNextSharedLiveBoundary()
    {
        var broadcast = new MiPlaySharedAccessUnitBroadcast(3);
        var sends = new MiPlaySharedSendCompletionCoordinator(3);
        var timeline = new MiPlaySharedOutputTimeline();
        var first = broadcast.Register(AudioChannelRoute.Stereo);
        var second = broadcast.Register(AudioChannelRoute.Stereo);
        var firstSend = sends.Register();
        var secondSend = sends.Register();
        var firstPacketizer = new MiPlayWfdAudioPacketizer();
        var secondPacketizer = new MiPlayWfdAudioPacketizer();
        var source = MiPlayAdtsStreamParserTests.CreateMpeg4AccessUnit(682, 0x5a);

        for (var index = 0; index < 100; index++)
        {
            var publish = broadcast.PublishAsync(
                StereoFrame(source, index),
                CancellationToken.None);
            var frames = await Task.WhenAll(
                broadcast.ReadIndexedAsync(first, CancellationToken.None).AsTask(),
                broadcast.ReadIndexedAsync(second, CancellationToken.None).AsTask());
            await publish;

            var output = timeline.GetTimestamp(
                index,
                5_000_000 + MiPlaySharedOutputTimeline.CalculateEncoderPresentationTimeMicroseconds(index));
            var firstPacket = Assert.Single(firstPacketizer.PacketizeAccessUnit(
                frames[0].AccessUnit.Bytes,
                output.ProgramClockReference27Mhz / 300,
                output.PresentationTimestamp90Khz,
                (ushort)(output.ProgramClockReference27Mhz % 300)));
            var secondPacket = Assert.Single(secondPacketizer.PacketizeAccessUnit(
                frames[1].AccessUnit.Bytes,
                output.ProgramClockReference27Mhz / 300,
                output.PresentationTimestamp90Khz,
                (ushort)(output.ProgramClockReference27Mhz % 300)));
            Assert.Equal(firstPacket.WireFrame, secondPacket.WireFrame);

            await Task.WhenAll(
                Complete(sends, firstSend, index, 1_000_000 + index * 21_333),
                Complete(sends, secondSend, index, 1_000_100 + index * 21_333));
        }

        var history = await broadcast.SnapshotHistoryAsync(
            AudioChannelRoute.Stereo,
            MiPlaySharedStartupAccessUnitBuffer.LateJoinRequiredAccessUnitCount,
            CancellationToken.None);
        Assert.Equal(53, history[0].AccessUnitIndex);
        Assert.Equal(99, history[^1].AccessUnitIndex);

        var latePacketizer = new MiPlayWfdAudioPacketizer();
        MiPlayWfdAudioPacket? lastReplayPacket = null;
        foreach (var replay in history)
        {
            var output = timeline.GetTimestamp(replay.AccessUnitIndex, 99_000_000);
            lastReplayPacket = Assert.Single(latePacketizer.PacketizeAccessUnit(
                replay.AccessUnit.Bytes,
                output.ProgramClockReference27Mhz / 300,
                output.PresentationTimestamp90Khz,
                (ushort)(output.ProgramClockReference27Mhz % 300)));
        }

        MiPlaySharedSendParticipant lateSend = default;
        var late = broadcast.Register(
            AudioChannelRoute.Stereo,
            joinIndex => lateSend = sends.Register(joinIndex));
        Assert.Equal(100, late.StartAccessUnitIndex);

        var livePublish = broadcast.PublishAsync(
            StereoFrame(source, 100),
            CancellationToken.None);
        var liveFrames = await Task.WhenAll(
            broadcast.ReadIndexedAsync(first, CancellationToken.None).AsTask(),
            broadcast.ReadIndexedAsync(second, CancellationToken.None).AsTask(),
            broadcast.ReadIndexedAsync(late, CancellationToken.None).AsTask());
        await livePublish;
        var liveOutput = timeline.GetTimestamp(
            100,
            5_000_000 + MiPlaySharedOutputTimeline.CalculateEncoderPresentationTimeMicroseconds(100));
        var firstLive = Assert.Single(firstPacketizer.PacketizeAccessUnit(
            liveFrames[0].AccessUnit.Bytes,
            liveOutput.ProgramClockReference27Mhz / 300,
            liveOutput.PresentationTimestamp90Khz,
            (ushort)(liveOutput.ProgramClockReference27Mhz % 300)));
        var secondLive = Assert.Single(secondPacketizer.PacketizeAccessUnit(
            liveFrames[1].AccessUnit.Bytes,
            liveOutput.ProgramClockReference27Mhz / 300,
            liveOutput.PresentationTimestamp90Khz,
            (ushort)(liveOutput.ProgramClockReference27Mhz % 300)));
        var lateLive = Assert.Single(latePacketizer.PacketizeAccessUnit(
            liveFrames[2].AccessUnit.Bytes,
            liveOutput.ProgramClockReference27Mhz / 300,
            liveOutput.PresentationTimestamp90Khz,
            (ushort)(liveOutput.ProgramClockReference27Mhz % 300)));

        Assert.NotNull(lastReplayPacket);
        Assert.Equal(unchecked((ushort)(lastReplayPacket.SequenceNumber + 1)), lateLive.SequenceNumber);
        Assert.Equal(firstLive.Timestamp90Khz, lateLive.Timestamp90Khz);
        Assert.Equal(secondLive.Timestamp90Khz, lateLive.Timestamp90Khz);
        Assert.NotEqual(firstLive.SequenceNumber, lateLive.SequenceNumber);
        Assert.All(liveFrames, frame => Assert.True(frame.ParticipatesInSendBarrier));

        var completed = await Task.WhenAll(
            Complete(sends, firstSend, 100, 3_133_300),
            Complete(sends, secondSend, 100, 3_133_400),
            Complete(sends, lateSend, 100, 3_133_500));
        Assert.All(completed, value => Assert.Equal(0.2, value.SendCompletionSkewMilliseconds, 3));
    }

    private static IReadOnlyDictionary<AudioChannelRoute, MiPlayEncodedAccessUnit> StereoFrame(
        byte[] bytes,
        long index) => new Dictionary<AudioChannelRoute, MiPlayEncodedAccessUnit>
        {
            [AudioChannelRoute.Stereo] = new(
                bytes,
                MiPlaySharedOutputTimeline.CalculateEncoderPresentationTimeMicroseconds(index)),
        };

    private static Task<MiPlaySharedSendCompletion> Complete(
        MiPlaySharedSendCompletionCoordinator coordinator,
        MiPlaySharedSendParticipant participant,
        long index,
        long completedAt) => coordinator.SynchronizeAsync(
            participant,
            index,
            completedAt,
            index == 0 ? 0 : MiPlayWfdStartupPacingPlan.NominalAccessUnitMilliseconds,
            mediaStartedAt: 1_000_000,
            stopwatchFrequency: 1_000_000,
            captureTimelineReanchored: false,
            CancellationToken.None);
}
