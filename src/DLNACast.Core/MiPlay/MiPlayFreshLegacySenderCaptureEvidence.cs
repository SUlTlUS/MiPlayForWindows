using System.Security.Cryptography;
using System.Text;

namespace DLNACast.Core.MiPlay;

public sealed record MiPlayFreshLegacySenderFrameEvidence(
    int Index,
    ushort Command,
    ushort Sequence,
    int PayloadLength,
    string FrameSha256Hex,
    string FrameBase64,
    string Meaning);

public sealed record MiPlayFreshLegacySenderCaptureSnapshot(
    string ArtifactPath,
    string PhoneLogcatArtifactPath,
    string PhoneEndpoint,
    string CaptureEndpoint,
    string SourcePackage,
    string SourcePackageVersion,
    string NativeSourceVersion,
    string SetLocalDeviceInfoJson,
    string SourceName,
    bool AdvertisedSupportsLyra,
    bool SentOnlyLegacyChallenge,
    bool SafetyInfoObserved,
    bool SafetyAuthObserved,
    bool SafetyDataObserved,
    bool ReceiverSentBusinessReply,
    bool PhoneClosedConnection,
    bool SafeForNetworkUse,
    IReadOnlyList<MiPlayFreshLegacySenderFrameEvidence> InboundFrames);

public sealed record MiPlayFreshLegacySenderCaptureDecision(
    bool ProvesFreshLegacyClearBranch,
    bool ProvesExactSetLocalDeviceInfoPayload,
    bool AuthorizesReceiverReplies,
    string Reason,
    string NextOfflineTarget);

public sealed record MiPlayFreshLegacyReceiverReplyCandidate(
    ushort TriggerCommand,
    ushort CandidateResponseCommand,
    string Evidence,
    bool ExactPayloadProvenForFreshClearBranch,
    bool SafeForNetworkUse);

/// <summary>
/// Test-backed evidence from the explicitly authorized fresh-session capture on
/// 2026-08-07. The distinct receiver sent one legacy 0x0028 challenge and no
/// version, device-info, heartbeat, business, RTSP, media, playback, or audio
/// response. The official phone source volunteered every captured inbound frame.
/// </summary>
public static class MiPlayFreshLegacySenderCaptureEvidence
{
    public const string ArtifactPath =
        "artifacts/phone_live/fresh-legacy-captures/fresh-legacy-20260807-014741.stdout.log";

    public const string PhoneLogcatArtifactPath =
        "artifacts/phone_live/fresh-legacy-captures/fresh-legacy-20260807-014741.milink-logcat.txt";

    public const string PhoneEndpoint = "192.168.10.58:50516";
    public const string CaptureEndpoint = "192.168.10.9:8899";
    public const string SourcePackage = "com.milink.service";
    public const string SourcePackageVersion = "12.4.8.13";
    public const string NativeSourceVersion = "1.0.1123012";
    public const string SetLocalDeviceInfoJson = "{\"sourceName\":\"MI PAD 4\\/Plus\"}";
    public const string SourceName = "MI PAD 4/Plus";

    public static MiPlayFreshLegacySenderCaptureSnapshot CreateCurrentSnapshot()
    {
        var frames = new[]
        {
            DecodeFrame(
                0,
                "JAA2AAAAAAAMMS4wLjExMjMwMTIA",
                "558EBE495951AD7B8929C4E3AFE9D58926D8E963961374A12A3BB5EEBC1646B0",
                "native source version"),
            DecodeFrame(
                1,
                "JAApAAAAAAAoODg5YTVkNTI2NzE2ZTc2Y2FmZWMyN2YwZjFiNzY4ODczYTI3Y2UwZg==",
                "AF8BF73F0315FD5BE81E05980E8AEFC266CCD56521E451DD8BAC45BC03F5B517",
                "legacy challenge acknowledgement"),
            DecodeFrame(
                2,
                "JAAeAAEAAAAA",
                "203B2D81F6878C606F65693571D9EE10DDA64C08ADE9EDF29D649EB17E482B03",
                "clear getDeviceInfo"),
            DecodeFrame(
                3,
                "JABYAAIAAAAfeyJzb3VyY2VOYW1lIjoiTUkgUEFEIDRcL1BsdXMifQ==",
                "1DC0862AC9E8AE7E69D7EA1E71C62E508B74257AA73C127C868707E63E9CD113",
                "clear setLocalDeviceInfo sourceName"),
            DecodeFrame(
                4,
                "JAAaAAMAAAAA",
                "2FACCB98E2B34F7E7EB1086874B8592125DF0561BF928D960DC9FDA8B066594E",
                "clear heartbeat"),
            DecodeFrame(
                5,
                "JAAaAAQAAAAA",
                "79722E27F8439222D60815BC2B8ABC97E87570AE8A663CBD9C7C5A7A45035BD6",
                "clear heartbeat"),
            DecodeFrame(
                6,
                "JAAaAAUAAAAA",
                "413FA7738258FD71FA337D49746B4D36FF410332CDF7DF8DC1EA52C936EC171D",
                "clear heartbeat"),
        };

        ValidatePayloadSemantics(frames);

        return new MiPlayFreshLegacySenderCaptureSnapshot(
            ArtifactPath,
            PhoneLogcatArtifactPath,
            PhoneEndpoint,
            CaptureEndpoint,
            SourcePackage,
            SourcePackageVersion,
            NativeSourceVersion,
            SetLocalDeviceInfoJson,
            SourceName,
            AdvertisedSupportsLyra: false,
            SentOnlyLegacyChallenge: true,
            SafetyInfoObserved: false,
            SafetyAuthObserved: false,
            SafetyDataObserved: false,
            ReceiverSentBusinessReply: false,
            PhoneClosedConnection: true,
            SafeForNetworkUse: false,
            frames);
    }

    public static MiPlayFreshLegacySenderCaptureDecision EvaluateCaptureBoundary(
        MiPlayFreshLegacySenderCaptureSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var commands = snapshot.InboundFrames.Select(frame => frame.Command).ToArray();
        var clearBranch =
            snapshot.SentOnlyLegacyChallenge &&
            !snapshot.AdvertisedSupportsLyra &&
            !snapshot.SafetyInfoObserved &&
            !snapshot.SafetyAuthObserved &&
            !snapshot.SafetyDataObserved &&
            commands.Contains(MiPlayProtocolConstants.NativeSourceVersionCommand) &&
            commands.Contains(MiPlayProtocolConstants.LegacySafetyAcknowledgementCommand) &&
            commands.Contains(MiPlayProtocolConstants.GetDeviceInfoCommand) &&
            commands.Contains(MiPlayProtocolConstants.SetLocalDeviceInfoCommand) &&
            commands.Contains(MiPlayProtocolConstants.HeartbeatCommand);

        var exactSetLocalDeviceInfo = snapshot.InboundFrames.Any(frame =>
            frame.Command == MiPlayProtocolConstants.SetLocalDeviceInfoCommand &&
            frame.Sequence == 2 &&
            frame.PayloadLength == Encoding.UTF8.GetByteCount(SetLocalDeviceInfoJson));

        return new MiPlayFreshLegacySenderCaptureDecision(
            clearBranch,
            exactSetLocalDeviceInfo,
            AuthorizesReceiverReplies: false,
            clearBranch && exactSetLocalDeviceInfo
                ? "The official 12.4.8.13 source selected a fresh legacy-clear branch and volunteered 0x0036, 0x0029, empty 0x001e, exact 31-byte 0x0058 sourceName JSON, and clear heartbeats after the receiver sent only one 0x0028 challenge. No 0x1400/0x1401/0x1402/0x1403 or SafetyData appeared. This disproves a universal SafetyAuth prerequisite but does not prove receiver reply payloads."
                : "The captured frame set is incomplete for a fresh legacy-clear branch or the exact 0x0058 payload is no longer reproduced.",
            "Keep 0x0037, 0x001f, 0x0059, and 0x001b as offline-only receiver reply candidates until their fresh-clear payload and ordering are proven; do not infer Open, AddMirror, RTSP, media, playback, or audio behavior.");
    }

    public static IReadOnlyList<MiPlayFreshLegacyReceiverReplyCandidate> CreateOfflineReplyCandidates() =>
        [
            new(
                MiPlayProtocolConstants.NativeSourceVersionCommand,
                MiPlayProtocolConstants.NativeSourceVersionAcknowledgementCommand,
                "Real S12 sessions return 0x0037, but the fresh sender trace reached cmd_sessionsuccess, getDeviceInfo, and setLocalDeviceInfo without one; its receiver-specific version string is not an initial prerequisite.",
                ExactPayloadProvenForFreshClearBranch: false,
                SafeForNetworkUse: false),
            new(
                MiPlayProtocolConstants.GetDeviceInfoCommand,
                MiPlayProtocolConstants.GetDeviceInfoAcknowledgementCommand,
                "LX06 mpas contains the same-sequence 0x001e -> 0x001f handler, a prior legacy-clear S12 probe received 0x001f, and the project can now encode the proven 20-field string-map schema for the distinct receiver; those generated values remain unverified by this phone.",
                ExactPayloadProvenForFreshClearBranch: false,
                SafeForNetworkUse: false),
            new(
                MiPlayProtocolConstants.SetLocalDeviceInfoCommand,
                MiPlayProtocolConstants.SetLocalDeviceInfoAcknowledgementCommand,
                "Source native ACK routing and established S12 captures identify 0x0059, but its fresh-clear payload and timing were not captured here.",
                ExactPayloadProvenForFreshClearBranch: false,
                SafeForNetworkUse: false),
            new(
                MiPlayProtocolConstants.HeartbeatCommand,
                MiPlayProtocolConstants.HeartbeatAcknowledgementCommand,
                "Established S12 sessions identify 0x001b heartbeat acknowledgements, but the sender already emitted three clear heartbeats after 0x001e/0x0058 and this passive capture deliberately sent none; it is not needed for the first onDeviceInfo gate.",
                ExactPayloadProvenForFreshClearBranch: false,
                SafeForNetworkUse: false),
        ];

    private static MiPlayFreshLegacySenderFrameEvidence DecodeFrame(
        int index,
        string frameBase64,
        string expectedSha256Hex,
        string meaning)
    {
        var bytes = Convert.FromBase64String(frameBase64);
        var actualSha256 = Convert.ToHexString(SHA256.HashData(bytes));
        if (!string.Equals(actualSha256, expectedSha256Hex, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Captured frame {index} SHA-256 no longer matches its golden transcript.");
        }

        if (!MiPlayCommandFrameCodec.TryDecode(bytes, out var frame, out var bytesConsumed) ||
            frame is null ||
            bytesConsumed != bytes.Length)
        {
            throw new InvalidDataException($"Captured frame {index} is not one complete MiPlay command frame.");
        }

        return new MiPlayFreshLegacySenderFrameEvidence(
            index,
            frame.Command,
            frame.Sequence,
            frame.Payload.Length,
            expectedSha256Hex,
            frameBase64,
            meaning);
    }

    private static void ValidatePayloadSemantics(IReadOnlyList<MiPlayFreshLegacySenderFrameEvidence> frames)
    {
        var versionEvidence = frames.Single(frame =>
            frame.Command == MiPlayProtocolConstants.NativeSourceVersionCommand);
        var versionFrameBytes = Convert.FromBase64String(versionEvidence.FrameBase64);
        MiPlayCommandFrameCodec.TryDecode(versionFrameBytes, out var versionFrame, out _);
        var version = Encoding.ASCII.GetString(versionFrame!.Payload).TrimEnd('\0');
        if (!string.Equals(version, NativeSourceVersion, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Captured native source version no longer matches the golden transcript.");
        }

        var sourceNameEvidence = frames.Single(frame =>
            frame.Command == MiPlayProtocolConstants.SetLocalDeviceInfoCommand);
        var sourceNameFrameBytes = Convert.FromBase64String(sourceNameEvidence.FrameBase64);
        MiPlayCommandFrameCodec.TryDecode(sourceNameFrameBytes, out var sourceNameFrame, out _);
        var json = Encoding.UTF8.GetString(sourceNameFrame!.Payload);
        if (!string.Equals(json, SetLocalDeviceInfoJson, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Captured setLocalDeviceInfo JSON no longer matches the golden transcript.");
        }
    }
}
