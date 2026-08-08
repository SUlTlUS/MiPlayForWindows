using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayRealSenderCaptureEndpointEvidenceTests
{
    [Fact]
    public void SnapshotTreatsRealSenderEndpointAsCaptureSourceOnly()
    {
        var snapshot = MiPlayRealSenderCaptureEndpointEvidence.CreateCurrentSnapshot();

        Assert.Equal("192.168.10.20:42509", snapshot.RequestedSourceEndpoint);
        Assert.Equal("192.168.10.20", MiPlayRealSenderCaptureEndpointEvidence.SenderAddress);
        Assert.Equal(42509, MiPlayRealSenderCaptureEndpointEvidence.SenderPort);
        Assert.Equal("192.168.10.9", snapshot.AnalyzerHostAddressObservedByIpconfig);
        Assert.Equal("ip.addr == 192.168.10.20 && tcp.port == 42509", snapshot.CaptureFilter);
        Assert.Equal("ip.src == 192.168.10.20 && tcp.srcport == 42509", snapshot.SenderToReceiverCaptureFilter);
        Assert.Equal("ip.dst == 192.168.10.20 && tcp.dstport == 42509", snapshot.ReceiverToSenderCaptureFilter);
        Assert.True(snapshot.OfflineDecoderAvailable);
        Assert.True(snapshot.RequiresPcapOrTcpPayloadBytes);
        Assert.False(snapshot.SendsPackets);
        Assert.False(snapshot.ReplaysPackets);
    }

    [Fact]
    public void OfflineBoundaryRequiresActualCapturedBytesBeforeComparison()
    {
        var missingCaptureDecision = MiPlayRealSenderCaptureEndpointEvidence.EvaluateOfflineCaptureBoundary(
            MiPlayRealSenderCaptureEndpointEvidence.CreateCurrentSnapshot());

        Assert.False(missingCaptureDecision.CanProceed);
        Assert.Contains("provide pcap/pcapng or exported TCP payload hex", missingCaptureDecision.Reason, StringComparison.Ordinal);

        var availableCaptureDecision = MiPlayRealSenderCaptureEndpointEvidence.EvaluateOfflineCaptureBoundary(
            MiPlayRealSenderCaptureEndpointEvidence.CreateCurrentSnapshot(captureBytesAvailable: true));

        Assert.True(availableCaptureDecision.CanProceed);
        Assert.Contains("decoded offline", availableCaptureDecision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void LocalCaptureVisibilityCallsOutWifiUnicastCaveat()
    {
        var decision = MiPlayRealSenderCaptureEndpointEvidence.EvaluateLocalCaptureVisibility(
            MiPlayRealSenderCaptureEndpointEvidence.CreateCurrentSnapshot());

        Assert.False(decision.CanProceed);
        Assert.Contains("192.168.10.9", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("192.168.10.20:42509", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("may not see phone-to-speaker unicast", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ArtifactRequirementsStayOfflineBecauseCaptureCanContainSecrets()
    {
        var requirements = MiPlayRealSenderCaptureEndpointEvidence.CreateArtifactRequirements();

        Assert.Collection(
            requirements,
            item =>
            {
                Assert.Equal("pcapng-or-pcap", item.ArtifactKind);
                Assert.True(item.ContainsReplayableSecretOrMedia);
                Assert.Contains("offline-only", item.Reason, StringComparison.Ordinal);
            },
            item =>
            {
                Assert.Equal("tcp-payload-hex", item.ArtifactKind);
                Assert.True(item.ContainsReplayableSecretOrMedia);
                Assert.Contains("offline decoder", item.Reason, StringComparison.Ordinal);
            });
    }
}

