namespace DLNACast.Core.MiPlay;

public sealed record MiPlayRealLegacySourceReceiverSessionEvidence(
    string Endpoint,
    ushort ChallengeSequence,
    int ChallengePayloadLength,
    IReadOnlyList<ushort> SourceBootstrapCommands,
    IReadOnlyList<ushort> SourceBootstrapSequences,
    IReadOnlyList<ushort> ReceiverBootstrapCommands,
    IReadOnlyList<ushort> ReceiverBootstrapSequences,
    bool ClearHeartbeatPairsObserved);

public sealed record MiPlayRealLegacySourceFreshSessionSnapshot(
    string ArtifactPath,
    string ArtifactSha256Hex,
    string SourcePackage,
    string SourcePackageVersion,
    string NativeSourceVersion,
    string SourceNamePayloadSha256Hex,
    string IsSameAccountPayloadSha256Hex,
    string MirrorModePayloadSha256Hex,
    IReadOnlyList<MiPlayRealLegacySourceReceiverSessionEvidence> ReceiverSessions,
    bool LegacyClearBasicBootstrapWireProven,
    bool ModernSafetyObserved,
    bool SetPlaySourceObserved,
    bool OpenObserved,
    bool AddMirrorObserved,
    bool RtspOrMediaObserved,
    bool SafeForNetworkUse);

public sealed record MiPlayRealLegacySourceFreshSessionDecision(
    bool MatchesTwoReceiverLegacyBootstrap,
    bool SupportsBoundedWindowsBootstrapValidation,
    bool AuthorizesNetworkSend,
    string Reason,
    string HardStopBoundary);

/// <summary>
/// Redacted, hash-pinned evidence from the 2026-08-07 passive strace of the
/// rooted Mi Pad source talking to two real TCP/8899 receivers. Permanent
/// receiver identifiers and raw 0x001f payloads are deliberately excluded.
/// </summary>
public static class MiPlayRealLegacySourceFreshSessionEvidence
{
    public const string ArtifactPath =
        "artifacts/phone_live/fresh-source-captures/mipad4-miplay-source-20260807-131152.strace";

    public const string ArtifactSha256Hex =
        "509F8C4AC8DFBFE2AFA63B085B8E59BD8B0AC4EBC61A52311805451A85B80CC4";

    public const string SourcePackage = "com.milink.service";
    public const string SourcePackageVersion = "12.4.8.13";
    public const string NativeSourceVersion = "1.0.1123012";
    public const string SourceNamePayloadSha256Hex =
        "07535040123CBDB2361724AD94789C9EEC6CE786F6244AB5B1108345E76BDF3E";
    public const string IsSameAccountPayloadSha256Hex =
        "70FFB9E27499ED81F281936BF3A0D1A2DA79E8BB3CE57261E17DB998D155EDB2";
    public const string MirrorModePayloadSha256Hex =
        "89EEFC18FA4B815BD1ADED2F24EB28885993AA00B6D0171BF5005F9D39AAEA10";

    private static readonly ushort[] SourceBootstrapCommands =
    [
        MiPlayProtocolConstants.NativeSourceVersionCommand,
        MiPlayProtocolConstants.LegacySafetyAcknowledgementCommand,
        MiPlayProtocolConstants.GetDeviceInfoCommand,
        MiPlayProtocolConstants.SetLocalDeviceInfoCommand,
        MiPlayProtocolConstants.SetLocalDeviceInfoCommand,
        MiPlayProtocolConstants.GetMirrorModeCommand,
    ];

    private static readonly ushort[] ReceiverBootstrapCommands =
    [
        MiPlayProtocolConstants.LegacySafetyChallengeCommand,
        MiPlayProtocolConstants.NativeSourceVersionAcknowledgementCommand,
        MiPlayProtocolConstants.GetDeviceInfoAcknowledgementCommand,
        MiPlayProtocolConstants.SetLocalDeviceInfoAcknowledgementCommand,
        MiPlayProtocolConstants.SetLocalDeviceInfoAcknowledgementCommand,
        MiPlayProtocolConstants.GetMirrorModeAcknowledgementCommand,
    ];

    public static MiPlayRealLegacySourceFreshSessionSnapshot CreateCurrentSnapshot() =>
        new(
            ArtifactPath,
            ArtifactSha256Hex,
            SourcePackage,
            SourcePackageVersion,
            NativeSourceVersion,
            SourceNamePayloadSha256Hex,
            IsSameAccountPayloadSha256Hex,
            MirrorModePayloadSha256Hex,
            [
                new(
                    "192.168.10.58:60912->192.168.10.3:8899",
                    0x00be,
                    16,
                    SourceBootstrapCommands,
                    [0, 0x00be, 1, 2, 3, 4],
                    ReceiverBootstrapCommands,
                    [0x00be, 0, 1, 2, 3, 4],
                    ClearHeartbeatPairsObserved: true),
                new(
                    "192.168.10.58:52488->192.168.10.4:8899",
                    0x0370,
                    17,
                    SourceBootstrapCommands,
                    [0, 0x0370, 1, 2, 3, 4],
                    ReceiverBootstrapCommands,
                    [0x0370, 0, 1, 2, 3, 4],
                    ClearHeartbeatPairsObserved: true),
            ],
            LegacyClearBasicBootstrapWireProven: true,
            ModernSafetyObserved: false,
            SetPlaySourceObserved: false,
            OpenObserved: false,
            AddMirrorObserved: false,
            RtspOrMediaObserved: false,
            SafeForNetworkUse: false);

    public static MiPlayRealLegacySourceFreshSessionDecision EvaluateDecodedCapture(
        MiPlayStraceNetworkCaptureDecodeResult decoded)
    {
        ArgumentNullException.ThrowIfNull(decoded);

        var snapshot = CreateCurrentSnapshot();
        var sessionsMatch = snapshot.ReceiverSessions.All(expected =>
        {
            var frames = decoded.Frames
                .Where(frame => string.Equals(frame.Endpoint.ToString(), expected.Endpoint, StringComparison.Ordinal))
                .ToArray();
            if (frames.Length == 0)
            {
                return false;
            }

            var challenge = frames.SingleOrDefault(frame =>
                frame.Direction == MiPlayStraceNetworkDirection.Inbound &&
                frame.Command == MiPlayProtocolConstants.LegacySafetyChallengeCommand);
            if (challenge is null ||
                challenge.Sequence != expected.ChallengeSequence ||
                challenge.PayloadLength != expected.ChallengePayloadLength)
            {
                return false;
            }

            var sourceBootstrap = frames
                .Where(frame => frame.Direction == MiPlayStraceNetworkDirection.Outbound)
                .Where(frame => frame.Command is
                    MiPlayProtocolConstants.NativeSourceVersionCommand or
                    MiPlayProtocolConstants.LegacySafetyAcknowledgementCommand or
                    MiPlayProtocolConstants.GetDeviceInfoCommand or
                    MiPlayProtocolConstants.SetLocalDeviceInfoCommand or
                    MiPlayProtocolConstants.GetMirrorModeCommand)
                .Take(SourceBootstrapCommands.Length)
                .ToArray();
            var receiverBootstrap = frames
                .Where(frame => frame.Direction == MiPlayStraceNetworkDirection.Inbound)
                .Where(frame => frame.Command is
                    MiPlayProtocolConstants.LegacySafetyChallengeCommand or
                    MiPlayProtocolConstants.NativeSourceVersionAcknowledgementCommand or
                    MiPlayProtocolConstants.GetDeviceInfoAcknowledgementCommand or
                    MiPlayProtocolConstants.SetLocalDeviceInfoAcknowledgementCommand or
                    MiPlayProtocolConstants.GetMirrorModeAcknowledgementCommand)
                .Take(ReceiverBootstrapCommands.Length)
                .ToArray();

            return sourceBootstrap.Select(frame => frame.Command).SequenceEqual(expected.SourceBootstrapCommands) &&
                   sourceBootstrap.Select(frame => frame.Sequence).SequenceEqual(expected.SourceBootstrapSequences) &&
                   receiverBootstrap.Select(frame => frame.Command).SequenceEqual(expected.ReceiverBootstrapCommands) &&
                   receiverBootstrap.Select(frame => frame.Sequence).SequenceEqual(expected.ReceiverBootstrapSequences) &&
                   sourceBootstrap.Any(frame =>
                       frame.Command == MiPlayProtocolConstants.SetLocalDeviceInfoCommand &&
                       frame.Sequence == 2 &&
                       frame.PayloadLength == 31 &&
                       frame.PayloadSha256Hex == SourceNamePayloadSha256Hex) &&
                   sourceBootstrap.Any(frame =>
                       frame.Command == MiPlayProtocolConstants.SetLocalDeviceInfoCommand &&
                       frame.Sequence == 3 &&
                       frame.PayloadLength == 19 &&
                       frame.PayloadSha256Hex == IsSameAccountPayloadSha256Hex) &&
                   receiverBootstrap.Any(frame =>
                       frame.Command == MiPlayProtocolConstants.GetMirrorModeAcknowledgementCommand &&
                       frame.Sequence == 4 &&
                       frame.PayloadLength == 5 &&
                       frame.PayloadSha256Hex == MirrorModePayloadSha256Hex) &&
                   HasHeartbeatPair(frames);
        });

        var commands = decoded.Frames.Select(frame => frame.Command).ToArray();
        var forbiddenObserved = commands.Any(command => command is
            MiPlayProtocolConstants.SafetyInfoCommand or
            MiPlayProtocolConstants.SafetyInfoAcknowledgementCommand or
            MiPlayProtocolConstants.SafetyAuthCommand or
            MiPlayProtocolConstants.SafetyAuthAcknowledgementCommand or
            MiPlayProtocolConstants.SetPlaySourceCommand or
            MiPlayProtocolConstants.OpenDeviceCommand or
            MiPlayProtocolConstants.AddMirrorCommand or
            MiPlayProtocolConstants.AddMirrorAcknowledgementCommand);
        var matches = decoded.Issues.Count == 0 && sessionsMatch && !forbiddenObserved;

        return new MiPlayRealLegacySourceFreshSessionDecision(
            matches,
            SupportsBoundedWindowsBootstrapValidation: matches,
            AuthorizesNetworkSend: false,
            matches
                ? "The passive strace independently reproduces the same legacy-clear source bootstrap against two receivers, including exact sourceName/isSameAccount hashes, mode 2, and heartbeat pairs. Modern SafetyAuth/SafetyData is not a prerequisite for this basic branch."
                : "The decoded capture no longer matches both redacted, hash-pinned legacy-clear sessions or contains a forbidden modern/business command.",
            "Stop after 0x0059 sequence 3 and 0x0035 sequence 4. The capture contains no 0x0040, Open, AddMirror, RTSP, playback, media, or audio proof.");
    }

    private static bool HasHeartbeatPair(IReadOnlyList<MiPlayStraceCommandFrameSummary> frames)
    {
        var outboundSequences = frames
            .Where(frame => frame.Direction == MiPlayStraceNetworkDirection.Outbound &&
                            frame.Command == MiPlayProtocolConstants.HeartbeatCommand)
            .Select(frame => frame.Sequence)
            .ToHashSet();
        return frames.Any(frame =>
            frame.Direction == MiPlayStraceNetworkDirection.Inbound &&
            frame.Command == MiPlayProtocolConstants.HeartbeatAcknowledgementCommand &&
            outboundSequences.Contains(frame.Sequence));
    }
}
