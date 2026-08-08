using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlaySetPlaySourceOfficialJsonOneFrameLiveValidationEvidenceTests
{
    [Fact]
    public void ConstantsCaptureOfficialJsonOneFrameS12Run()
    {
        Assert.Equal("192.168.10.4", MiPlaySetPlaySourceOfficialJsonOneFrameLiveValidationEvidence.DeviceAddress);
        Assert.Equal("192.168.10.9", MiPlaySetPlaySourceOfficialJsonOneFrameLiveValidationEvidence.LocalControlAddress);
        Assert.Equal(12_037, MiPlaySetPlaySourceOfficialJsonOneFrameLiveValidationEvidence.LocalControlPort);
        Assert.Equal(8_899, MiPlaySetPlaySourceOfficialJsonOneFrameLiveValidationEvidence.DeviceControlPort);
        Assert.Equal("1.94.13", MiPlaySetPlaySourceOfficialJsonOneFrameLiveValidationEvidence.CurrentLx06FirmwareVersion);
        Assert.Equal("2.1.5091615", MiPlaySetPlaySourceOfficialJsonOneFrameLiveValidationEvidence.ControlSessionVersionAcknowledgement);
        Assert.Equal("3.1.6030516", MiPlaySetPlaySourceOfficialJsonOneFrameLiveValidationEvidence.NativeSourceVersionSent);
        Assert.Equal((ushort)0x0001, MiPlaySetPlaySourceOfficialJsonOneFrameLiveValidationEvidence.NativeSourceVersionSequence);
        Assert.Equal((ushort)0x0002, MiPlaySetPlaySourceOfficialJsonOneFrameLiveValidationEvidence.SafetyInfoSequence);
        Assert.Equal((ushort)0x0003, MiPlaySetPlaySourceOfficialJsonOneFrameLiveValidationEvidence.LocalSafetyAuthSequence);
        Assert.Equal((ushort)0x0000, MiPlaySetPlaySourceOfficialJsonOneFrameLiveValidationEvidence.PeerSafetyAuthChallengeSequence);
        Assert.Equal((ushort)0x0004, MiPlaySetPlaySourceOfficialJsonOneFrameLiveValidationEvidence.SetPlaySourceSequence);
        Assert.Equal(
            "{\"ref_channel\":\"playpage\",\"ref_function\":\"\",\"ref_content\":\"\"}",
            MiPlaySetPlaySourceOfficialJsonOneFrameLiveValidationEvidence.OfficialMinimalPayloadText);
        Assert.Equal(61, MiPlaySetPlaySourceOfficialJsonOneFrameLiveValidationEvidence.PlaintextPayloadLength);
        Assert.Equal(73, MiPlaySetPlaySourceOfficialJsonOneFrameLiveValidationEvidence.EncryptedSetPlaySourcePayloadLength);
        Assert.Equal(7, MiPlaySetPlaySourceOfficialJsonOneFrameLiveValidationEvidence.FollowUpFrameCountBeforeClose);
        Assert.Equal(10_053, MiPlaySetPlaySourceOfficialJsonOneFrameLiveValidationEvidence.SocketNativeErrorAfterSetPlaySource);
        Assert.Equal("peer-first:observed-s12-inbound-iv-type1", MiPlaySetPlaySourceOfficialJsonOneFrameLiveValidationEvidence.SelectedSafetyDataCandidate);
    }

    [Fact]
    public void SnapshotPreservesOneFrameNoMediaBoundary()
    {
        var snapshot = MiPlaySetPlaySourceOfficialJsonOneFrameLiveValidationEvidence.CreateCurrentSnapshot();

        Assert.True(snapshot.MutualSafetyAuthCompleted);
        Assert.True(snapshot.OfficialMinimalJsonPayloadSent);
        Assert.True(snapshot.SafetyDataWrappedSetPlaySourceSent);
        Assert.False(snapshot.SetPlaySourceAcknowledgementObserved);
        Assert.True(snapshot.DeviceClosedControlAfterSetPlaySource);
        Assert.False(snapshot.RetryOrFallbackSent);
        Assert.False(snapshot.CmdOpenSent);
        Assert.False(snapshot.SetLocalDeviceInfo0058Sent);
        Assert.False(snapshot.AddMirrorSent);
        Assert.False(snapshot.RtspListenerOrResponseUsed);
        Assert.False(snapshot.MediaOrRtpSent);
        Assert.False(snapshot.PlaybackOrAudioSent);
    }

    [Fact]
    public void NegativeOfficialJsonResultRulesOutMissingSourceJsonAsPrimaryCause()
    {
        var decision = MiPlaySetPlaySourceOfficialJsonOneFrameLiveValidationEvidence.EvaluateResult(
            MiPlaySetPlaySourceOfficialJsonOneFrameLiveValidationEvidence.CreateCurrentSnapshot());

        Assert.False(decision.OfficialJsonSetPlaySourceAccepted);
        Assert.Contains("without a 0x0041 acknowledgement", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("both empty and official JSON 0x0040 probes", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("unlikely to be ref_channel/ref_function/ref_content", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("SafetyData direction/IV", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("command envelope", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("handler ownership", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void BoundaryExpansionInvalidatesOfficialJsonEvidence()
    {
        var unsafeSnapshot = MiPlaySetPlaySourceOfficialJsonOneFrameLiveValidationEvidence.CreateCurrentSnapshot() with
        {
            CmdOpenSent = true,
        };

        var decision = MiPlaySetPlaySourceOfficialJsonOneFrameLiveValidationEvidence.EvaluateResult(unsafeSnapshot);

        Assert.False(decision.OfficialJsonSetPlaySourceAccepted);
        Assert.Contains("boundary was exceeded", decision.Reason, StringComparison.Ordinal);
    }
}
