using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlaySetPlaySourceNativeNoResetOfficialJsonLiveValidationEvidenceTests
{
    [Fact]
    public void ConstantsCaptureNativeNoResetOfficialJsonS12Run()
    {
        Assert.Equal("192.168.10.4", MiPlaySetPlaySourceNativeNoResetOfficialJsonLiveValidationEvidence.DeviceAddress);
        Assert.Equal("192.168.10.9", MiPlaySetPlaySourceNativeNoResetOfficialJsonLiveValidationEvidence.LocalControlAddress);
        Assert.Equal(7_576, MiPlaySetPlaySourceNativeNoResetOfficialJsonLiveValidationEvidence.LocalControlPort);
        Assert.Equal(8_899, MiPlaySetPlaySourceNativeNoResetOfficialJsonLiveValidationEvidence.DeviceControlPort);
        Assert.Equal("1.94.13", MiPlaySetPlaySourceNativeNoResetOfficialJsonLiveValidationEvidence.CurrentLx06FirmwareVersion);
        Assert.Equal("2.1.5091615", MiPlaySetPlaySourceNativeNoResetOfficialJsonLiveValidationEvidence.ControlSessionVersionAcknowledgement);
        Assert.Equal("3.1.6030516", MiPlaySetPlaySourceNativeNoResetOfficialJsonLiveValidationEvidence.NativeSourceVersionSent);
        Assert.Equal((ushort)0x0001, MiPlaySetPlaySourceNativeNoResetOfficialJsonLiveValidationEvidence.NativeSourceVersionSequence);
        Assert.Equal((ushort)0x0002, MiPlaySetPlaySourceNativeNoResetOfficialJsonLiveValidationEvidence.SafetyInfoSequence);
        Assert.Equal((ushort)0x0003, MiPlaySetPlaySourceNativeNoResetOfficialJsonLiveValidationEvidence.LocalSafetyAuthSequence);
        Assert.Equal((ushort)0x0000, MiPlaySetPlaySourceNativeNoResetOfficialJsonLiveValidationEvidence.PeerSafetyAuthChallengeSequence);
        Assert.Equal((ushort)0x0004, MiPlaySetPlaySourceNativeNoResetOfficialJsonLiveValidationEvidence.SetPlaySourceSequence);
        Assert.Equal((ushort)0x0040, MiPlaySetPlaySourceNativeNoResetOfficialJsonLiveValidationEvidence.SetPlaySourceCommand);
        Assert.Equal(61, MiPlaySetPlaySourceNativeNoResetOfficialJsonLiveValidationEvidence.PlaintextPayloadLength);
        Assert.Equal(73, MiPlaySetPlaySourceNativeNoResetOfficialJsonLiveValidationEvidence.EncryptedSetPlaySourcePayloadLength);
        Assert.Equal(7, MiPlaySetPlaySourceNativeNoResetOfficialJsonLiveValidationEvidence.FollowUpFrameCountBeforeClose);
        Assert.Equal("peer-first:observed-s12-inbound-iv-type1", MiPlaySetPlaySourceNativeNoResetOfficialJsonLiveValidationEvidence.SelectedSafetyAuthCandidate);
        Assert.Equal("native-no-reset-outbound-type2", MiPlaySetPlaySourceNativeNoResetOfficialJsonLiveValidationEvidence.OutboundProfile);
        Assert.Equal(
            "{\"ref_channel\":\"playpage\",\"ref_function\":\"\",\"ref_content\":\"\"}",
            MiPlaySetPlaySourceNativeNoResetOfficialJsonLiveValidationEvidence.OfficialMinimalPayloadText);
    }

    [Fact]
    public void SnapshotPreservesOneFrameNoMediaBoundary()
    {
        var snapshot = MiPlaySetPlaySourceNativeNoResetOfficialJsonLiveValidationEvidence.CreateCurrentSnapshot();

        Assert.True(snapshot.MutualSafetyAuthCompleted);
        Assert.True(snapshot.OfficialMinimalJsonPayloadSent);
        Assert.True(snapshot.NativeNoResetOutboundProfileUsed);
        Assert.True(snapshot.SafetyDataWrappedSetPlaySourceSent);
        Assert.False(snapshot.SetPlaySourceAcknowledgementObserved);
        Assert.True(snapshot.DeviceClosedControlAfterSetPlaySource);
        Assert.False(snapshot.RetryOrFallbackSent);
        Assert.False(snapshot.CmdOpenSent);
        Assert.False(snapshot.SetLocalDeviceInfo0058Sent);
        Assert.False(snapshot.AddMirrorSent);
        Assert.False(snapshot.GetDeviceInfoSent);
        Assert.False(snapshot.RtspListenerOrResponseUsed);
        Assert.False(snapshot.MediaOrRtpSent);
        Assert.False(snapshot.PlaybackOrAudioSent);
    }

    [Fact]
    public void NegativeResultRulesOutPromotedIvAsOnlyFailureButDoesNotAuthorizeNextFrame()
    {
        var decision = MiPlaySetPlaySourceNativeNoResetOfficialJsonLiveValidationEvidence.EvaluateResult(
            MiPlaySetPlaySourceNativeNoResetOfficialJsonLiveValidationEvidence.CreateCurrentSnapshot());

        Assert.False(decision.NativeNoResetOfficialJsonAccepted);
        Assert.False(decision.AuthorizesNextBusinessFrame);
        Assert.Contains("closed without a 0x0041 acknowledgement", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("promoted-inbound-IV", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("native no-reset plus minimal JSON is still insufficient", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("continue offline", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void BoundaryExpansionInvalidatesNativeNoResetEvidence()
    {
        var unsafeSnapshot = MiPlaySetPlaySourceNativeNoResetOfficialJsonLiveValidationEvidence.CreateCurrentSnapshot() with
        {
            AddMirrorSent = true,
        };

        var decision = MiPlaySetPlaySourceNativeNoResetOfficialJsonLiveValidationEvidence.EvaluateResult(unsafeSnapshot);

        Assert.False(decision.NativeNoResetOfficialJsonAccepted);
        Assert.False(decision.AuthorizesNextBusinessFrame);
        Assert.Contains("boundary was exceeded", decision.Reason, StringComparison.Ordinal);
    }
}