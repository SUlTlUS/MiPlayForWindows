using System.Net;
using System.Text;
using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayLegacyPlaybackControlSessionTests
{
    [Fact]
    public void ReproducesCapturedSequencesEightThroughFourteenWithoutAddMirror()
    {
        var session = new MiPlayLegacyPlaybackControlSession(
            CompleteBootstrap(),
            "MI PAD 4/Plus",
            IPAddress.Parse("192.168.10.58"),
            7_274);
        var allFrames = new List<byte[]>();

        var start = session.Start();
        allFrames.AddRange(start.OutboundWrites.SelectMany(write => write.Frames));
        Assert.Equal(2, start.OutboundWrites.Count);
        AssertFrame(allFrames[0], MiPlayProtocolConstants.SetLocalDeviceInfoCommand, 8,
            Encoding.UTF8.GetBytes("{\"sourceName\":\"MI PAD 4\\/Plus\"}"));
        AssertFrame(allFrames[1], MiPlayProtocolConstants.GetDeviceInfoCommand, 9, []);

        var sourceNameAck = session.ProcessInbound(Frame(
            MiPlayProtocolConstants.SetLocalDeviceInfoAcknowledgementCommand, 8, []));
        Assert.Empty(sourceNameAck.OutboundWrites);
        var deviceInfoAck = session.ProcessInbound(Frame(
            MiPlayProtocolConstants.GetDeviceInfoAcknowledgementCommand,
            9,
            MiPlayLegacyDeviceInfoPayloadCodec.Encode(
                [new KeyValuePair<string, string>("model", "LX06")])));
        allFrames.AddRange(deviceInfoAck.OutboundWrites.SelectMany(write => write.Frames));
        AssertFrame(allFrames[2], MiPlayProtocolConstants.SetLocalDeviceInfoCommand, 10,
            Encoding.UTF8.GetBytes("{\"isSameAccount\":0}"));

        var accountAck = session.ProcessInbound(Frame(
            MiPlayProtocolConstants.SetLocalDeviceInfoAcknowledgementCommand, 10, []));
        allFrames.AddRange(accountAck.OutboundWrites.SelectMany(write => write.Frames));
        AssertFrame(allFrames[3], MiPlayProtocolConstants.GetMirrorModeCommand, 11, []);

        var mirrorAck = session.ProcessInbound(Frame(
            MiPlayProtocolConstants.GetMirrorModeAcknowledgementCommand,
            11,
            MiPlayLegacyStatusScalarCodec.Encode(1)));
        allFrames.AddRange(mirrorAck.OutboundWrites.SelectMany(write => write.Frames));
        AssertFrame(allFrames[4], MiPlayProtocolConstants.HeartbeatCommand, 12, []);

        var heartbeatAck = session.ProcessInbound(Frame(
            MiPlayProtocolConstants.HeartbeatAcknowledgementCommand, 12, []));
        allFrames.AddRange(heartbeatAck.OutboundWrites.SelectMany(write => write.Frames));
        Assert.Equal(MiPlayLegacyPlaybackControlPhase.AwaitingOpenPrerequisites, heartbeatAck.Phase);
        AssertFrame(
            allFrames[5],
            MiPlayProtocolConstants.SetPlaySourceCommand,
            13,
            Encoding.UTF8.GetBytes(MiPlayRealLegacyPlaybackSessionEvidence.SetPlaySourceJson));

        var blocked = session.PrepareOpen(new(
            TcpListenerBound: true,
            UdpTimerResponderBound: true,
            ReverseConnectionCapacity: 2,
            AacMpegTsPipelineReady: true));
        Assert.False(blocked.Accepted);
        Assert.Empty(blocked.OutboundWrites);
        Assert.Equal(MiPlayLegacyPlaybackControlPhase.AwaitingOpenPrerequisites, blocked.Phase);

        var open = session.PrepareOpen(new(
            TcpListenerBound: true,
            UdpTimerResponderBound: true,
            ReverseConnectionCapacity: 3,
            AacMpegTsPipelineReady: true));
        allFrames.AddRange(open.OutboundWrites.SelectMany(write => write.Frames));
        Assert.True(open.Accepted);
        Assert.True(open.OpenPrepared);
        Assert.False(open.SafeForNetworkUse);
        AssertFrame(
            allFrames[6],
            MiPlayProtocolConstants.OpenDeviceCommand,
            14,
            Encoding.UTF8.GetBytes("wfd://192.168.10.58:7274?mirrorMode=1\0"));

        Assert.DoesNotContain(allFrames, bytes => Decode(bytes).Command == MiPlayProtocolConstants.AddMirrorCommand);
        Assert.Equal([8, 9, 10, 11, 12, 13, 14], allFrames.Select(bytes => Decode(bytes).Sequence));
    }

    [Fact]
    public void PauseAndResumeUseThePhoneFirmwareCommandMapNotMediaPlayerCommands()
    {
        Assert.Equal((ushort)0x0004, MiPlayProtocolConstants.PauseCommand);
        Assert.Equal((ushort)0x0005, MiPlayProtocolConstants.PauseAcknowledgementCommand);
        Assert.Equal((ushort)0x0006, MiPlayProtocolConstants.ResumeCommand);
        Assert.Equal((ushort)0x0007, MiPlayProtocolConstants.ResumeAcknowledgementCommand);
        Assert.NotEqual((ushort)0x0044, MiPlayProtocolConstants.PauseCommand);
        Assert.NotEqual((ushort)0x0046, MiPlayProtocolConstants.ResumeCommand);
    }

    [Fact]
    public void ContinuationRejectsAnIncompleteBootstrap()
    {
        var bootstrap = MiPlayLegacyAudioSourceSession.CreateCapturedMiPadComparisonSession();

        var error = Assert.Throws<InvalidOperationException>(() =>
            new MiPlayLegacyPlaybackControlSession(
                bootstrap,
                "MI PAD 4/Plus",
                IPAddress.Loopback,
                7_274));

        Assert.Contains("completed", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static MiPlayLegacyAudioSourceSession CompleteBootstrap()
    {
        var session = MiPlayLegacyAudioSourceSession.CreateCapturedMiPadComparisonSession();
        session.ProcessInboundFrame(Frame(
            MiPlayProtocolConstants.LegacySafetyChallengeCommand,
            0x0100,
            Encoding.ASCII.GetBytes("1234567890123456")));
        session.ProcessInboundFrame(Frame(
            MiPlayProtocolConstants.NativeSourceVersionAcknowledgementCommand,
            0,
            Encoding.ASCII.GetBytes("2.1.4052010\0")));
        session.ProcessInboundFrame(Frame(
            MiPlayProtocolConstants.SetLocalDeviceInfoAcknowledgementCommand, 2, []));
        session.ProcessInboundFrame(Frame(
            MiPlayProtocolConstants.GetDeviceInfoAcknowledgementCommand,
            1,
            MiPlayLegacyDeviceInfoPayloadCodec.Encode(
                [new KeyValuePair<string, string>("model", "LX06")])));
        session.ProcessInboundFrame(Frame(
            MiPlayProtocolConstants.SetLocalDeviceInfoAcknowledgementCommand, 3, []));
        session.ProcessInboundFrame(Frame(
            MiPlayProtocolConstants.GetMirrorModeAcknowledgementCommand,
            4,
            MiPlayLegacyStatusScalarCodec.Encode(2)));
        session.ProcessInboundFrame(Frame(
            MiPlayProtocolConstants.GetVolumeAcknowledgementCommand,
            5,
            MiPlayLegacyStatusScalarCodec.Encode(25)));
        session.ProcessInboundFrame(Frame(
            MiPlayProtocolConstants.GetStateAcknowledgementCommand,
            7,
            MiPlayLegacyStatusScalarCodec.Encode(0)));
        session.ProcessInboundFrame(Frame(
            MiPlayProtocolConstants.GetMediaInfoAcknowledgementCommand,
            6,
            [1]));

        Assert.Equal(MiPlayLegacyAudioSourcePhase.BasicBootstrapComplete, session.Phase);
        return session;
    }

    private static byte[] Frame(ushort command, ushort sequence, byte[] payload) =>
        MiPlayCommandFrameCodec.Encode(command, sequence, payload);

    private static MiPlayCommandFrame Decode(byte[] bytes)
    {
        Assert.True(MiPlayCommandFrameCodec.TryDecode(bytes, out var frame, out var consumed));
        Assert.NotNull(frame);
        Assert.Equal(bytes.Length, consumed);
        return frame;
    }

    private static void AssertFrame(byte[] bytes, ushort command, ushort sequence, byte[] payload)
    {
        var frame = Decode(bytes);
        Assert.Equal(command, frame.Command);
        Assert.Equal(sequence, frame.Sequence);
        Assert.Equal(payload, frame.Payload);
    }
}
