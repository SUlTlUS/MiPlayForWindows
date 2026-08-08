using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayFreshLegacyPostDeviceInfoObservationLiveResultEvidenceTests
{
    [Fact]
    public void AuthorizedWindowWithoutTcpConnectionIsNotAProtocolResult()
    {
        var snapshot = MiPlayFreshLegacyPostDeviceInfoObservationLiveResultEvidence.CreateCurrentSnapshot();
        var decision = MiPlayFreshLegacyPostDeviceInfoObservationLiveResultEvidence.Evaluate(snapshot);

        Assert.Equal("192.168.10.9", MiPlayFreshLegacyPostDeviceInfoObservationLiveResultEvidence.ReceiverAddress);
        Assert.Equal(120, MiPlayFreshLegacyPostDeviceInfoObservationLiveResultEvidence.ObservationSeconds);
        Assert.EndsWith("fresh-legacy-post-device-info-observation-20260807-115514.stdout.log", MiPlayFreshLegacyPostDeviceInfoObservationLiveResultEvidence.ArtifactPath, StringComparison.Ordinal);
        Assert.False(snapshot.TcpSenderConnected);
        Assert.Equal(0, snapshot.OutboundLegacyChallengeCount);
        Assert.Equal(0, snapshot.OutboundGetDeviceInfoAcknowledgementCount);
        Assert.False(decision.ProducesProtocolResult);
        Assert.True(decision.RequiresFreshAuthorizationForRetry);
        Assert.True(decision.FollowupStateConsistentWithNoSenderTrigger);
        Assert.True(snapshot.FollowupDeviceAsleep);
        Assert.True(snapshot.FollowupDisplayOff);
        Assert.True(snapshot.FollowupKeyguardShowing);
        Assert.True(snapshot.FollowupMiPlayAudioServiceRunning);
        Assert.True(snapshot.FollowupRootShellVerified);
        Assert.Contains("not a protocol acceptance or rejection", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("consistent with no sender trigger", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AnyUnexpectedTcpOrOutboundAccountingInvalidatesCleanNoConnectionInterpretation()
    {
        var snapshot = MiPlayFreshLegacyPostDeviceInfoObservationLiveResultEvidence.CreateCurrentSnapshot();

        var connected = MiPlayFreshLegacyPostDeviceInfoObservationLiveResultEvidence.Evaluate(
            snapshot with { TcpSenderConnected = true });
        var sent = MiPlayFreshLegacyPostDeviceInfoObservationLiveResultEvidence.Evaluate(
            snapshot with { OutboundLegacyChallengeCount = 1 });

        Assert.False(connected.ProducesProtocolResult);
        Assert.Contains("incomplete", connected.Reason, StringComparison.Ordinal);
        Assert.False(sent.ProducesProtocolResult);
        Assert.Contains("incomplete", sent.Reason, StringComparison.Ordinal);

        var awake = MiPlayFreshLegacyPostDeviceInfoObservationLiveResultEvidence.Evaluate(
            snapshot with { FollowupDeviceAsleep = false });
        Assert.False(awake.FollowupStateConsistentWithNoSenderTrigger);
    }
}
