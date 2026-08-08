using System.Security.Cryptography;
using System.Text;
using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayLegacyAudioSourceSessionTests
{
    [Fact]
    public void ReproducesCapturedBasicSourceBootstrapAndStopsBeforeBusinessTraffic()
    {
        var session = MiPlayLegacyAudioSourceSession.CreateCapturedMiPadComparisonSession();
        var challengePayload = Encoding.ASCII.GetBytes("1234567890123456");
        var challenge = MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.LegacySafetyChallengeCommand,
            0x00be,
            challengePayload);

        var auth = session.ProcessInboundFrame(challenge);

        Assert.True(auth.Accepted);
        Assert.False(auth.SafeForNetworkUse);
        Assert.Equal(MiPlayLegacyAudioSourcePhase.AwaitingNativeVersionAcknowledgement, auth.Phase);
        Assert.Equal(2, auth.OutboundWrites.Count);
        Assert.Equal(2, auth.OutboundWrites[0].Frames.Count);
        Assert.Single(auth.OutboundWrites[1].Frames);
        AssertFrame(
            auth.OutboundWrites[0].Frames[0],
            MiPlayProtocolConstants.NativeSourceVersionCommand,
            0,
            Encoding.ASCII.GetBytes("1.0.1123012\0"));
        var expectedAcknowledgement = MiPlayLegacySafetyChallengeCodec.CreateAcknowledgement(0x00be, challengePayload);
        AssertFrame(
            auth.OutboundWrites[0].Frames[1],
            MiPlayProtocolConstants.LegacySafetyAcknowledgementCommand,
            0x00be,
            Encoding.ASCII.GetBytes(expectedAcknowledgement.Response));
        AssertFrame(
            auth.OutboundWrites[1].Frames[0],
            MiPlayProtocolConstants.GetDeviceInfoCommand,
            1,
            []);

        var nativeVersionAck = MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.NativeSourceVersionAcknowledgementCommand,
            0,
            Encoding.ASCII.GetBytes("2.1.5091615\0"));
        var identity = session.ProcessInboundFrame(nativeVersionAck);

        Assert.True(identity.Accepted);
        Assert.False(identity.SafeForNetworkUse);
        var sourceNameFrame = Assert.Single(Assert.Single(identity.OutboundWrites).Frames);
        AssertFrame(
            sourceNameFrame,
            MiPlayProtocolConstants.SetLocalDeviceInfoCommand,
            2,
            Encoding.UTF8.GetBytes("{\"sourceName\":\"MI PAD 4\\/Plus\"}"));
        Assert.Equal(
            "1DC0862AC9E8AE7E69D7EA1E71C62E508B74257AA73C127C868707E63E9CD113",
            Convert.ToHexString(SHA256.HashData(sourceNameFrame)));

        var sourceNameAck = session.ProcessInboundFrame(MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.SetLocalDeviceInfoAcknowledgementCommand,
            2,
            []));
        Assert.True(sourceNameAck.Accepted);
        Assert.Empty(sourceNameAck.OutboundWrites);

        var deviceInfoPayload = MiPlayLegacyDeviceInfoPayloadCodec.Encode(
            [new KeyValuePair<string, string>("model", "LX06")]);
        var initialComplete = session.ProcessInboundFrame(MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.GetDeviceInfoAcknowledgementCommand,
            1,
            deviceInfoPayload));

        Assert.True(initialComplete.Accepted);
        Assert.Equal(MiPlayLegacyAudioSourcePhase.AwaitingAccountAndMirrorAcknowledgements, initialComplete.Phase);
        Assert.Equal(2, initialComplete.OutboundWrites.Count);
        AssertFrame(
            initialComplete.OutboundWrites[0].Frames[0],
            MiPlayProtocolConstants.SetLocalDeviceInfoCommand,
            3,
            Encoding.UTF8.GetBytes("{\"isSameAccount\":0}"));
        AssertFrame(
            initialComplete.OutboundWrites[1].Frames[0],
            MiPlayProtocolConstants.GetMirrorModeCommand,
            4,
            []);
        Assert.Equal(
            new MiPlayLegacyAudioSourceProgress(
                MiPlayLegacyAudioSourcePhase.AwaitingAccountAndMirrorAcknowledgements,
                DeviceInfoAcknowledged: true,
                SourceNameAcknowledged: true,
                AccountAcknowledged: false,
                MirrorModeAcknowledged: false,
                StatusQueriesPrepared: false,
                VolumeAcknowledged: false,
                StateAcknowledged: false,
                MediaInfoObserved: false),
            session.Progress);

        var mirror = session.ProcessInboundFrame(MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.GetMirrorModeAcknowledgementCommand,
            4,
            [0, 0, 0, 0, 2]));
        Assert.True(mirror.Accepted);
        Assert.False(mirror.Completed);
        Assert.True(session.Progress.MirrorModeAcknowledged);
        Assert.False(session.Progress.AccountAcknowledged);
        Assert.False(session.Progress.StatusQueriesPrepared);

        var complete = session.ProcessInboundFrame(MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.SetLocalDeviceInfoAcknowledgementCommand,
            3,
            []));
        Assert.True(complete.Accepted);
        Assert.False(complete.Completed);
        Assert.False(complete.SafeForNetworkUse);
        Assert.Equal(3, complete.OutboundWrites.Count);
        AssertFrame(
            complete.OutboundWrites[0].Frames[0],
            MiPlayProtocolConstants.GetVolumeCommand,
            5,
            []);
        AssertFrame(
            complete.OutboundWrites[1].Frames[0],
            MiPlayProtocolConstants.GetMediaInfoCommand,
            6,
            []);
        AssertFrame(
            complete.OutboundWrites[2].Frames[0],
            MiPlayProtocolConstants.GetStateCommand,
            7,
            []);
        Assert.True(session.Progress.AccountAcknowledged);
        Assert.True(session.Progress.MirrorModeAcknowledged);
        Assert.True(session.Progress.StatusQueriesPrepared);

        var volume = session.ProcessInboundFrame(MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.GetVolumeAcknowledgementCommand,
            5,
            MiPlayLegacyStatusScalarCodec.Encode(25)));
        Assert.True(volume.Accepted);
        Assert.False(volume.Completed);
        Assert.Equal((uint)25, session.CurrentVolume);

        var mediaInfo = session.ProcessInboundFrame(MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.GetMediaInfoAcknowledgementCommand,
            6,
            [1, 2, 3]));
        Assert.True(mediaInfo.Accepted);
        Assert.False(mediaInfo.Completed);

        var statusComplete = session.ProcessInboundFrame(MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.GetStateAcknowledgementCommand,
            7,
            MiPlayLegacyStatusScalarCodec.Encode(0)));
        Assert.True(statusComplete.Accepted);
        Assert.True(statusComplete.Completed);
        Assert.False(statusComplete.SafeForNetworkUse);
        Assert.Equal(MiPlayLegacyAudioSourcePhase.BasicBootstrapComplete, statusComplete.Phase);
        Assert.Empty(statusComplete.OutboundWrites);
        Assert.Contains("Hard stop", statusComplete.Boundary, StringComparison.Ordinal);
        Assert.Equal(
            new MiPlayLegacyAudioSourceProgress(
                MiPlayLegacyAudioSourcePhase.BasicBootstrapComplete,
                DeviceInfoAcknowledged: true,
                SourceNameAcknowledged: true,
                AccountAcknowledged: true,
                MirrorModeAcknowledged: true,
                StatusQueriesPrepared: true,
                VolumeAcknowledged: true,
                StateAcknowledged: true,
                MediaInfoObserved: true),
            session.Progress);

        var afterBoundary = session.ProcessInboundFrame(MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.HeartbeatAcknowledgementCommand,
            5,
            []));
        Assert.False(afterBoundary.Accepted);
        Assert.True(afterBoundary.Completed);
        Assert.Empty(afterBoundary.OutboundWrites);
    }

    [Fact]
    public void ScalarStatusRepliesCompleteBootstrapWithoutMediaInfoNotification()
    {
        var session = new MiPlayLegacyAudioSourceSession(
            "MI PAD 4/Plus",
            MiPlayLegacyStatusQueryOrder.VolumeStateMediaInfo);
        session.ProcessInboundFrame(MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.LegacySafetyChallengeCommand,
            1,
            Encoding.ASCII.GetBytes("1234567890123456")));
        session.ProcessInboundFrame(MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.NativeSourceVersionAcknowledgementCommand,
            0,
            Encoding.ASCII.GetBytes("2.1.5091615\0")));
        session.ProcessInboundFrame(MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.GetDeviceInfoAcknowledgementCommand,
            1,
            MiPlayLegacyDeviceInfoPayloadCodec.Encode(
                [new KeyValuePair<string, string>("model", "LX06")])));
        session.ProcessInboundFrame(MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.SetLocalDeviceInfoAcknowledgementCommand,
            2,
            []));
        session.ProcessInboundFrame(MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.SetLocalDeviceInfoAcknowledgementCommand,
            3,
            []));
        session.ProcessInboundFrame(MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.GetMirrorModeAcknowledgementCommand,
            4,
            MiPlayLegacyStatusScalarCodec.Encode(2)));
        session.ProcessInboundFrame(MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.GetVolumeAcknowledgementCommand,
            5,
            MiPlayLegacyStatusScalarCodec.Encode(25)));

        var complete = session.ProcessInboundFrame(MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.GetStateAcknowledgementCommand,
            6,
            MiPlayLegacyStatusScalarCodec.Encode(0)));

        Assert.True(complete.Accepted);
        Assert.True(complete.Completed);
        Assert.Equal(MiPlayLegacyAudioSourcePhase.BasicBootstrapComplete, session.Phase);
        Assert.True(session.Progress.VolumeAcknowledged);
        Assert.True(session.Progress.StateAcknowledged);
        Assert.False(session.Progress.MediaInfoObserved);
        Assert.Contains("media-info notification remains optional", complete.Boundary, StringComparison.Ordinal);
    }

    [Fact]
    public void InterleavedNotifyIsReadOnlyAndDoesNotAdvanceState()
    {
        var session = MiPlayLegacyAudioSourceSession.CreateCapturedMiPadComparisonSession();
        session.ProcessInboundFrame(MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.LegacySafetyChallengeCommand,
            1,
            Encoding.ASCII.GetBytes("1234567890123456")));

        var notify = session.ProcessInboundFrame(MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.NotifyCommand,
            4,
            [1, 2, 3]));

        Assert.True(notify.Accepted);
        Assert.Equal(MiPlayLegacyAudioSourcePhase.AwaitingNativeVersionAcknowledgement, notify.Phase);
        Assert.Empty(notify.OutboundWrites);
        Assert.False(notify.SafeForNetworkUse);
    }

    [Fact]
    public void ModernSafetyFrameStopsLegacyBranchWithoutPreparingOutput()
    {
        var session = MiPlayLegacyAudioSourceSession.CreateCapturedMiPadComparisonSession();

        var result = session.ProcessInboundFrame(MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.SafetyInfoCommand,
            0,
            []));

        Assert.False(result.Accepted);
        Assert.Equal(MiPlayLegacyAudioSourcePhase.Stopped, result.Phase);
        Assert.Empty(result.OutboundWrites);
        Assert.False(result.SafeForNetworkUse);
    }

    [Fact]
    public void SeventeenDigitLegacyChallengeIsAcceptedAndHashedInFull()
    {
        var session = MiPlayLegacyAudioSourceSession.CreateCapturedMiPadComparisonSession();
        var challenge = Encoding.ASCII.GetBytes("12345678901234567");

        var result = session.ProcessInboundFrame(MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.LegacySafetyChallengeCommand,
            0x0370,
            challenge));

        Assert.True(result.Accepted);
        var acknowledgementBytes = result.OutboundWrites[0].Frames[1];
        Assert.True(MiPlayCommandFrameCodec.TryDecode(acknowledgementBytes, out var acknowledgement, out _));
        Assert.NotNull(acknowledgement);
        Assert.Equal((ushort)0x0370, acknowledgement.Sequence);
        Assert.Equal(
            MiPlayLegacySafetyChallengeCodec.CreateAcknowledgement(0x0370, challenge).Response,
            Encoding.ASCII.GetString(acknowledgement.Payload));
    }

    [Fact]
    public void TwelveDigitLiveLegacyChallengeIsAcceptedAndHashedInFull()
    {
        var session = MiPlayLegacyAudioSourceSession.CreateCapturedMiPadComparisonSession();
        var challenge = Encoding.ASCII.GetBytes("308415521644");

        var result = session.ProcessInboundFrame(MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.LegacySafetyChallengeCommand,
            0x0051,
            challenge));

        Assert.True(result.Accepted);
        var acknowledgementBytes = result.OutboundWrites[0].Frames[1];
        Assert.True(MiPlayCommandFrameCodec.TryDecode(acknowledgementBytes, out var acknowledgement, out _));
        Assert.NotNull(acknowledgement);
        Assert.Equal((ushort)0x0051, acknowledgement.Sequence);
        Assert.Equal(
            MiPlayLegacySafetyChallengeCodec.CreateAcknowledgement(0x0051, challenge).Response,
            Encoding.ASCII.GetString(acknowledgement.Payload));
    }

    [Fact]
    public void ElevenDigitLegacyChallengeStopsBeforePreparingOutput()
    {
        var session = MiPlayLegacyAudioSourceSession.CreateCapturedMiPadComparisonSession();

        var result = session.ProcessInboundFrame(MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.LegacySafetyChallengeCommand,
            0x0052,
            Encoding.ASCII.GetBytes("12345678901")));

        Assert.False(result.Accepted);
        Assert.Equal(MiPlayLegacyAudioSourcePhase.Stopped, result.Phase);
        Assert.Empty(result.OutboundWrites);
        Assert.Contains("payload length 11", result.Boundary, StringComparison.Ordinal);
    }

    [Fact]
    public void LiveObservedModeOneMirrorAcknowledgementIsAccepted()
    {
        var session = CreateAwaitingAccountAndMirrorSession();

        var result = session.ProcessInboundFrame(MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.GetMirrorModeAcknowledgementCommand,
            MiPlayLegacyAudioSourceSession.GetMirrorModeSequence,
            MiPlayLegacyStatusScalarCodec.Encode(1)));

        Assert.True(result.Accepted);
        Assert.True(session.Progress.MirrorModeAcknowledged);
        Assert.Equal(MiPlayLegacyAudioSourcePhase.AwaitingAccountAndMirrorAcknowledgements, result.Phase);
    }

    [Fact]
    public void UnobservedModeThreeMirrorAcknowledgementStopsBeforePreparingOutput()
    {
        var session = CreateAwaitingAccountAndMirrorSession();

        var result = session.ProcessInboundFrame(MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.GetMirrorModeAcknowledgementCommand,
            MiPlayLegacyAudioSourceSession.GetMirrorModeSequence,
            MiPlayLegacyStatusScalarCodec.Encode(3)));

        Assert.False(result.Accepted);
        Assert.Equal(MiPlayLegacyAudioSourcePhase.Stopped, result.Phase);
        Assert.Empty(result.OutboundWrites);
        Assert.Contains("decoded mode 3", result.Boundary, StringComparison.Ordinal);
    }

    [Fact]
    public void AlternateCapturedStatusOrderUsesStateSequenceSixAndMediaInfoSequenceSeven()
    {
        var session = new MiPlayLegacyAudioSourceSession(
            "MI PAD 4/Plus",
            MiPlayLegacyStatusQueryOrder.VolumeStateMediaInfo);
        session.ProcessInboundFrame(MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.LegacySafetyChallengeCommand,
            1,
            Encoding.ASCII.GetBytes("1234567890123456")));
        session.ProcessInboundFrame(MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.NativeSourceVersionAcknowledgementCommand,
            0,
            Encoding.ASCII.GetBytes("2.1.5091615\0")));
        session.ProcessInboundFrame(MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.GetDeviceInfoAcknowledgementCommand,
            1,
            MiPlayLegacyDeviceInfoPayloadCodec.Encode(
                [new KeyValuePair<string, string>("model", "LX06")])));
        session.ProcessInboundFrame(MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.SetLocalDeviceInfoAcknowledgementCommand,
            2,
            []));

        var account = session.ProcessInboundFrame(MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.SetLocalDeviceInfoAcknowledgementCommand,
            3,
            []));

        Assert.Equal(3, account.OutboundWrites.Count);
        AssertFrame(account.OutboundWrites[0].Frames[0], MiPlayProtocolConstants.GetVolumeCommand, 5, []);
        AssertFrame(account.OutboundWrites[1].Frames[0], MiPlayProtocolConstants.GetStateCommand, 6, []);
        AssertFrame(account.OutboundWrites[2].Frames[0], MiPlayProtocolConstants.GetMediaInfoCommand, 7, []);
    }

    [Fact]
    public void LegacySourceNameCodecReproducesEscapedSlashTranscript()
    {
        var payload = MiPlayLocalDeviceInfoPayloadCodec.EncodeLegacySourceNameOnly("MI PAD 4/Plus");

        Assert.Equal("{\"sourceName\":\"MI PAD 4\\/Plus\"}", Encoding.UTF8.GetString(payload));
        Assert.Equal(31, payload.Length);
    }

    private static void AssertFrame(
        byte[] bytes,
        ushort command,
        ushort sequence,
        byte[] payload)
    {
        Assert.True(MiPlayCommandFrameCodec.TryDecode(bytes, out var frame, out var bytesConsumed));
        Assert.NotNull(frame);
        Assert.Equal(bytes.Length, bytesConsumed);
        Assert.Equal(command, frame.Command);
        Assert.Equal(sequence, frame.Sequence);
        Assert.Equal(payload, frame.Payload);
    }

    private static MiPlayLegacyAudioSourceSession CreateAwaitingAccountAndMirrorSession()
    {
        var session = MiPlayLegacyAudioSourceSession.CreateCapturedMiPadComparisonSession();
        session.ProcessInboundFrame(MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.LegacySafetyChallengeCommand,
            0x0051,
            Encoding.ASCII.GetBytes("308415521644")));
        session.ProcessInboundFrame(MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.NativeSourceVersionAcknowledgementCommand,
            0,
            Encoding.ASCII.GetBytes("2.1.5091615\0")));
        session.ProcessInboundFrame(MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.GetDeviceInfoAcknowledgementCommand,
            1,
            MiPlayLegacyDeviceInfoPayloadCodec.Encode(
                [new KeyValuePair<string, string>("model", "LX06")])));
        session.ProcessInboundFrame(MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.SetLocalDeviceInfoAcknowledgementCommand,
            2,
            []));

        Assert.Equal(MiPlayLegacyAudioSourcePhase.AwaitingAccountAndMirrorAcknowledgements, session.Phase);
        return session;
    }
}
