using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayNoMediaRtspLiveValidationEvidenceTests
{
    [Fact]
    public void CapturesNegativeNoMediaCmdOpenCallbackResultWithoutBoundaryExpansion()
    {
        var snapshot = MiPlayNoMediaRtspLiveValidationEvidence.CreateCurrentSnapshot();

        Assert.Equal("192.168.10.4", MiPlayNoMediaRtspLiveValidationEvidence.DeviceAddress);
        Assert.Equal("192.168.10.9", MiPlayNoMediaRtspLiveValidationEvidence.LocalControlAddress);
        Assert.Equal(1718, MiPlayNoMediaRtspLiveValidationEvidence.LocalControlPort);
        Assert.Equal(8899, MiPlayNoMediaRtspLiveValidationEvidence.DeviceControlPort);
        Assert.Equal("2.1.5091615", MiPlayNoMediaRtspLiveValidationEvidence.ControlSessionVersionAcknowledgement);
        Assert.Equal(7236, MiPlayNoMediaRtspLiveValidationEvidence.RtspListenPort);
        Assert.Equal((ushort)0x0004, MiPlayNoMediaRtspLiveValidationEvidence.CmdOpenSequence);
        Assert.Equal("wfd://192.168.10.9:7236?mirrorMode=1", MiPlayNoMediaRtspLiveValidationEvidence.CmdOpenPayload);
        Assert.Equal(57, MiPlayNoMediaRtspLiveValidationEvidence.EncryptedCmdOpenPayloadLength);
        Assert.Equal(7, MiPlayNoMediaRtspLiveValidationEvidence.FollowUpFrameCountBeforeClose);
        Assert.Equal("peer-first:observed-s12-inbound-iv-type1", MiPlayNoMediaRtspLiveValidationEvidence.SelectedSafetyDataCandidate);
        Assert.True(MiPlayNoMediaRtspLiveValidationEvidence.CmdOpenPayloadShapeStaticallyCompatibleWithMpas);
        Assert.Equal("pre-open source identity/device-info/add-mirror/session context", MiPlayNoMediaRtspLiveValidationEvidence.NextOfflineHypothesis);

        Assert.True(snapshot.MutualSafetyAuthCompleted);
        Assert.True(snapshot.CmdOpenSentAfterListenerStarted);
        Assert.True(snapshot.SafetyDataWrappedCmdOpen);
        Assert.True(snapshot.DeviceClosedControlAfterCmdOpen);
        Assert.False(snapshot.RtspCallbackObserved);
        Assert.False(snapshot.RtspResponseSent);
        Assert.False(snapshot.MediaOrRtpSent);
        Assert.False(snapshot.SetLocalDeviceInfo0058Sent);
        Assert.False(snapshot.PlaybackCommandSent);

        var decision = MiPlayNoMediaRtspLiveValidationEvidence.EvaluateBridgeResult(snapshot);

        Assert.False(decision.BridgeVerified);
        Assert.Contains("closed the 8899 control connection", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("without a callback", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("does not verify", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("URL query ordering is ruled out", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("pre-open source identity/device-info/add-mirror/session context", decision.Reason, StringComparison.Ordinal);
    }
}