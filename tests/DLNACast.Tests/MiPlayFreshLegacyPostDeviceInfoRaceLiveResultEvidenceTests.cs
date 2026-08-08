using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayFreshLegacyPostDeviceInfoRaceLiveResultEvidenceTests
{
    [Fact]
    public void AuthorizedAutomaticDiscoveryRunProvesDeviceInfoAcceptanceAndInitial0058Race()
    {
        var snapshot = MiPlayFreshLegacyPostDeviceInfoRaceLiveResultEvidence.CreateCurrentSnapshot();
        var decision = MiPlayFreshLegacyPostDeviceInfoRaceLiveResultEvidence.Evaluate(snapshot);

        Assert.Equal("192.168.10.9", MiPlayFreshLegacyPostDeviceInfoRaceLiveResultEvidence.ReceiverAddress);
        Assert.Equal(8899, MiPlayFreshLegacyPostDeviceInfoRaceLiveResultEvidence.ReceiverPort);
        Assert.Equal("192.168.10.58", MiPlayFreshLegacyPostDeviceInfoRaceLiveResultEvidence.SourceAddress);
        Assert.Equal(50_730, MiPlayFreshLegacyPostDeviceInfoRaceLiveResultEvidence.SourcePort);
        Assert.EndsWith(
            "fresh-legacy-post-device-info-observation-20260807-125156.stdout.log",
            MiPlayFreshLegacyPostDeviceInfoRaceLiveResultEvidence.ArtifactPath,
            StringComparison.Ordinal);
        Assert.True(snapshot.ReceiverAppearedThroughAutomaticLanDiscovery);
        Assert.True(snapshot.ReceiverWasNotSelectedByUser);
        Assert.Equal(1, snapshot.OutboundLegacyChallengeCount);
        Assert.Equal(1, snapshot.OutboundGetDeviceInfoAcknowledgementCount);
        Assert.True(snapshot.NoOtherOutboundFrames);
        Assert.True(decision.ProvesDeviceInfoAccepted);
        Assert.True(decision.ProvesInitialSetLocalDeviceInfoCanRaceAfterDeviceInfoAcknowledgement);
        Assert.True(decision.ProvesGetMirrorModeReachedPhoneSide);
        Assert.False(decision.ProvesGetMirrorModeOnWire);
        Assert.True(decision.AppearanceConsistentWithDeliberateDisconnect);
        Assert.True(decision.RequiresFreshAuthorizationForRetry);
        Assert.Contains("Automatic LAN discovery", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("deliberately disconnected", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingPhoneCallbackOrExpandedOutboundCountInvalidatesAcceptance()
    {
        var snapshot = MiPlayFreshLegacyPostDeviceInfoRaceLiveResultEvidence.CreateCurrentSnapshot();

        var missingCallback = MiPlayFreshLegacyPostDeviceInfoRaceLiveResultEvidence.Evaluate(
            snapshot with { PhoneLogObservedGetMirrorMode = false });
        var extraOutbound = MiPlayFreshLegacyPostDeviceInfoRaceLiveResultEvidence.Evaluate(
            snapshot with { OutboundGetDeviceInfoAcknowledgementCount = 2 });

        Assert.False(missingCallback.ProvesDeviceInfoAccepted);
        Assert.False(missingCallback.ProvesGetMirrorModeReachedPhoneSide);
        Assert.False(extraOutbound.ProvesDeviceInfoAccepted);
        Assert.Contains("incomplete", missingCallback.Reason, StringComparison.Ordinal);
        Assert.Contains("incomplete", extraOutbound.Reason, StringComparison.Ordinal);
    }
}
