using System.Text;
using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayRealLegacySourceFreshSessionEvidenceTests
{
    [Fact]
    public void SnapshotPinsTwoRedactedReceiverSessionsAndNoMediaBoundary()
    {
        var snapshot = MiPlayRealLegacySourceFreshSessionEvidence.CreateCurrentSnapshot();

        Assert.Equal(
            "509F8C4AC8DFBFE2AFA63B085B8E59BD8B0AC4EBC61A52311805451A85B80CC4",
            snapshot.ArtifactSha256Hex);
        Assert.Equal("com.milink.service", snapshot.SourcePackage);
        Assert.Equal("12.4.8.13", snapshot.SourcePackageVersion);
        Assert.Equal("1.0.1123012", snapshot.NativeSourceVersion);
        Assert.Equal(2, snapshot.ReceiverSessions.Count);
        Assert.Equal([16, 17], snapshot.ReceiverSessions.Select(session => session.ChallengePayloadLength));
        Assert.All(snapshot.ReceiverSessions, session => Assert.True(session.ClearHeartbeatPairsObserved));
        Assert.True(snapshot.LegacyClearBasicBootstrapWireProven);
        Assert.False(snapshot.ModernSafetyObserved);
        Assert.False(snapshot.SetPlaySourceObserved);
        Assert.False(snapshot.OpenObserved);
        Assert.False(snapshot.AddMirrorObserved);
        Assert.False(snapshot.RtspOrMediaObserved);
        Assert.False(snapshot.SafeForNetworkUse);
    }

    [Fact]
    public void DecoderEvaluationAcceptsDeterministicRedactedTwoReceiverTranscript()
    {
        var trace = string.Join(
            '\n',
            CreateSessionLines(
                threadId: 100,
                localPort: 60912,
                remoteAddress: "192.168.10.3",
                challengeSequence: 0x00be,
                challengeText: "1234567890123456")
            .Concat(CreateSessionLines(
                    threadId: 101,
                    localPort: 52488,
                    remoteAddress: "192.168.10.4",
                    challengeSequence: 0x0370,
                    challengeText: "12345678901234567")));
        var decoded = MiPlayStraceNetworkCaptureDecoder.Decode(trace);

        var decision = MiPlayRealLegacySourceFreshSessionEvidence.EvaluateDecodedCapture(decoded);

        Assert.Empty(decoded.Issues);
        Assert.True(decision.MatchesTwoReceiverLegacyBootstrap);
        Assert.True(decision.SupportsBoundedWindowsBootstrapValidation);
        Assert.False(decision.AuthorizesNetworkSend);
        Assert.Contains("Modern SafetyAuth/SafetyData is not a prerequisite", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("no 0x0040, Open, AddMirror, RTSP", decision.HardStopBoundary, StringComparison.Ordinal);
    }

    private static IEnumerable<string> CreateSessionLines(
        int threadId,
        int localPort,
        string remoteAddress,
        ushort challengeSequence,
        string challengeText)
    {
        var endpoint = $"192.168.10.58:{localPort}->{remoteAddress}:8899";
        var challengePayload = Encoding.ASCII.GetBytes(challengeText);
        var deviceInfo = MiPlayLegacyDeviceInfoPayloadCodec.Encode(
            [new KeyValuePair<string, string>("model", "LX06")]);
        var outboundFrames = new[]
        {
            MiPlayNativeVersionCodec.EncodeSourceVersion(0, MiPlayProtocolConstants.NativeSourceVersion12_4_8_13),
            MiPlayLegacySafetyChallengeCodec.EncodeAcknowledgement(
                MiPlayLegacySafetyChallengeCodec.CreateAcknowledgement(challengeSequence, challengePayload)),
            MiPlayCommandFrameCodec.Encode(MiPlayProtocolConstants.GetDeviceInfoCommand, 1, []),
            MiPlayCommandFrameCodec.Encode(
                MiPlayProtocolConstants.SetLocalDeviceInfoCommand,
                2,
                MiPlayLocalDeviceInfoPayloadCodec.EncodeLegacySourceNameOnly("MI PAD 4/Plus")),
            MiPlayCommandFrameCodec.Encode(
                MiPlayProtocolConstants.SetLocalDeviceInfoCommand,
                3,
                MiPlayLocalDeviceInfoPayloadCodec.EncodeIsSameAccount(0)),
            MiPlayCommandFrameCodec.Encode(MiPlayProtocolConstants.GetMirrorModeCommand, 4, []),
            MiPlayCommandFrameCodec.Encode(MiPlayProtocolConstants.HeartbeatCommand, 5, []),
        };
        var inboundFrames = new[]
        {
            MiPlayCommandFrameCodec.Encode(MiPlayProtocolConstants.LegacySafetyChallengeCommand, challengeSequence, challengePayload),
            MiPlayCommandFrameCodec.Encode(MiPlayProtocolConstants.NativeSourceVersionAcknowledgementCommand, 0, Encoding.ASCII.GetBytes("2.1.5091615\0")),
            MiPlayCommandFrameCodec.Encode(MiPlayProtocolConstants.GetDeviceInfoAcknowledgementCommand, 1, deviceInfo),
            MiPlayCommandFrameCodec.Encode(MiPlayProtocolConstants.SetLocalDeviceInfoAcknowledgementCommand, 2, []),
            MiPlayCommandFrameCodec.Encode(MiPlayProtocolConstants.SetLocalDeviceInfoAcknowledgementCommand, 3, []),
            MiPlayCommandFrameCodec.Encode(MiPlayProtocolConstants.GetMirrorModeAcknowledgementCommand, 4, [0, 0, 0, 0, 2]),
            MiPlayCommandFrameCodec.Encode(MiPlayProtocolConstants.HeartbeatAcknowledgementCommand, 5, []),
        };

        var lines = new List<string>();
        for (var index = 0; index < inboundFrames.Length; index++)
        {
            lines.Add(TraceLine(threadId, index * 2, "recvfrom", endpoint, inboundFrames[index]));
            lines.Add(TraceLine(threadId, (index * 2) + 1, "sendto", endpoint, outboundFrames[index]));
        }

        return lines;
    }

    private static string TraceLine(
        int threadId,
        int microseconds,
        string call,
        string endpoint,
        byte[] frame) =>
        $"{threadId}  13:13:25.{microseconds:000000} {call}(95<TCP:[{endpoint}]>, \"{Escape(frame)}\", {frame.Length}, 0, NULL, 0) = {frame.Length}";

    private static string Escape(IEnumerable<byte> bytes) =>
        string.Concat(bytes.Select(value => $"\\x{value:x2}"));
}
