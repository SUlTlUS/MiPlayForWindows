using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayPostAuthReadOnlyGetDeviceInfoLiveValidationEvidenceTests
{
    [Fact]
    public void ConstantsCaptureSingleReadonlyPostAuthGetDeviceInfoRun()
    {
        Assert.Equal("192.168.10.4", MiPlayPostAuthReadOnlyGetDeviceInfoLiveValidationEvidence.DeviceAddress);
        Assert.Equal("192.168.10.9", MiPlayPostAuthReadOnlyGetDeviceInfoLiveValidationEvidence.LocalControlAddress);
        Assert.Equal(1_542, MiPlayPostAuthReadOnlyGetDeviceInfoLiveValidationEvidence.LocalControlPort);
        Assert.Equal(8_899, MiPlayPostAuthReadOnlyGetDeviceInfoLiveValidationEvidence.DeviceControlPort);
        Assert.Equal("1.94.13", MiPlayPostAuthReadOnlyGetDeviceInfoLiveValidationEvidence.CurrentLx06FirmwareVersion);
        Assert.Equal("2.1.5091615", MiPlayPostAuthReadOnlyGetDeviceInfoLiveValidationEvidence.ControlSessionVersionAcknowledgement);
        Assert.Equal("3.1.6030516", MiPlayPostAuthReadOnlyGetDeviceInfoLiveValidationEvidence.NativeSourceVersionSent);
        Assert.Equal((ushort)0x0001, MiPlayPostAuthReadOnlyGetDeviceInfoLiveValidationEvidence.NativeSourceVersionSequence);
        Assert.Equal((ushort)0x0002, MiPlayPostAuthReadOnlyGetDeviceInfoLiveValidationEvidence.SafetyInfoSequence);
        Assert.Equal((ushort)0x0003, MiPlayPostAuthReadOnlyGetDeviceInfoLiveValidationEvidence.LocalSafetyAuthSequence);
        Assert.Equal((ushort)0x0000, MiPlayPostAuthReadOnlyGetDeviceInfoLiveValidationEvidence.PeerSafetyAuthChallengeSequence);
        Assert.Equal((ushort)0x0225, MiPlayPostAuthReadOnlyGetDeviceInfoLiveValidationEvidence.ReadyStateNotifySequence);
        Assert.Equal("state", MiPlayPostAuthReadOnlyGetDeviceInfoLiveValidationEvidence.ReadyStateNotifyLabel);
        Assert.Equal(3, MiPlayPostAuthReadOnlyGetDeviceInfoLiveValidationEvidence.ReadyStateNotifyIntegerValue);
    }

    [Fact]
    public void CapturesReadOnlyGetDeviceInfoFrameAndNoAcknowledgement()
    {
        Assert.Equal(MiPlayProtocolConstants.GetDeviceInfoCommand, MiPlayPostAuthReadOnlyGetDeviceInfoLiveValidationEvidence.GetDeviceInfoCommand);
        Assert.Equal((ushort)0x0004, MiPlayPostAuthReadOnlyGetDeviceInfoLiveValidationEvidence.GetDeviceInfoSequence);
        Assert.Equal(0, MiPlayPostAuthReadOnlyGetDeviceInfoLiveValidationEvidence.GetDeviceInfoPlaintextPayloadLength);
        Assert.Equal(25, MiPlayPostAuthReadOnlyGetDeviceInfoLiveValidationEvidence.EncryptedGetDeviceInfoPayloadLength);
        Assert.Equal(MiPlayProtocolConstants.GetDeviceInfoAcknowledgementCommand, MiPlayPostAuthReadOnlyGetDeviceInfoLiveValidationEvidence.ExpectedGetDeviceInfoAcknowledgementCommand);
        Assert.Equal(MiPlayPostAuthProbePolicy.MinimumDeviceInfoAcknowledgementPayloadLength, MiPlayPostAuthReadOnlyGetDeviceInfoLiveValidationEvidence.MinimumExpectedAcknowledgementPayloadLength);
        Assert.Equal(7, MiPlayPostAuthReadOnlyGetDeviceInfoLiveValidationEvidence.FollowUpFrameCountBeforeClose);
        Assert.Equal("peer-first:observed-s12-inbound-iv-type1", MiPlayPostAuthReadOnlyGetDeviceInfoLiveValidationEvidence.SelectedSafetyAuthCandidate);
        Assert.Equal("native-no-reset-outbound-type2", MiPlayPostAuthReadOnlyGetDeviceInfoLiveValidationEvidence.OutboundProfile);
    }

    [Fact]
    public void SnapshotPreservesNoBusinessOrMediaBoundary()
    {
        var snapshot = MiPlayPostAuthReadOnlyGetDeviceInfoLiveValidationEvidence.CreateCurrentSnapshot();

        Assert.True(snapshot.MutualSafetyAuthCompleted);
        Assert.True(snapshot.NativeNoResetOutboundProfileUsed);
        Assert.True(snapshot.SafetyDataWrappedGetDeviceInfoSent);
        Assert.True(snapshot.EmptyPlaintextPayloadSent);
        Assert.False(snapshot.GetDeviceInfoAcknowledgementObserved);
        Assert.True(snapshot.DeviceClosedControlAfterGetDeviceInfo);
        Assert.False(snapshot.RetryOrFallbackSent);
        Assert.False(snapshot.SetPlaySource0040Sent);
        Assert.False(snapshot.SetLocalDeviceInfo0058Sent);
        Assert.False(snapshot.CmdOpenSent);
        Assert.False(snapshot.AddMirrorSent);
        Assert.False(snapshot.RtspListenerOrResponseUsed);
        Assert.False(snapshot.MediaOrRtpSent);
        Assert.False(snapshot.PlaybackOrAudioSent);
    }

    [Fact]
    public void NegativeResultDoesNotAuthorizeNextFrame()
    {
        var decision = MiPlayPostAuthReadOnlyGetDeviceInfoLiveValidationEvidence.EvaluateResult(
            MiPlayPostAuthReadOnlyGetDeviceInfoLiveValidationEvidence.CreateCurrentSnapshot());

        Assert.False(decision.PostAuthGetDeviceInfoAccepted);
        Assert.False(decision.AuthorizesNextFrame);
        Assert.Contains("closed without a same-sequence 0x001f", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("legacy clear 0x001e success", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("does not distinguish cipher phase mismatch", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("authorizes no 0x0040", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("RTSP", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("audio", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BoundaryExpansionInvalidatesReadOnlyEvidence()
    {
        Assert.False(MiPlayPostAuthReadOnlyGetDeviceInfoLiveValidationEvidence.EvaluateResult(
            MiPlayPostAuthReadOnlyGetDeviceInfoLiveValidationEvidence.CreateCurrentSnapshot() with { SetPlaySource0040Sent = true }).AuthorizesNextFrame);
        Assert.False(MiPlayPostAuthReadOnlyGetDeviceInfoLiveValidationEvidence.EvaluateResult(
            MiPlayPostAuthReadOnlyGetDeviceInfoLiveValidationEvidence.CreateCurrentSnapshot() with { SetLocalDeviceInfo0058Sent = true }).AuthorizesNextFrame);
        Assert.False(MiPlayPostAuthReadOnlyGetDeviceInfoLiveValidationEvidence.EvaluateResult(
            MiPlayPostAuthReadOnlyGetDeviceInfoLiveValidationEvidence.CreateCurrentSnapshot() with { CmdOpenSent = true }).AuthorizesNextFrame);
        Assert.False(MiPlayPostAuthReadOnlyGetDeviceInfoLiveValidationEvidence.EvaluateResult(
            MiPlayPostAuthReadOnlyGetDeviceInfoLiveValidationEvidence.CreateCurrentSnapshot() with { RtspListenerOrResponseUsed = true }).AuthorizesNextFrame);
        Assert.False(MiPlayPostAuthReadOnlyGetDeviceInfoLiveValidationEvidence.EvaluateResult(
            MiPlayPostAuthReadOnlyGetDeviceInfoLiveValidationEvidence.CreateCurrentSnapshot() with { PlaybackOrAudioSent = true }).AuthorizesNextFrame);
    }
}
