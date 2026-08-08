using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayPostAuthOutboundProfileDryRunLiveValidationEvidenceTests
{
    [Fact]
    public void ConstantsCaptureBoundedS12DryRun()
    {
        Assert.Equal("192.168.10.4", MiPlayPostAuthOutboundProfileDryRunLiveValidationEvidence.DeviceAddress);
        Assert.Equal("192.168.10.9", MiPlayPostAuthOutboundProfileDryRunLiveValidationEvidence.LocalControlAddress);
        Assert.Equal(12_679, MiPlayPostAuthOutboundProfileDryRunLiveValidationEvidence.LocalControlPort);
        Assert.Equal(8_899, MiPlayPostAuthOutboundProfileDryRunLiveValidationEvidence.DeviceControlPort);
        Assert.Equal("1.94.13", MiPlayPostAuthOutboundProfileDryRunLiveValidationEvidence.CurrentLx06FirmwareVersion);
        Assert.Equal("2.1.5091615", MiPlayPostAuthOutboundProfileDryRunLiveValidationEvidence.ControlSessionVersionAcknowledgement);
        Assert.Equal("3.1.6030516", MiPlayPostAuthOutboundProfileDryRunLiveValidationEvidence.NativeSourceVersionSent);
        Assert.Equal((ushort)0x0001, MiPlayPostAuthOutboundProfileDryRunLiveValidationEvidence.NativeSourceVersionSequence);
        Assert.Equal((ushort)0x0002, MiPlayPostAuthOutboundProfileDryRunLiveValidationEvidence.SafetyInfoSequence);
        Assert.Equal((ushort)0x0003, MiPlayPostAuthOutboundProfileDryRunLiveValidationEvidence.LocalSafetyAuthSequence);
        Assert.Equal((ushort)0x0000, MiPlayPostAuthOutboundProfileDryRunLiveValidationEvidence.PeerSafetyAuthChallengeSequence);
        Assert.Equal((ushort)0x0004, MiPlayPostAuthOutboundProfileDryRunLiveValidationEvidence.DryRunPostAuthSequence);
        Assert.Equal((ushort)0x0040, MiPlayPostAuthOutboundProfileDryRunLiveValidationEvidence.DryRunCommand);
        Assert.Equal(61, MiPlayPostAuthOutboundProfileDryRunLiveValidationEvidence.OfficialJsonPlaintextLength);
        Assert.Equal(73, MiPlayPostAuthOutboundProfileDryRunLiveValidationEvidence.DryRunSafetyDataPayloadLength);
        Assert.Equal(82, MiPlayPostAuthOutboundProfileDryRunLiveValidationEvidence.DryRunCommandFrameLength);
        Assert.Equal("peer-first:observed-s12-inbound-iv-type1", MiPlayPostAuthOutboundProfileDryRunLiveValidationEvidence.SelectedSafetyAuthCandidate);
    }

    [Fact]
    public void RealSessionDryRunHashesDistinguishNativeOutboundFromOldProbeNegativeControl()
    {
        Assert.Equal("native-no-reset-outbound-type2", MiPlayPostAuthOutboundProfileDryRunLiveValidationEvidence.NativeNoResetOutboundProfile);
        Assert.Equal("observed-inbound-promoted-outbound-type1", MiPlayPostAuthOutboundProfileDryRunLiveValidationEvidence.OldProbeNegativeControlProfile);
        Assert.Equal("29508b1064aaaa901e5de0d9e0b4467b4fcd42a9f334f4bca9f681fc3f0665bd", MiPlayPostAuthOutboundProfileDryRunLiveValidationEvidence.NativeNoResetCommandFrameSha256);
        Assert.Equal("41d298788a1a63930b706eb82c55554e756161024032a4148fd75f058948bee7", MiPlayPostAuthOutboundProfileDryRunLiveValidationEvidence.OldProbeNegativeControlCommandFrameSha256);
        Assert.NotEqual(
            MiPlayPostAuthOutboundProfileDryRunLiveValidationEvidence.NativeNoResetCommandFrameSha256,
            MiPlayPostAuthOutboundProfileDryRunLiveValidationEvidence.OldProbeNegativeControlCommandFrameSha256);
    }

    [Fact]
    public void SnapshotPreservesNoPostAuthBusinessBoundary()
    {
        var snapshot = MiPlayPostAuthOutboundProfileDryRunLiveValidationEvidence.CreateCurrentSnapshot();

        Assert.True(snapshot.MutualSafetyAuthCompleted);
        Assert.True(snapshot.PeerSafetyAuthChallengeDecoded);
        Assert.True(snapshot.PeerSafetyAuthAcknowledgementVerified);
        Assert.True(snapshot.DryRunComparisonPrinted);
        Assert.False(snapshot.PostAuthBusinessFrameSent);
        Assert.False(snapshot.SetPlaySourceFrameSent);
        Assert.False(snapshot.GetDeviceInfoFrameSent);
        Assert.False(snapshot.SetLocalDeviceInfo0058Sent);
        Assert.False(snapshot.CmdOpenSent);
        Assert.False(snapshot.AddMirrorSent);
        Assert.False(snapshot.RtspListenerOrResponseUsed);
        Assert.False(snapshot.MediaOrRtpSent);
        Assert.False(snapshot.PlaybackOrAudioSent);
    }

    [Fact]
    public void DryRunEvidenceIsUsefulButDoesNotAuthorizeBusinessSend()
    {
        var decision = MiPlayPostAuthOutboundProfileDryRunLiveValidationEvidence.EvaluateResult(
            MiPlayPostAuthOutboundProfileDryRunLiveValidationEvidence.CreateCurrentSnapshot());

        Assert.True(decision.UsefulDryRunEvidence);
        Assert.False(decision.AuthorizesPostAuthBusinessSend);
        Assert.Contains("different Cmd_SetPlaySource frame hashes", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("does not prove receiver acceptance", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void BoundaryExpansionInvalidatesDryRunEvidence()
    {
        var unsafeSnapshot = MiPlayPostAuthOutboundProfileDryRunLiveValidationEvidence.CreateCurrentSnapshot() with
        {
            SetPlaySourceFrameSent = true,
        };

        var decision = MiPlayPostAuthOutboundProfileDryRunLiveValidationEvidence.EvaluateResult(unsafeSnapshot);

        Assert.False(decision.UsefulDryRunEvidence);
        Assert.False(decision.AuthorizesPostAuthBusinessSend);
        Assert.Contains("boundary was exceeded", decision.Reason, StringComparison.Ordinal);
    }
}