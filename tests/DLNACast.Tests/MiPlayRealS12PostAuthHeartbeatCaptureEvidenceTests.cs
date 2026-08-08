using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayRealS12PostAuthHeartbeatCaptureEvidenceTests
{
    [Fact]
    public void CapturedRootTcpdumpFramesDecodeAsPostAuthSafetyDataHeartbeats()
    {
        var snapshot = MiPlayRealS12PostAuthHeartbeatCaptureEvidence.CreateCurrentSnapshot();

        Assert.Equal(
            "artifacts/phone_live/2210132C_OS3.0.313.0/root-captures/miplay-root-8899-scriptcheck-20260726-120328.pcap",
            snapshot.ArtifactPath);
        Assert.Equal("192.168.10.20:44754", snapshot.PhoneEndpoint);
        Assert.Equal("192.168.10.7:8899", snapshot.SpeakerEndpoint);
        Assert.True(snapshot.CapturedWithRootTcpdump);
        Assert.True(snapshot.SentNoProbeFrames);

        Assert.Collection(
            snapshot.Frames,
            frame =>
            {
                Assert.Equal("phone-to-speaker", frame.Direction);
                Assert.Equal(MiPlayProtocolConstants.HeartbeatCommand, frame.Command);
                Assert.Equal((ushort)0x0032, frame.Sequence);
                AssertSafetyDataHeartbeatPayload(frame);
            },
            frame =>
            {
                Assert.Equal("speaker-to-phone", frame.Direction);
                Assert.Equal(MiPlayProtocolConstants.HeartbeatAcknowledgementCommand, frame.Command);
                Assert.Equal((ushort)0x0032, frame.Sequence);
                AssertSafetyDataHeartbeatPayload(frame);
            },
            frame =>
            {
                Assert.Equal("phone-to-speaker", frame.Direction);
                Assert.Equal(MiPlayProtocolConstants.HeartbeatCommand, frame.Command);
                Assert.Equal((ushort)0x0033, frame.Sequence);
                AssertSafetyDataHeartbeatPayload(frame);
            },
            frame =>
            {
                Assert.Equal("speaker-to-phone", frame.Direction);
                Assert.Equal(MiPlayProtocolConstants.HeartbeatAcknowledgementCommand, frame.Command);
                Assert.Equal((ushort)0x0033, frame.Sequence);
                AssertSafetyDataHeartbeatPayload(frame);
            });
    }

    [Fact]
    public void DecisionCapturesClearOuterCommandAndEncryptedSafetyDataPayloadBoundary()
    {
        var decision = MiPlayRealS12PostAuthHeartbeatCaptureEvidence.EvaluatePostAuthHeartbeatBoundary(
            MiPlayRealS12PostAuthHeartbeatCaptureEvidence.CreateCurrentSnapshot());

        Assert.True(decision.CanProceed);
        Assert.Contains("outer command and sequence are clear", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("SafetyData v1", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void TriggeredWindowCapturedOnlyExistingPostAuthHeartbeatPairs()
    {
        var snapshot = MiPlayRealS12PostAuthHeartbeatCaptureEvidence.CreateTriggeredWindowSnapshot();

        Assert.Equal(
            "artifacts/phone_live/2210132C_OS3.0.313.0/root-captures/miplay-root-8899-triggered-20260726-121154.pcap",
            snapshot.ArtifactPath);
        Assert.Equal("192.168.10.20:43720", snapshot.PhoneEndpoint);
        Assert.Equal("192.168.10.7:8899", snapshot.SpeakerEndpoint);
        Assert.Equal(16, snapshot.Frames.Count);
        Assert.Equal((ushort)0x0065, snapshot.Frames[0].Sequence);
        Assert.Equal((ushort)0x006c, snapshot.Frames[^1].Sequence);

        for (var i = 0; i < snapshot.Frames.Count; i += 2)
        {
            var expectedSequence = (ushort)(0x0065 + (i / 2));

            Assert.Equal("phone-to-speaker", snapshot.Frames[i].Direction);
            Assert.Equal(MiPlayProtocolConstants.HeartbeatCommand, snapshot.Frames[i].Command);
            Assert.Equal(expectedSequence, snapshot.Frames[i].Sequence);
            AssertSafetyDataHeartbeatPayload(snapshot.Frames[i]);

            Assert.Equal("speaker-to-phone", snapshot.Frames[i + 1].Direction);
            Assert.Equal(MiPlayProtocolConstants.HeartbeatAcknowledgementCommand, snapshot.Frames[i + 1].Command);
            Assert.Equal(expectedSequence, snapshot.Frames[i + 1].Sequence);
            AssertSafetyDataHeartbeatPayload(snapshot.Frames[i + 1]);
        }
    }

    [Fact]
    public void TriggeredWindowDecisionAcceptsLongerHeartbeatOnlyCapture()
    {
        var decision = MiPlayRealS12PostAuthHeartbeatCaptureEvidence.EvaluatePostAuthHeartbeatBoundary(
            MiPlayRealS12PostAuthHeartbeatCaptureEvidence.CreateTriggeredWindowSnapshot());

        Assert.True(decision.CanProceed);
        Assert.Contains("8 real post-auth", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("0x001a/0x001b", decision.Reason, StringComparison.Ordinal);
    }

    private static void AssertSafetyDataHeartbeatPayload(MiPlayRealS12PostAuthHeartbeatFrame frame)
    {
        Assert.Equal(25, frame.PayloadLength);
        Assert.Equal(9, frame.SafetyDataHeader.HeaderLength);
        Assert.Equal(0xE0, frame.SafetyDataHeader.Flags);
        Assert.Equal((byte)0x10, frame.SafetyDataHeader.PaddingLength);
        Assert.NotNull(frame.SafetyDataHeader.IntegrityValue);
        Assert.Equal(9, frame.SafetyDataHeader.PayloadOffset);
        Assert.Equal(16, frame.SafetyDataHeader.PayloadLength);
        Assert.True(frame.SafetyDataHeader.IsEncrypted);
        Assert.True(frame.SafetyDataHeader.HasPaddingLengthField);
        Assert.True(frame.SafetyDataHeader.HasIntegrityValue);
    }
}
