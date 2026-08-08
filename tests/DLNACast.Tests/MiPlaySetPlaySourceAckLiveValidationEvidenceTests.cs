using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlaySetPlaySourceAckLiveValidationEvidenceTests
{
    [Fact]
    public void ConstantsCaptureSingleEmptySetPlaySourceAckOnlyS12Run()
    {
        Assert.Equal("192.168.10.4", MiPlaySetPlaySourceAckLiveValidationEvidence.DeviceAddress);
        Assert.Equal("192.168.10.9", MiPlaySetPlaySourceAckLiveValidationEvidence.LocalControlAddress);
        Assert.Equal(4_828, MiPlaySetPlaySourceAckLiveValidationEvidence.LocalControlPort);
        Assert.Equal(8_899, MiPlaySetPlaySourceAckLiveValidationEvidence.DeviceControlPort);
        Assert.Equal("2.1.5091615", MiPlaySetPlaySourceAckLiveValidationEvidence.ControlSessionVersionAcknowledgement);
        Assert.Equal((ushort)0x0004, MiPlaySetPlaySourceAckLiveValidationEvidence.SetPlaySourceSequence);
        Assert.Equal(0, MiPlaySetPlaySourceAckLiveValidationEvidence.PlaintextPayloadLength);
        Assert.Equal(25, MiPlaySetPlaySourceAckLiveValidationEvidence.EncryptedSetPlaySourcePayloadLength);
        Assert.Equal(7, MiPlaySetPlaySourceAckLiveValidationEvidence.FollowUpFrameCountBeforeClose);
        Assert.Equal("peer-first:observed-s12-inbound-iv-type1", MiPlaySetPlaySourceAckLiveValidationEvidence.SelectedSafetyDataCandidate);
    }

    [Fact]
    public void SnapshotPreservesAckOnlyNoBusinessBoundary()
    {
        var snapshot = MiPlaySetPlaySourceAckLiveValidationEvidence.CreateCurrentSnapshot();

        Assert.True(snapshot.MutualSafetyAuthCompleted);
        Assert.True(snapshot.SafetyDataWrappedSetPlaySourceSent);
        Assert.True(snapshot.EmptyPlaintextPayloadSent);
        Assert.False(snapshot.SetPlaySourceAcknowledgementObserved);
        Assert.True(snapshot.DeviceClosedControlAfterSetPlaySource);
        Assert.False(snapshot.JsonSourceIdentitySent);
        Assert.False(snapshot.CmdOpenSent);
        Assert.False(snapshot.SetLocalDeviceInfo0058Sent);
        Assert.False(snapshot.AddMirrorSent);
        Assert.False(snapshot.RtspListenerOrResponseUsed);
        Assert.False(snapshot.MediaOrRtpSent);
        Assert.False(snapshot.PlaybackCommandSent);
    }

    [Fact]
    public void NegativeAckOnlyResultMovesNextHypothesisBelowServerDispatcher()
    {
        var decision = MiPlaySetPlaySourceAckLiveValidationEvidence.EvaluateAckResult(
            MiPlaySetPlaySourceAckLiveValidationEvidence.CreateCurrentSnapshot());

        Assert.False(decision.DispatcherAckVerified);
        Assert.Contains("without a 0x0041 acknowledgement", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("before payload-length or JSON parsing", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("below ServerApp::doMpasCommand", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("SafetyData/session routing", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("rather than missing source-identity JSON", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void BoundaryExceededInvalidatesAckOnlyEvidence()
    {
        var unsafeSnapshot = MiPlaySetPlaySourceAckLiveValidationEvidence.CreateCurrentSnapshot() with
        {
            JsonSourceIdentitySent = true,
        };

        var decision = MiPlaySetPlaySourceAckLiveValidationEvidence.EvaluateAckResult(unsafeSnapshot);

        Assert.False(decision.DispatcherAckVerified);
        Assert.Contains("boundary was exceeded", decision.Reason, StringComparison.Ordinal);
    }
}