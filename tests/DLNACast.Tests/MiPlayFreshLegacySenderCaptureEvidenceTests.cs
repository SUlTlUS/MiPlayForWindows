using System.Security.Cryptography;
using System.Text;
using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayFreshLegacySenderCaptureEvidenceTests
{
    [Fact]
    public void GoldenFramesDecodeToFreshLegacyClearSequence()
    {
        var snapshot = MiPlayFreshLegacySenderCaptureEvidence.CreateCurrentSnapshot();

        Assert.Equal("192.168.10.58:50516", snapshot.PhoneEndpoint);
        Assert.Equal("192.168.10.9:8899", snapshot.CaptureEndpoint);
        Assert.Equal("com.milink.service", snapshot.SourcePackage);
        Assert.Equal("12.4.8.13", snapshot.SourcePackageVersion);
        Assert.Equal("1.0.1123012", snapshot.NativeSourceVersion);
        Assert.False(snapshot.AdvertisedSupportsLyra);
        Assert.True(snapshot.SentOnlyLegacyChallenge);
        Assert.False(snapshot.SafetyInfoObserved);
        Assert.False(snapshot.SafetyAuthObserved);
        Assert.False(snapshot.SafetyDataObserved);
        Assert.False(snapshot.ReceiverSentBusinessReply);
        Assert.True(snapshot.PhoneClosedConnection);
        Assert.False(snapshot.SafeForNetworkUse);

        Assert.Equal(
            [
                MiPlayProtocolConstants.NativeSourceVersionCommand,
                MiPlayProtocolConstants.LegacySafetyAcknowledgementCommand,
                MiPlayProtocolConstants.GetDeviceInfoCommand,
                MiPlayProtocolConstants.SetLocalDeviceInfoCommand,
                MiPlayProtocolConstants.HeartbeatCommand,
                MiPlayProtocolConstants.HeartbeatCommand,
                MiPlayProtocolConstants.HeartbeatCommand,
            ],
            snapshot.InboundFrames.Select(frame => frame.Command));

        Assert.Equal([0, 0, 1, 2, 3, 4, 5], snapshot.InboundFrames.Select(frame => (int)frame.Sequence));
        Assert.Equal([12, 40, 0, 31, 0, 0, 0], snapshot.InboundFrames.Select(frame => frame.PayloadLength));
    }

    [Fact]
    public void SetLocalDeviceInfoPayloadExplainsThirtyOneByteLength()
    {
        var snapshot = MiPlayFreshLegacySenderCaptureEvidence.CreateCurrentSnapshot();
        var evidence = Assert.Single(
            snapshot.InboundFrames,
            frame => frame.Command == MiPlayProtocolConstants.SetLocalDeviceInfoCommand);
        var bytes = Convert.FromBase64String(evidence.FrameBase64);

        Assert.True(MiPlayCommandFrameCodec.TryDecode(bytes, out var frame, out var bytesConsumed));
        Assert.NotNull(frame);
        Assert.Equal(bytes.Length, bytesConsumed);
        Assert.Equal("{\"sourceName\":\"MI PAD 4\\/Plus\"}", Encoding.UTF8.GetString(frame.Payload));
        Assert.Equal("MI PAD 4/Plus", snapshot.SourceName);
        Assert.Equal(31, Encoding.UTF8.GetByteCount(snapshot.SetLocalDeviceInfoJson));
        Assert.Equal(evidence.FrameSha256Hex, Convert.ToHexString(SHA256.HashData(bytes)));
    }

    [Fact]
    public void DecisionProvesCompatibilityBranchButAuthorizesNoReplies()
    {
        var decision = MiPlayFreshLegacySenderCaptureEvidence.EvaluateCaptureBoundary(
            MiPlayFreshLegacySenderCaptureEvidence.CreateCurrentSnapshot());

        Assert.True(decision.ProvesFreshLegacyClearBranch);
        Assert.True(decision.ProvesExactSetLocalDeviceInfoPayload);
        Assert.False(decision.AuthorizesReceiverReplies);
        Assert.Contains("disproves a universal SafetyAuth prerequisite", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("0x0037, 0x001f, 0x0059, and 0x001b", decision.NextOfflineTarget, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryReceiverReplyCandidateRemainsOfflineOnly()
    {
        var candidates = MiPlayFreshLegacySenderCaptureEvidence.CreateOfflineReplyCandidates();

        Assert.Equal(4, candidates.Count);
        Assert.Equal(
            [
                MiPlayProtocolConstants.NativeSourceVersionAcknowledgementCommand,
                MiPlayProtocolConstants.GetDeviceInfoAcknowledgementCommand,
                MiPlayProtocolConstants.SetLocalDeviceInfoAcknowledgementCommand,
                MiPlayProtocolConstants.HeartbeatAcknowledgementCommand,
            ],
            candidates.Select(candidate => candidate.CandidateResponseCommand));
        Assert.All(candidates, candidate =>
        {
            Assert.False(candidate.ExactPayloadProvenForFreshClearBranch);
            Assert.False(candidate.SafeForNetworkUse);
            Assert.NotEmpty(candidate.Evidence);
        });
    }
}
