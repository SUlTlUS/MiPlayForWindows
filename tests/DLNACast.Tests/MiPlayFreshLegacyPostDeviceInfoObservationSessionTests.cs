using System.Text;
using System.Security.Cryptography;
using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayFreshLegacyPostDeviceInfoObservationSessionTests
{
    [Fact]
    public void CompleteInMemoryValidationPreservesExactTwoFrameOutboundBoundary()
    {
        var acknowledgement = MiPlayLegacySafetyChallengeCodec.CreateAcknowledgement(
            MiPlayPassiveSenderCaptureProfile.ChallengeSequence,
            Encoding.ASCII.GetBytes(MiPlayPassiveSenderCaptureProfile.ChallengeText));
        var bootstrap = new MiPlayFreshLegacyReceiverBootstrapSession();
        Assert.True(bootstrap.ProcessInboundFrame(
            MiPlayLegacySafetyChallengeCodec.EncodeAcknowledgement(acknowledgement)).Accepted);

        var deviceInfoRequest = bootstrap.ProcessInboundFrame(MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.GetDeviceInfoCommand,
            MiPlayFreshLegacyDeviceInfoLiveValidationEvidence.GetDeviceInfoSequence,
            []));
        var outboundLegacyChallengeCount = 1;
        var outboundGetDeviceInfoAcknowledgementCount = 0;
        var outboundPostDeviceInfoFrameCount = 0;
        var sendPolicy = MiPlayFreshLegacyReceiverProbePolicy.Evaluate(
            explicitUserAuthorization: true,
            deviceInfoRequest,
            outboundLegacyChallengeCount,
            outboundGetDeviceInfoAcknowledgementCount,
            noOtherOutboundFrames: true);

        Assert.True(sendPolicy.CanSendNow);
        Assert.True(sendPolicy.SafeForNetworkUse);
        Assert.NotNull(deviceInfoRequest.ResponseCandidate);
        outboundGetDeviceInfoAcknowledgementCount++;

        var observation = new MiPlayFreshLegacyPostDeviceInfoObservationSession();
        var setLocalDeviceInfo = observation.ProcessInboundFrame(
            MiPlayFreshLegacyDeviceInfoLiveValidationEvidence.ReconstructAdvancedSetLocalDeviceInfoFrame());
        var getMirrorMode = observation.ProcessInboundFrame(
            MiPlayFreshLegacyPostDeviceInfoProgressionEvidence.CreateOfflinePlan().PredictedGetMirrorModeFrame);

        Assert.True(setLocalDeviceInfo.Accepted);
        Assert.True(getMirrorMode.Completed);
        Assert.False(setLocalDeviceInfo.AllowsFollowUpSend);
        Assert.False(getMirrorMode.AllowsFollowUpSend);
        Assert.Equal(1, outboundLegacyChallengeCount);
        Assert.Equal(1, outboundGetDeviceInfoAcknowledgementCount);
        Assert.Equal(0, outboundPostDeviceInfoFrameCount);
    }

    [Fact]
    public void AcceptsOnlyExact0058ThenExact0034AndNeverAllowsReply()
    {
        var session = new MiPlayFreshLegacyPostDeviceInfoObservationSession();
        var setLocalDeviceInfo = session.ProcessInboundFrame(
            MiPlayFreshLegacyDeviceInfoLiveValidationEvidence.ReconstructAdvancedSetLocalDeviceInfoFrame());

        Assert.True(setLocalDeviceInfo.Accepted);
        Assert.False(setLocalDeviceInfo.Completed);
        Assert.False(setLocalDeviceInfo.ExactInitialSetLocalDeviceInfoRaceObserved);
        Assert.True(setLocalDeviceInfo.ExactSetLocalDeviceInfoObserved);
        Assert.False(setLocalDeviceInfo.ExactGetMirrorModeObserved);
        Assert.False(setLocalDeviceInfo.AllowsFollowUpSend);
        Assert.Contains("without sending 0x0059", setLocalDeviceInfo.Boundary, StringComparison.Ordinal);

        var getMirrorMode = session.ProcessInboundFrame(
            MiPlayFreshLegacyPostDeviceInfoProgressionEvidence.CreateOfflinePlan().PredictedGetMirrorModeFrame);

        Assert.True(getMirrorMode.Accepted);
        Assert.True(getMirrorMode.Completed);
        Assert.True(getMirrorMode.ExactSetLocalDeviceInfoObserved);
        Assert.True(getMirrorMode.ExactGetMirrorModeObserved);
        Assert.False(getMirrorMode.ExactInitialSetLocalDeviceInfoRaceObserved);
        Assert.False(getMirrorMode.AllowsFollowUpSend);
        Assert.Contains("Stop without 0x0035", getMirrorMode.Boundary, StringComparison.Ordinal);
    }

    [Fact]
    public void AcceptsOneExactInitial0058RaceBeforeAdvanced0058And0034()
    {
        var session = new MiPlayFreshLegacyPostDeviceInfoObservationSession();

        var initial = session.ProcessInboundFrame(
            MiPlayFreshLegacyDeviceInfoLiveValidationEvidence.ReconstructInitialSetLocalDeviceInfoFrame());

        Assert.True(initial.Accepted);
        Assert.False(initial.Completed);
        Assert.True(initial.ExactInitialSetLocalDeviceInfoRaceObserved);
        Assert.False(initial.ExactSetLocalDeviceInfoObserved);
        Assert.False(initial.ExactGetMirrorModeObserved);
        Assert.False(initial.AllowsFollowUpSend);
        Assert.Contains("proven race", initial.Boundary, StringComparison.Ordinal);

        var advanced = session.ProcessInboundFrame(
            MiPlayFreshLegacyDeviceInfoLiveValidationEvidence.ReconstructAdvancedSetLocalDeviceInfoFrame());

        Assert.True(advanced.Accepted);
        Assert.True(advanced.ExactInitialSetLocalDeviceInfoRaceObserved);
        Assert.True(advanced.ExactSetLocalDeviceInfoObserved);
        Assert.False(advanced.ExactGetMirrorModeObserved);
        Assert.False(advanced.AllowsFollowUpSend);

        var getMirrorMode = session.ProcessInboundFrame(
            MiPlayFreshLegacyPostDeviceInfoProgressionEvidence.CreateOfflinePlan().PredictedGetMirrorModeFrame);

        Assert.True(getMirrorMode.Accepted);
        Assert.True(getMirrorMode.Completed);
        Assert.True(getMirrorMode.ExactInitialSetLocalDeviceInfoRaceObserved);
        Assert.True(getMirrorMode.ExactSetLocalDeviceInfoObserved);
        Assert.True(getMirrorMode.ExactGetMirrorModeObserved);
        Assert.False(getMirrorMode.AllowsFollowUpSend);
    }

    [Fact]
    public void ReconstructedInitial0058MatchesCapturedFrameExactly()
    {
        var frame = MiPlayFreshLegacyDeviceInfoLiveValidationEvidence.ReconstructInitialSetLocalDeviceInfoFrame();

        Assert.Equal(
            MiPlayProtocolConstants.CommandHeaderLength +
            MiPlayFreshLegacyDeviceInfoLiveValidationEvidence.InitialSetLocalDeviceInfoPayloadLength,
            frame.Length);
        Assert.Equal(40, frame.Length);
        Assert.Equal(
            MiPlayFreshLegacyDeviceInfoLiveValidationEvidence.InitialSetLocalDeviceInfoFrameSha256,
            Convert.ToHexString(SHA256.HashData(frame)));
    }

    [Fact]
    public void RejectsDuplicateInitial0058RaceWithoutAllowingReply()
    {
        var session = new MiPlayFreshLegacyPostDeviceInfoObservationSession();
        var initialFrame =
            MiPlayFreshLegacyDeviceInfoLiveValidationEvidence.ReconstructInitialSetLocalDeviceInfoFrame();

        Assert.True(session.ProcessInboundFrame(initialFrame).Accepted);
        var duplicate = session.ProcessInboundFrame(initialFrame);

        Assert.False(duplicate.Accepted);
        Assert.True(duplicate.ExactInitialSetLocalDeviceInfoRaceObserved);
        Assert.False(duplicate.ExactSetLocalDeviceInfoObserved);
        Assert.False(duplicate.AllowsFollowUpSend);
        Assert.Contains("one allowed initial sequence 0x0002", duplicate.Boundary, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects0034Before0058()
    {
        var session = new MiPlayFreshLegacyPostDeviceInfoObservationSession();

        var result = session.ProcessInboundFrame(
            MiPlayFreshLegacyPostDeviceInfoProgressionEvidence.CreateOfflinePlan().PredictedGetMirrorModeFrame);

        Assert.False(result.Accepted);
        Assert.False(result.AllowsFollowUpSend);
        Assert.Contains("0x0058 sequence 0x0003", result.Boundary, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsWrongSequencePayloadOrInterleavedFrame()
    {
        var wrongSequence = new MiPlayFreshLegacyPostDeviceInfoObservationSession().ProcessInboundFrame(
            MiPlayCommandFrameCodec.Encode(
                MiPlayProtocolConstants.SetLocalDeviceInfoCommand,
                4,
                MiPlayLocalDeviceInfoPayloadCodec.EncodeIsSameAccount(0)));
        Assert.False(wrongSequence.Accepted);

        var session = new MiPlayFreshLegacyPostDeviceInfoObservationSession();
        Assert.True(session.ProcessInboundFrame(
            MiPlayFreshLegacyDeviceInfoLiveValidationEvidence.ReconstructAdvancedSetLocalDeviceInfoFrame()).Accepted);

        var heartbeat = session.ProcessInboundFrame(MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.HeartbeatCommand,
            4,
            []));
        Assert.False(heartbeat.Accepted);
        Assert.False(heartbeat.AllowsFollowUpSend);
        Assert.Contains("empty 0x0034 sequence 0x0004", heartbeat.Boundary, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsAnythingAfterSuccessfulObservation()
    {
        var session = new MiPlayFreshLegacyPostDeviceInfoObservationSession();
        session.ProcessInboundFrame(
            MiPlayFreshLegacyDeviceInfoLiveValidationEvidence.ReconstructAdvancedSetLocalDeviceInfoFrame());
        var plan = MiPlayFreshLegacyPostDeviceInfoProgressionEvidence.CreateOfflinePlan();
        Assert.True(session.ProcessInboundFrame(plan.PredictedGetMirrorModeFrame).Completed);

        var extra = session.ProcessInboundFrame(plan.PredictedGetMirrorModeFrame);

        Assert.False(extra.Accepted);
        Assert.True(extra.Completed);
        Assert.True(extra.ExactGetMirrorModeObserved);
        Assert.False(extra.AllowsFollowUpSend);
        Assert.Contains("already completed", extra.Boundary, StringComparison.Ordinal);
    }
}
