using System.Text;
using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayLegacyPostOpenPlaybackSessionTests
{
    [Fact]
    public void ReproducesCleanAutomaticSelectionWithoutPlaybackControls()
    {
        var session = CreateSession();

        var start = session.Start();

        Assert.True(start.Accepted);
        Assert.Equal(MiPlayLegacyPostOpenPlaybackPhase.AwaitingReceiverPlaybackReadiness, start.Phase);
        var mediaInfo = Decode(Assert.Single(start.OutboundWrites));
        Assert.Equal((MiPlayProtocolConstants.SetMediaInfoCommand, (ushort)15), (mediaInfo.Command, mediaInfo.Sequence));
        Assert.True(MiPlaySetMediaInfoPayloadCodec.TryDecode(mediaInfo.Payload, out var decodedMediaInfo));
        Assert.Equal("System Audio", decodedMediaInfo!.Title);

        var firstAudio = session.ProcessInbound(Notify("first-audiopcm", 1));
        Assert.True(firstAudio.Accepted);
        Assert.Equal(MiPlayLegacyPostOpenPlaybackPhase.AwaitingReceiverPlaybackReadiness, firstAudio.Phase);
        Assert.Equal("first-audiopcm", firstAudio.Notify!.Label);
        Assert.True(session.FirstAudioPcmObserved);

        var playing = session.ProcessInbound(Notify("state", 2));
        Assert.True(playing.Accepted);
        Assert.Equal(MiPlayLegacyPostOpenPlaybackPhase.Playing, playing.Phase);
        Assert.Equal(2, session.ReceiverState);
    }

    [Fact]
    public void AcceptsOptionalSetMediaInfoAckWithoutUsingItAsAResumeGate()
    {
        var session = CreateSession();
        session.Start();

        var acknowledgement = session.ProcessInbound(MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.SetMediaInfoAcknowledgementCommand,
            15,
            []));

        Assert.True(acknowledgement.Accepted);
        Assert.Equal(MiPlayLegacyPostOpenPlaybackPhase.AwaitingReceiverPlaybackReadiness, acknowledgement.Phase);
    }

    [Fact]
    public void CompoundFirstAudioPcmAndStateTwoReachPlaying()
    {
        var session = CreateSession();
        session.Start();
        var compoundPayload = Convert.FromHexString(
            "0E66697273742D617564696F70636D0301" +
            "1A66697273742D617564696F70636D2D6275666665722D74696D650600000000");

        var firstAudio = session.ProcessInbound(MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.NotifyCommand,
            1,
            compoundPayload));
        var playing = session.ProcessInbound(Notify("state", 2));

        Assert.True(firstAudio.Accepted);
        Assert.NotNull(firstAudio.Notify);
        Assert.True(session.FirstAudioPcmObserved);
        Assert.Equal(0, session.UnsupportedNotificationCount);
        Assert.True(playing.Accepted);
        Assert.Equal(MiPlayLegacyPostOpenPlaybackPhase.Playing, playing.Phase);
    }

    [Fact]
    public void IgnoresUnsupportedReadOnlyNotificationWithoutChangingStateOrReplying()
    {
        var session = CreateSession();
        session.Start();

        var transition = session.ProcessInbound(MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.NotifyCommand,
            0,
            [0xff, 0x00, 0x7f]));

        Assert.True(transition.Accepted);
        Assert.Equal(MiPlayLegacyPostOpenPlaybackPhase.AwaitingReceiverPlaybackReadiness, transition.Phase);
        Assert.Empty(transition.OutboundWrites);
        Assert.Null(transition.Notify);
        Assert.Null(session.ReceiverState);
        Assert.False(session.FirstAudioPcmObserved);
        Assert.Equal(1, session.UnsupportedNotificationCount);
    }

    [Fact]
    public void StopsOnAnUnexpectedPostOpenBusinessCommand()
    {
        var session = CreateSession();
        session.Start();

        var transition = session.ProcessInbound(MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.AddMirrorCommand,
            17,
            []));

        Assert.False(transition.Accepted);
        Assert.Equal(MiPlayLegacyPostOpenPlaybackPhase.Stopped, transition.Phase);
    }

    private static MiPlayLegacyPostOpenPlaybackSession CreateSession() =>
        new(MiPlaySetMediaInfoPayloadCodec.CreateWindowsSystemAudio(20_011, "DLNACast Windows"));

    private static byte[] Notify(string label, byte value)
    {
        var labelBytes = Encoding.ASCII.GetBytes(label);
        var payload = new byte[1 + labelBytes.Length + 2];
        payload[0] = checked((byte)labelBytes.Length);
        labelBytes.CopyTo(payload, 1);
        payload[^2] = MiPlayNotifyPayloadCodec.ByteValueType;
        payload[^1] = value;
        return MiPlayCommandFrameCodec.Encode(MiPlayProtocolConstants.NotifyCommand, 0, payload);
    }

    private static MiPlayCommandFrame Decode(MiPlayLegacyAudioSourceWrite write)
    {
        var bytes = Assert.Single(write.Frames);
        Assert.True(MiPlayCommandFrameCodec.TryDecode(bytes, out var frame, out var consumed));
        Assert.NotNull(frame);
        Assert.Equal(bytes.Length, consumed);
        return frame;
    }
}
