using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayRealPhoneSessionMappingEvidenceTests
{
    [Fact]
    public void SnapshotMapsCapturedS12FlowToMilinkAudioProcess()
    {
        var snapshot = MiPlayRealPhoneSessionMappingEvidence.CreateCurrentSnapshot();

        Assert.Equal(
            "artifacts/phone_live/2210132C_OS3.0.313.0/root-captures/miplay-root-8899-map-20260726-132653.pcap",
            snapshot.ArtifactPath);
        Assert.Equal("192.168.10.20:43720", snapshot.PhoneEndpoint);
        Assert.Equal("192.168.10.7:8899", snapshot.SpeakerEndpoint);
        Assert.Equal("com.milink.service", snapshot.AndroidPackage);
        Assert.Equal("com.milink.service:audio", snapshot.AndroidProcess);
        Assert.Equal(10168, snapshot.AndroidUid);
        Assert.Equal(975, snapshot.AndroidPid);
        Assert.True(snapshot.CapturedWithRootTcpdump);
        Assert.True(snapshot.SentNoProbeFrames);
    }

    [Fact]
    public void HeartbeatSequenceAlignsPcapFlowWithDid8899CommandSession()
    {
        var snapshot = MiPlayRealPhoneSessionMappingEvidence.CreateCurrentSnapshot();

        Assert.Equal("DID8899:CMD_1bc2", snapshot.PcapMappedCommandSession);
        Assert.Equal((ushort)0x043a, snapshot.PcapFirstHeartbeatSequence);
        Assert.Equal((ushort)0x043c, snapshot.PcapLastHeartbeatSequence);
        Assert.Equal((ushort)0x043e, snapshot.LogcatMappedHeartbeatSequence);
        Assert.Equal(2, snapshot.LogcatMappedHeartbeatSequence - snapshot.PcapLastHeartbeatSequence);

        Assert.Equal("DID8899:CMD_2599", snapshot.OtherObservedCommandSession);
        Assert.Equal((ushort)0x044f, snapshot.OtherObservedHeartbeatSequence);
    }

    [Fact]
    public void DecisionKeepsMappingAsReadOnlyEvidence()
    {
        var decision = MiPlayRealPhoneSessionMappingEvidence.EvaluateMapping(
            MiPlayRealPhoneSessionMappingEvidence.CreateCurrentSnapshot());

        Assert.True(decision.CanProceed);
        Assert.Contains("com.milink.service:audio", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("DID8899:CMD_1bc2", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("second S12 8899 socket", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ProbeFramesInvalidateMappingBoundary()
    {
        var snapshot = MiPlayRealPhoneSessionMappingEvidence.CreateCurrentSnapshot() with
        {
            SentNoProbeFrames = false,
        };

        var decision = MiPlayRealPhoneSessionMappingEvidence.EvaluateMapping(snapshot);

        Assert.False(decision.CanProceed);
        Assert.Contains("passive root tcpdump", decision.Reason, StringComparison.Ordinal);
    }
}
