using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayOfficialPostAuthRecoveredIdentityLiveValidationEvidenceTests
{
    [Fact]
    public void ConstantsCaptureRecoveredIdentityS12Run()
    {
        Assert.Equal("192.168.10.4", MiPlayOfficialPostAuthRecoveredIdentityLiveValidationEvidence.DeviceAddress);
        Assert.Equal("192.168.10.9", MiPlayOfficialPostAuthRecoveredIdentityLiveValidationEvidence.LocalControlAddress);
        Assert.Equal(1_776, MiPlayOfficialPostAuthRecoveredIdentityLiveValidationEvidence.LocalControlPort);
        Assert.Equal(8_899, MiPlayOfficialPostAuthRecoveredIdentityLiveValidationEvidence.DeviceControlPort);
        Assert.Equal("1.94.13", MiPlayOfficialPostAuthRecoveredIdentityLiveValidationEvidence.CurrentLx06FirmwareVersion);
        Assert.Equal("2.1.5091615", MiPlayOfficialPostAuthRecoveredIdentityLiveValidationEvidence.ControlSessionVersionAcknowledgement);
        Assert.Equal("3.1.6030516", MiPlayOfficialPostAuthRecoveredIdentityLiveValidationEvidence.NativeSourceVersionSent);
        Assert.Equal((ushort)0x0001, MiPlayOfficialPostAuthRecoveredIdentityLiveValidationEvidence.NativeSourceVersionSequence);
        Assert.Equal((ushort)0x0002, MiPlayOfficialPostAuthRecoveredIdentityLiveValidationEvidence.SafetyInfoSequence);
        Assert.Equal((ushort)0x0003, MiPlayOfficialPostAuthRecoveredIdentityLiveValidationEvidence.LocalSafetyAuthSequence);
        Assert.Equal((ushort)0x0000, MiPlayOfficialPostAuthRecoveredIdentityLiveValidationEvidence.PeerSafetyAuthChallengeSequence);
        Assert.Equal((ushort)0x0003, MiPlayOfficialPostAuthRecoveredIdentityLiveValidationEvidence.PeerSafetyAuthProofSequence);
        Assert.Equal((ushort)0x0338, MiPlayOfficialPostAuthRecoveredIdentityLiveValidationEvidence.LegacySafetyAuthChallengeSequence);
        Assert.Equal((ushort)0x0339, MiPlayOfficialPostAuthRecoveredIdentityLiveValidationEvidence.ModeNotifySequence);
        Assert.Equal((ushort)0x033A, MiPlayOfficialPostAuthRecoveredIdentityLiveValidationEvidence.MediaInfoNotifySequence);
        Assert.Equal((ushort)0x033B, MiPlayOfficialPostAuthRecoveredIdentityLiveValidationEvidence.StateNotifySequence);
        Assert.Equal(0, MiPlayOfficialPostAuthRecoveredIdentityLiveValidationEvidence.StateNotifyIntegerValue);
    }

    [Fact]
    public void CapturesRecoveredFirst0058AbortBeforeLaterOfficialSteps()
    {
        Assert.Equal((ushort)0x0004, MiPlayOfficialPostAuthRecoveredIdentityLiveValidationEvidence.FirstPostAuthCommandSequence);
        Assert.Equal(MiPlayProtocolConstants.SetLocalDeviceInfoCommand, MiPlayOfficialPostAuthRecoveredIdentityLiveValidationEvidence.FirstPostAuthCommand);
        Assert.Equal(MiPlayProtocolConstants.SetLocalDeviceInfoAcknowledgementCommand, MiPlayOfficialPostAuthRecoveredIdentityLiveValidationEvidence.ExpectedFirstPostAuthAcknowledgement);
        Assert.Equal("SendSourceName", MiPlayOfficialPostAuthRecoveredIdentityLiveValidationEvidence.FirstPostAuthStepKind);
        Assert.Equal(80, MiPlayOfficialPostAuthRecoveredIdentityLiveValidationEvidence.FirstPostAuthPlaintextPayloadLength);
        Assert.Equal(105, MiPlayOfficialPostAuthRecoveredIdentityLiveValidationEvidence.FirstPostAuthEncryptedPayloadLength);
        Assert.Equal(73, MiPlayOfficialPostAuthRecoveredIdentityLiveValidationEvidence.PreviousDefaultWindowsEncryptedPayloadLength);
        Assert.Equal(0, MiPlayOfficialPostAuthRecoveredIdentityLiveValidationEvidence.PostAuthFramesObservedAfterFirst0058);
        Assert.Equal(7, MiPlayOfficialPostAuthRecoveredIdentityLiveValidationEvidence.FollowUpFrameCountBeforeAbort);
        Assert.Equal(10_053, MiPlayOfficialPostAuthRecoveredIdentityLiveValidationEvidence.SocketNativeErrorAfterFirst0058);
        Assert.Equal("peer-first:observed-s12-inbound-iv-type1", MiPlayOfficialPostAuthRecoveredIdentityLiveValidationEvidence.SelectedSafetyAuthCandidate);
        Assert.Equal("native-no-reset-outbound-type2", MiPlayOfficialPostAuthRecoveredIdentityLiveValidationEvidence.OutboundProfile);
    }

    [Fact]
    public void SnapshotPreservesNoLaterCommandOrMediaBoundary()
    {
        var snapshot = MiPlayOfficialPostAuthRecoveredIdentityLiveValidationEvidence.CreateCurrentSnapshot();

        Assert.True(snapshot.MutualSafetyAuthCompleted);
        Assert.True(snapshot.NativeNoResetOutboundProfileUsed);
        Assert.True(snapshot.RecoveredOfficialSourceIdentitySent);
        Assert.True(snapshot.FirstFrameMatchedRecoveredPhonePcapLength);
        Assert.False(snapshot.LocalDeviceInfoAcknowledgement0059Observed);
        Assert.False(snapshot.GetDeviceInfo001eSent);
        Assert.False(snapshot.CanAlonePlayCtrl0058Sent);
        Assert.False(snapshot.AlonePlayCapacity0058Sent);
        Assert.False(snapshot.GetMirrorMode0034Sent);
        Assert.False(snapshot.SetPlaySource0040Sent);
        Assert.True(snapshot.SocketAbortedAfterRecoveredIdentity0058);
        Assert.False(snapshot.RetryOrFallbackSent);
        Assert.False(snapshot.CmdOpenSent);
        Assert.False(snapshot.AddMirrorSent);
        Assert.False(snapshot.RtspListenerOrResponseUsed);
        Assert.False(snapshot.MediaOrRtpSent);
        Assert.False(snapshot.PlaybackOrAudioSent);
    }

    [Fact]
    public void NegativeResultRulesOutOnlyMidSession0058AsFreshSuccessor()
    {
        var decision = MiPlayOfficialPostAuthRecoveredIdentityLiveValidationEvidence.EvaluateResult(
            MiPlayOfficialPostAuthRecoveredIdentityLiveValidationEvidence.CreateCurrentSnapshot());

        Assert.False(decision.RecoveredIdentityAccepted);
        Assert.False(decision.AuthorizesNextFrame);
        Assert.Contains("recovered official 80-byte / 105-byte 0x0058", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("without a 0x0059", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("No 0x001e, 0x0034, or 0x0040 was sent", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("captured mid-session at sequence 0x013a", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("does not rule out the same 0x0058 payload", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("authorizes no retry", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("first command after a fresh type-2 mutual SafetyAuth", decision.NextOfflineTarget, StringComparison.Ordinal);
        Assert.Contains("no business acknowledgement", decision.NextOfflineTarget, StringComparison.Ordinal);
    }

    [Fact]
    public void BoundaryExpansionInvalidatesRecoveredIdentityEvidence()
    {
        var unsafeSnapshot = MiPlayOfficialPostAuthRecoveredIdentityLiveValidationEvidence.CreateCurrentSnapshot() with
        {
            SetPlaySource0040Sent = true,
        };

        var decision = MiPlayOfficialPostAuthRecoveredIdentityLiveValidationEvidence.EvaluateResult(unsafeSnapshot);

        Assert.False(decision.RecoveredIdentityAccepted);
        Assert.False(decision.AuthorizesNextFrame);
        Assert.Contains("boundary was exceeded", decision.Reason, StringComparison.Ordinal);
    }
}
