namespace DLNACast.Core.MiPlay;

public sealed record MiPlayRealPhoneSessionMappingSnapshot(
    string ArtifactPath,
    string PhoneEndpoint,
    string SpeakerEndpoint,
    string AndroidPackage,
    string AndroidProcess,
    int AndroidUid,
    int AndroidPid,
    string PcapMappedCommandSession,
    ushort PcapFirstHeartbeatSequence,
    ushort PcapLastHeartbeatSequence,
    ushort LogcatMappedHeartbeatSequence,
    string OtherObservedCommandSession,
    ushort OtherObservedHeartbeatSequence,
    bool CapturedWithRootTcpdump,
    bool SentNoProbeFrames);

/// <summary>
/// Read-only process/log/pcap correlation for the official phone sender. It maps
/// the rooted tcpdump 8899 flow to the Android package/process and the native
/// DID8899 command-session name visible in logcat. It does not decrypt or replay
/// any captured payload.
/// </summary>
public static class MiPlayRealPhoneSessionMappingEvidence
{
    public const string ArtifactPath =
        "artifacts/phone_live/2210132C_OS3.0.313.0/root-captures/miplay-root-8899-map-20260726-132653.pcap";

    public const string PhoneEndpoint = "192.168.10.20:43720";
    public const string SpeakerEndpoint = "192.168.10.7:8899";
    public const string AndroidPackage = "com.milink.service";
    public const string AndroidProcess = "com.milink.service:audio";
    public const int AndroidUid = 10168;
    public const int AndroidPid = 975;
    public const string PcapMappedCommandSession = "DID8899:CMD_1bc2";
    public const ushort PcapFirstHeartbeatSequence = 0x043a;
    public const ushort PcapLastHeartbeatSequence = 0x043c;
    public const ushort LogcatMappedHeartbeatSequence = 0x043e;
    public const string OtherObservedCommandSession = "DID8899:CMD_2599";
    public const ushort OtherObservedHeartbeatSequence = 0x044f;

    public static MiPlayRealPhoneSessionMappingSnapshot CreateCurrentSnapshot() =>
        new(
            ArtifactPath,
            PhoneEndpoint,
            SpeakerEndpoint,
            AndroidPackage,
            AndroidProcess,
            AndroidUid,
            AndroidPid,
            PcapMappedCommandSession,
            PcapFirstHeartbeatSequence,
            PcapLastHeartbeatSequence,
            LogcatMappedHeartbeatSequence,
            OtherObservedCommandSession,
            OtherObservedHeartbeatSequence,
            CapturedWithRootTcpdump: true,
            SentNoProbeFrames: true);

    public static MiPlayIdmStateDecision EvaluateMapping(MiPlayRealPhoneSessionMappingSnapshot snapshot)
    {
        if (!snapshot.CapturedWithRootTcpdump || !snapshot.SentNoProbeFrames)
        {
            return new MiPlayIdmStateDecision(false, "Session mapping evidence must come from passive root tcpdump and read-only process/log inspection.");
        }

        if (snapshot.AndroidPackage != AndroidPackage ||
            snapshot.AndroidProcess != AndroidProcess ||
            snapshot.AndroidUid != AndroidUid ||
            snapshot.AndroidPid != AndroidPid)
        {
            return new MiPlayIdmStateDecision(false, "The socket owner does not match the observed official MiPlay audio process.");
        }

        if (snapshot.PcapFirstHeartbeatSequence != 0x043a ||
            snapshot.PcapLastHeartbeatSequence != 0x043c ||
            snapshot.LogcatMappedHeartbeatSequence != 0x043e ||
            snapshot.PcapMappedCommandSession != PcapMappedCommandSession)
        {
            return new MiPlayIdmStateDecision(false, "The pcap heartbeat sequence does not align with the DID8899:CMD_1bc2 logcat sequence chain.");
        }

        return new MiPlayIdmStateDecision(
            true,
            "The official 192.168.10.20:43720 -> 192.168.10.7:8899 flow is owned by com.milink.service:audio (UID 10168/PID 975) and aligns with the DID8899:CMD_1bc2 command-session heartbeat sequence. A separate DID8899:CMD_2599 heartbeat chain was also observed from the same process, matching the presence of a second S12 8899 socket but not needed to identify the captured .10.7 flow.");
    }
}
