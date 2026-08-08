using System.Text;
using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayFreshLegacyReceiverBootstrapSessionTests
{
    [Fact]
    public void CapturedOrderPreparesExactlyOneSameSequenceGetDeviceInfoAcknowledgement()
    {
        var session = new MiPlayFreshLegacyReceiverBootstrapSession();

        var version = session.ProcessInboundFrame(MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.NativeSourceVersionCommand,
            0,
            Encoding.ASCII.GetBytes("1.0.1123012\0")));
        Assert.True(version.Accepted);
        Assert.Null(version.ResponseCandidate);

        var legacy = session.ProcessInboundFrame(CreateValidLegacyAcknowledgement());
        Assert.True(legacy.Accepted);
        Assert.True(legacy.LegacyAcknowledgementVerified);
        Assert.Null(legacy.ResponseCandidate);

        var getDeviceInfo = session.ProcessInboundFrame(MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.GetDeviceInfoCommand,
            0x0042,
            []));
        Assert.True(getDeviceInfo.Accepted);
        Assert.False(getDeviceInfo.SafeForNetworkUse);
        Assert.NotNull(getDeviceInfo.ResponseCandidate);

        Assert.True(MiPlayCommandFrameCodec.TryDecode(
            getDeviceInfo.ResponseCandidate,
            out var response,
            out var bytesConsumed));
        Assert.NotNull(response);
        Assert.Equal(getDeviceInfo.ResponseCandidate.Length, bytesConsumed);
        Assert.Equal(MiPlayProtocolConstants.GetDeviceInfoAcknowledgementCommand, response.Command);
        Assert.Equal((ushort)0x0042, response.Sequence);

        var duplicate = session.ProcessInboundFrame(MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.GetDeviceInfoCommand,
            0x0043,
            []));
        Assert.False(duplicate.Accepted);
        Assert.Null(duplicate.ResponseCandidate);
    }

    [Fact]
    public void GetDeviceInfoCanRaceBeforeLegacyAcknowledgementButIsHeld()
    {
        var session = new MiPlayFreshLegacyReceiverBootstrapSession();
        var earlyGetDeviceInfo = session.ProcessInboundFrame(MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.GetDeviceInfoCommand,
            7,
            []));

        Assert.True(earlyGetDeviceInfo.Accepted);
        Assert.Equal("getDeviceInfo-pending-auth", earlyGetDeviceInfo.Phase);
        Assert.False(earlyGetDeviceInfo.LegacyAcknowledgementVerified);
        Assert.True(earlyGetDeviceInfo.EmptyGetDeviceInfoObserved);
        Assert.Null(earlyGetDeviceInfo.ResponseCandidate);

        var legacy = session.ProcessInboundFrame(CreateValidLegacyAcknowledgement());
        Assert.True(legacy.Accepted);
        Assert.NotNull(legacy.ResponseCandidate);
        Assert.True(MiPlayCommandFrameCodec.TryDecode(legacy.ResponseCandidate, out var response, out _));
        Assert.Equal((ushort)7, response!.Sequence);
    }

    [Fact]
    public void SourceNameAndHeartbeatsNeverProduceReplies()
    {
        var session = new MiPlayFreshLegacyReceiverBootstrapSession();

        var sourceName = session.ProcessInboundFrame(MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.SetLocalDeviceInfoCommand,
            2,
            Encoding.UTF8.GetBytes(MiPlayFreshLegacySenderCaptureEvidence.SetLocalDeviceInfoJson)));
        var heartbeat = session.ProcessInboundFrame(MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.HeartbeatCommand,
            3,
            []));

        Assert.True(sourceName.Accepted);
        Assert.True(heartbeat.Accepted);
        Assert.Null(sourceName.ResponseCandidate);
        Assert.Null(heartbeat.ResponseCandidate);
        Assert.False(sourceName.SafeForNetworkUse);
        Assert.False(heartbeat.SafeForNetworkUse);
    }

    [Fact]
    public void RejectsBadAuthNonEmptyGetDeviceInfoAndModernSafety()
    {
        var badAuth = new MiPlayFreshLegacyReceiverBootstrapSession().ProcessInboundFrame(
            MiPlayCommandFrameCodec.Encode(
                MiPlayProtocolConstants.LegacySafetyAcknowledgementCommand,
                0,
                Encoding.ASCII.GetBytes("bad")));
        Assert.False(badAuth.Accepted);

        var nonEmpty = new MiPlayFreshLegacyReceiverBootstrapSession().ProcessInboundFrame(
            MiPlayCommandFrameCodec.Encode(
                MiPlayProtocolConstants.GetDeviceInfoCommand,
                1,
                [0]));
        Assert.False(nonEmpty.Accepted);

        var safety = new MiPlayFreshLegacyReceiverBootstrapSession().ProcessInboundFrame(
            MiPlayCommandFrameCodec.Encode(
                MiPlayProtocolConstants.SafetyInfoCommand,
                1,
                []));
        Assert.False(safety.Accepted);
    }

    [Fact]
    public void ProbePolicyRequiresFreshAuthorizationAndExactOutboundAccounting()
    {
        var session = new MiPlayFreshLegacyReceiverBootstrapSession();
        session.ProcessInboundFrame(CreateValidLegacyAcknowledgement());
        var result = session.ProcessInboundFrame(MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.GetDeviceInfoCommand,
            1,
            []));

        var noAuthorization = MiPlayFreshLegacyReceiverProbePolicy.Evaluate(
            false,
            result,
            outboundLegacyChallengeCount: 1,
            outboundGetDeviceInfoAcknowledgementCount: 0,
            noOtherOutboundFrames: true);
        Assert.False(noAuthorization.CanSendNow);
        Assert.False(noAuthorization.SafeForNetworkUse);

        var wrongAccounting = MiPlayFreshLegacyReceiverProbePolicy.Evaluate(
            true,
            result,
            outboundLegacyChallengeCount: 1,
            outboundGetDeviceInfoAcknowledgementCount: 1,
            noOtherOutboundFrames: true);
        Assert.False(wrongAccounting.CanSendNow);

        var authorized = MiPlayFreshLegacyReceiverProbePolicy.Evaluate(
            true,
            result,
            outboundLegacyChallengeCount: 1,
            outboundGetDeviceInfoAcknowledgementCount: 0,
            noOtherOutboundFrames: true);
        Assert.True(authorized.CanSendNow);
        Assert.True(authorized.SafeForNetworkUse);
        Assert.Contains("one same-sequence 0x001f only", authorized.Reason, StringComparison.Ordinal);
    }

    private static byte[] CreateValidLegacyAcknowledgement()
    {
        var acknowledgement = MiPlayLegacySafetyChallengeCodec.CreateAcknowledgement(
            MiPlayPassiveSenderCaptureProfile.ChallengeSequence,
            Encoding.ASCII.GetBytes(MiPlayPassiveSenderCaptureProfile.ChallengeText));
        return MiPlayLegacySafetyChallengeCodec.EncodeAcknowledgement(acknowledgement);
    }
}
