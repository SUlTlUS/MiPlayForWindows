using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayOfficialPostAuthSequenceLiveValidationEvidenceTests
{
    [Fact]
    public void ConstantsCaptureBoundedOfficialSequenceS12Run()
    {
        Assert.Equal("192.168.10.4", MiPlayOfficialPostAuthSequenceLiveValidationEvidence.DeviceAddress);
        Assert.Equal("192.168.10.9", MiPlayOfficialPostAuthSequenceLiveValidationEvidence.LocalControlAddress);
        Assert.Equal(4_434, MiPlayOfficialPostAuthSequenceLiveValidationEvidence.LocalControlPort);
        Assert.Equal(8_899, MiPlayOfficialPostAuthSequenceLiveValidationEvidence.DeviceControlPort);
        Assert.Equal("1.94.13", MiPlayOfficialPostAuthSequenceLiveValidationEvidence.CurrentLx06FirmwareVersion);
        Assert.Equal("2.1.5091615", MiPlayOfficialPostAuthSequenceLiveValidationEvidence.ControlSessionVersionAcknowledgement);
        Assert.Equal("3.1.6030516", MiPlayOfficialPostAuthSequenceLiveValidationEvidence.NativeSourceVersionSent);
        Assert.Equal((ushort)0x0001, MiPlayOfficialPostAuthSequenceLiveValidationEvidence.NativeSourceVersionSequence);
        Assert.Equal((ushort)0x0002, MiPlayOfficialPostAuthSequenceLiveValidationEvidence.SafetyInfoSequence);
        Assert.Equal((ushort)0x0003, MiPlayOfficialPostAuthSequenceLiveValidationEvidence.LocalSafetyAuthSequence);
        Assert.Equal((ushort)0x0000, MiPlayOfficialPostAuthSequenceLiveValidationEvidence.PeerSafetyAuthChallengeSequence);
        Assert.Equal((ushort)0x0003, MiPlayOfficialPostAuthSequenceLiveValidationEvidence.PeerSafetyAuthProofSequence);
        Assert.Equal((ushort)0x0335, MiPlayOfficialPostAuthSequenceLiveValidationEvidence.ModeNotifySequence);
        Assert.Equal((ushort)0x0336, MiPlayOfficialPostAuthSequenceLiveValidationEvidence.MediaInfoNotifySequence);
        Assert.Equal((ushort)0x0337, MiPlayOfficialPostAuthSequenceLiveValidationEvidence.StateNotifySequence);
        Assert.Equal(0, MiPlayOfficialPostAuthSequenceLiveValidationEvidence.StateNotifyIntegerValue);
    }

    [Fact]
    public void CapturesFirst0058AbortBeforeLaterOfficialSteps()
    {
        Assert.Equal((ushort)0x0004, MiPlayOfficialPostAuthSequenceLiveValidationEvidence.FirstPostAuthCommandSequence);
        Assert.Equal(MiPlayProtocolConstants.SetLocalDeviceInfoCommand, MiPlayOfficialPostAuthSequenceLiveValidationEvidence.FirstPostAuthCommand);
        Assert.Equal(MiPlayProtocolConstants.SetLocalDeviceInfoAcknowledgementCommand, MiPlayOfficialPostAuthSequenceLiveValidationEvidence.ExpectedFirstPostAuthAcknowledgement);
        Assert.Equal("SendSourceName", MiPlayOfficialPostAuthSequenceLiveValidationEvidence.FirstPostAuthStepKind);
        Assert.Equal(
            "{\"sourceName\":\"DLNACast Windows\",\"mSourceBtMac\":\"\"}",
            MiPlayOfficialPostAuthSequenceLiveValidationEvidence.FirstPostAuthPlaintextPayload);
        Assert.Equal(51, MiPlayOfficialPostAuthSequenceLiveValidationEvidence.FirstPostAuthPlaintextPayloadLength);
        Assert.Equal(73, MiPlayOfficialPostAuthSequenceLiveValidationEvidence.FirstPostAuthEncryptedPayloadLength);
        Assert.Equal(105, MiPlayOfficialPostAuthSequenceLiveValidationEvidence.OfficialPhoneFirst0058SafetyDataPayloadLength);
        Assert.Equal(0, MiPlayOfficialPostAuthSequenceLiveValidationEvidence.PostAuthFramesObservedAfterFirst0058);
        Assert.Equal(7, MiPlayOfficialPostAuthSequenceLiveValidationEvidence.FollowUpFrameCountBeforeAbort);
        Assert.Equal(10_053, MiPlayOfficialPostAuthSequenceLiveValidationEvidence.SocketNativeErrorAfterFirst0058);
        Assert.Equal("peer-first:observed-s12-inbound-iv-type1", MiPlayOfficialPostAuthSequenceLiveValidationEvidence.SelectedSafetyAuthCandidate);
        Assert.Equal("native-no-reset-outbound-type2", MiPlayOfficialPostAuthSequenceLiveValidationEvidence.OutboundProfile);
        Assert.Equal("Xiaomi 13 Pro", MiPlayOfficialPostAuthSequenceLiveValidationEvidence.RecoveredOfficialPhoneSourceName);
    }

    [Fact]
    public void SnapshotPreservesNoLaterCommandOrMediaBoundary()
    {
        var snapshot = MiPlayOfficialPostAuthSequenceLiveValidationEvidence.CreateCurrentSnapshot();

        Assert.True(snapshot.MutualSafetyAuthCompleted);
        Assert.True(snapshot.NativeNoResetOutboundProfileUsed);
        Assert.True(snapshot.OfficialSequencePlanPrepared);
        Assert.True(snapshot.SourceNameLocalDeviceInfo0058Sent);
        Assert.True(snapshot.SourceNamePayloadUsedDefaultWindowsIdentity);
        Assert.False(snapshot.SourceNamePayloadMatchedRecoveredPhoneIdentity);
        Assert.False(snapshot.LocalDeviceInfoAcknowledgement0059ObservedAfterSourceName);
        Assert.False(snapshot.GetDeviceInfo001eSent);
        Assert.False(snapshot.CanAlonePlayCtrl0058Sent);
        Assert.False(snapshot.AlonePlayCapacity0058Sent);
        Assert.False(snapshot.GetMirrorMode0034Sent);
        Assert.False(snapshot.SetPlaySource0040Sent);
        Assert.True(snapshot.SocketAbortedAfterFirst0058);
        Assert.False(snapshot.RetryOrFallbackSent);
        Assert.False(snapshot.CmdOpenSent);
        Assert.False(snapshot.AddMirrorSent);
        Assert.False(snapshot.RtspListenerOrResponseUsed);
        Assert.False(snapshot.MediaOrRtpSent);
        Assert.False(snapshot.PlaybackOrAudioSent);
    }

    [Fact]
    public void NegativeResultLocalizesEarliestFailingGateToDefaultSourceIdentity()
    {
        var decision = MiPlayOfficialPostAuthSequenceLiveValidationEvidence.EvaluateResult(
            MiPlayOfficialPostAuthSequenceLiveValidationEvidence.CreateCurrentSnapshot());

        Assert.False(decision.OfficialSequenceAccepted);
        Assert.False(decision.AuthorizesNextFrame);
        Assert.Contains("closed after the first SafetyData-wrapped 0x0058", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("No 0x001e, 0x0034, or 0x0040 was sent", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("default Windows sourceName", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("empty mSourceBtMac", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("does not reject the later recovered official command order", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("authorizes no retry", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("audio", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BoundaryExpansionInvalidatesOfficialSequenceEvidence()
    {
        var unsafeSnapshot = MiPlayOfficialPostAuthSequenceLiveValidationEvidence.CreateCurrentSnapshot() with
        {
            AddMirrorSent = true,
        };

        var decision = MiPlayOfficialPostAuthSequenceLiveValidationEvidence.EvaluateResult(unsafeSnapshot);

        Assert.False(decision.OfficialSequenceAccepted);
        Assert.False(decision.AuthorizesNextFrame);
        Assert.Contains("boundary was exceeded", decision.Reason, StringComparison.Ordinal);
    }
}
