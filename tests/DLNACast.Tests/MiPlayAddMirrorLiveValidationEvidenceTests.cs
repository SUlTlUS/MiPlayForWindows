using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayAddMirrorLiveValidationEvidenceTests
{
    [Fact]
    public void ConstantsCaptureSingleAddMirrorOnlyS12Run()
    {
        Assert.Equal("192.168.10.4", MiPlayAddMirrorLiveValidationEvidence.DeviceAddress);
        Assert.Equal("192.168.10.9", MiPlayAddMirrorLiveValidationEvidence.LocalControlAddress);
        Assert.Equal(10_527, MiPlayAddMirrorLiveValidationEvidence.LocalControlPort);
        Assert.Equal(8_899, MiPlayAddMirrorLiveValidationEvidence.DeviceControlPort);
        Assert.Equal("2.1.5091615", MiPlayAddMirrorLiveValidationEvidence.ControlSessionVersionAcknowledgement);
        Assert.Equal((ushort)0x0004, MiPlayAddMirrorLiveValidationEvidence.AddMirrorSequence);
        Assert.Equal("192.168.10.9:7236&from:192.168.10.9&islocal:1", MiPlayAddMirrorLiveValidationEvidence.AddMirrorPayload);
        Assert.Equal(57, MiPlayAddMirrorLiveValidationEvidence.EncryptedAddMirrorPayloadLength);
        Assert.Equal(7, MiPlayAddMirrorLiveValidationEvidence.FollowUpFrameCountBeforeClose);
        Assert.Equal("peer-first:observed-s12-inbound-iv-type1", MiPlayAddMirrorLiveValidationEvidence.SelectedSafetyDataCandidate);
        Assert.True(MiPlayAddMirrorLiveValidationEvidence.RecoveredLocalAddMirrorPayloadShapeSent);
    }

    [Fact]
    public void NegativeResultKeepsOpenMediaAnd0058Gated()
    {
        var snapshot = MiPlayAddMirrorLiveValidationEvidence.CreateCurrentSnapshot();

        Assert.True(snapshot.MutualSafetyAuthCompleted);
        Assert.True(snapshot.SafetyDataWrappedAddMirrorSent);
        Assert.True(snapshot.AddMirrorPayloadMatchedRecoveredLocalShape);
        Assert.False(snapshot.AddMirrorAcknowledgementObserved);
        Assert.True(snapshot.DeviceClosedControlAfterAddMirror);
        Assert.False(snapshot.CmdOpenSent);
        Assert.False(snapshot.SetLocalDeviceInfo0058Sent);
        Assert.False(snapshot.SetPlaySource0040Sent);
        Assert.False(snapshot.RtspListenerOrResponseUsed);
        Assert.False(snapshot.MediaOrRtpSent);
        Assert.False(snapshot.PlaybackCommandSent);

        var decision = MiPlayAddMirrorLiveValidationEvidence.EvaluateAddMirrorResult(snapshot);

        Assert.False(decision.AddMirrorAccepted);
        Assert.Contains("without a 0x002f acknowledgement", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("receive direction", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("master/slave role", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("sender-info session state", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void BoundaryViolationInvalidatesEvidence()
    {
        var unsafeSnapshot = MiPlayAddMirrorLiveValidationEvidence.CreateCurrentSnapshot() with
        {
            CmdOpenSent = true,
        };

        var decision = MiPlayAddMirrorLiveValidationEvidence.EvaluateAddMirrorResult(unsafeSnapshot);

        Assert.False(decision.AddMirrorAccepted);
        Assert.Contains("boundary was exceeded", decision.Reason, StringComparison.Ordinal);
    }
}