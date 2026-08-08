using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayRealPhonePostAuthCommandSequenceEvidenceTests
{
    [Fact]
    public void SnapshotCapturesRootTcpdumpArtifactAndExistingSessionBoundary()
    {
        var snapshot = MiPlayRealPhonePostAuthCommandSequenceEvidence.CreateCurrentSnapshot();

        Assert.Equal(
            "artifacts/phone_live/2210132C_OS3.0.313.0/root-captures/miplay-root-8899-reconnect-20260726-122421.pcap",
            snapshot.ArtifactPath);
        Assert.Equal("192.168.10.20:43720", snapshot.PhoneEndpoint);
        Assert.Equal("192.168.10.7:8899", snapshot.SpeakerEndpoint);
        Assert.True(snapshot.CapturedWithRootTcpdump);
        Assert.True(snapshot.SentNoProbeFrames);
        Assert.False(snapshot.ContainsTcpBootstrap);
        Assert.Equal(43, snapshot.Frames.Count);
    }

    [Fact]
    public void OfficialPhoneSendsLocalDeviceInfoAndGetDeviceInfoBeforeLaterSetPlaySource()
    {
        var frames = MiPlayRealPhonePostAuthCommandSequenceEvidence.CreateCurrentSnapshot().Frames;

        Assert.Equal(MiPlayProtocolConstants.SetLocalDeviceInfoCommand, frames[0].Command);
        Assert.Equal((ushort)0x013a, frames[0].Sequence);
        Assert.Equal(105, frames[0].PayloadLength);
        Assert.Equal(0xdb25f5f0u, frames[0].SafetyDataHeader.IntegrityValue);

        Assert.Equal(MiPlayProtocolConstants.GetDeviceInfoCommand, frames[1].Command);
        Assert.Equal((ushort)0x013b, frames[1].Sequence);
        Assert.Equal(25, frames[1].PayloadLength);

        Assert.Equal(MiPlayProtocolConstants.SetLocalDeviceInfoAcknowledgementCommand, frames[2].Command);
        Assert.Equal((ushort)0x013a, frames[2].Sequence);

        Assert.Equal(MiPlayProtocolConstants.GetDeviceInfoAcknowledgementCommand, frames[5].Command);
        Assert.Equal((ushort)0x013b, frames[5].Sequence);
        Assert.Equal(425, frames[5].PayloadLength);
        Assert.Equal(0x205be7f0u, frames[5].SafetyDataHeader.IntegrityValue);

        var setPlaySourceFrame = Assert.Single(frames, frame => frame.Command == MiPlayProtocolConstants.SetPlaySourceCommand);
        Assert.Equal((ushort)0x0144, setPlaySourceFrame.Sequence);
        Assert.Equal(105, setPlaySourceFrame.PayloadLength);
        Assert.True(frames.ToList().IndexOf(setPlaySourceFrame) > 5);
    }

    [Fact]
    public void CapturedSequenceContainsGetMirrorModePairAndNo0041WhileHeartbeatContinues()
    {
        var frames = MiPlayRealPhonePostAuthCommandSequenceEvidence.CreateCurrentSnapshot().Frames;

        var getMirrorMode = Assert.Single(frames, frame => frame.Command == MiPlayProtocolConstants.GetMirrorModeCommand);
        var getMirrorModeAcknowledgement = Assert.Single(frames, frame => frame.Command == MiPlayProtocolConstants.GetMirrorModeAcknowledgementCommand);
        Assert.Equal((ushort)0x013e, getMirrorMode.Sequence);
        Assert.Equal(getMirrorMode.Sequence, getMirrorModeAcknowledgement.Sequence);
        Assert.Contains("GetMirrorMode", getMirrorMode.Meaning, StringComparison.Ordinal);
        Assert.Contains("GetMirrorMode_Ack", getMirrorModeAcknowledgement.Meaning, StringComparison.Ordinal);

        Assert.DoesNotContain(frames, frame => frame.Command == MiPlayProtocolConstants.SetPlaySourceAcknowledgementCommand);

        var setPlaySourceIndex = frames.ToList().FindIndex(frame => frame.Command == MiPlayProtocolConstants.SetPlaySourceCommand);
        var heartbeatAfterSetPlaySource = frames.Skip(setPlaySourceIndex + 1)
            .Where(frame => frame.Command is MiPlayProtocolConstants.HeartbeatCommand or MiPlayProtocolConstants.HeartbeatAcknowledgementCommand)
            .ToList();

        Assert.Equal(22, heartbeatAfterSetPlaySource.Count);
        Assert.Contains(heartbeatAfterSetPlaySource, frame => frame.Command == MiPlayProtocolConstants.HeartbeatAcknowledgementCommand);
    }

    [Fact]
    public void AllFramesRemainSafetyDataVersion1Containers()
    {
        var frames = MiPlayRealPhonePostAuthCommandSequenceEvidence.CreateCurrentSnapshot().Frames;

        Assert.All(
            frames,
            frame =>
            {
                Assert.Equal(9, frame.SafetyDataHeader.HeaderLength);
                Assert.Equal(0xE0, frame.SafetyDataHeader.Flags);
                Assert.Equal(9, frame.SafetyDataHeader.PayloadOffset);
                Assert.Equal(frame.PayloadLength - 9, frame.SafetyDataHeader.PayloadLength);
                Assert.True(frame.SafetyDataHeader.IsEncrypted);
                Assert.True(frame.SafetyDataHeader.HasPaddingLengthField);
                Assert.True(frame.SafetyDataHeader.HasIntegrityValue);
            });
    }

    [Fact]
    public void DecisionSeparatesObservedOfficialOrderFromSendableProbePolicy()
    {
        var decision = MiPlayRealPhonePostAuthCommandSequenceEvidence.EvaluatePostAuthSequence(
            MiPlayRealPhonePostAuthCommandSequenceEvidence.CreateCurrentSnapshot());

        Assert.True(decision.CanProceed);
        Assert.Contains("official phone command window", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("0x0058 local-device-info", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("0x001e getDeviceInfo", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("GetMirrorMode/GetMirrorMode_Ack", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("0x0040 SetPlaySource", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("starts at sequence 0x013a", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("does not identify the first command after DealSafetyDone", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void GeneratedProbeFramesWouldInvalidateEvidenceBoundary()
    {
        var snapshot = MiPlayRealPhonePostAuthCommandSequenceEvidence.CreateCurrentSnapshot() with
        {
            SentNoProbeFrames = false,
        };

        var decision = MiPlayRealPhonePostAuthCommandSequenceEvidence.EvaluatePostAuthSequence(snapshot);

        Assert.False(decision.CanProceed);
        Assert.Contains("passive root tcpdump", decision.Reason, StringComparison.Ordinal);
    }
}
