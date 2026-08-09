namespace DLNACast.Core.MiPlay;

public sealed record MiPlayRealPhonePostAuthCommandSequenceFrame(
    int Index,
    string Direction,
    ushort Command,
    ushort Sequence,
    int PayloadLength,
    MiPlayCapturedSafetyDataHeaderSummary SafetyDataHeader,
    string Meaning);

public sealed record MiPlayRealPhonePostAuthCommandSequenceSnapshot(
    string ArtifactPath,
    string PhoneEndpoint,
    string SpeakerEndpoint,
    bool CapturedWithRootTcpdump,
    bool SentNoProbeFrames,
    bool ContainsTcpBootstrap,
    IReadOnlyList<MiPlayRealPhonePostAuthCommandSequenceFrame> Frames);

/// <summary>
/// Passive root-tcpdump evidence from the official phone sender talking to the
/// real LX06 S12 over an already authenticated 8899 command session. It records
/// wire command order and SafetyData envelope metadata only; it does not decrypt,
/// replay, or authorize generated business/media frames.
/// </summary>
public static class MiPlayRealPhonePostAuthCommandSequenceEvidence
{
    public const string ArtifactPath =
        "artifacts/phone_live/2210132C_OS3.0.313.0/root-captures/miplay-root-8899-reconnect-20260726-122421.pcap";

    public const string PhoneEndpoint = "192.168.10.20:43720";
    public const string SpeakerEndpoint = "192.168.10.7:8899";

    public const ushort GetMirrorModeCommand = MiPlayProtocolConstants.GetMirrorModeCommand;
    public const ushort GetMirrorModeAcknowledgementCommand = MiPlayProtocolConstants.GetMirrorModeAcknowledgementCommand;

    public static MiPlayRealPhonePostAuthCommandSequenceSnapshot CreateCurrentSnapshot() =>
        new(
            ArtifactPath,
            PhoneEndpoint,
            SpeakerEndpoint,
            CapturedWithRootTcpdump: true,
            SentNoProbeFrames: true,
            ContainsTcpBootstrap: false,
            CreateFrames());

    public static MiPlayIdmStateDecision EvaluatePostAuthSequence(
        MiPlayRealPhonePostAuthCommandSequenceSnapshot snapshot)
    {
        if (!snapshot.CapturedWithRootTcpdump || !snapshot.SentNoProbeFrames)
        {
            return new MiPlayIdmStateDecision(false, "The sequence must come from passive root tcpdump with no generated Probe frames.");
        }

        if (snapshot.ContainsTcpBootstrap)
        {
            return new MiPlayIdmStateDecision(false, "This evidence intentionally models an existing authenticated session, not a complete bootstrap capture.");
        }

        if (snapshot.Frames.Count != 43 || snapshot.Frames.Any(frame => frame.SafetyDataHeader.HeaderLength != 9))
        {
            return new MiPlayIdmStateDecision(false, "The captured command sequence is incomplete or contains a non-SafetyData payload.");
        }

        var frames = snapshot.Frames;
        if (frames[0].Command != MiPlayProtocolConstants.SetLocalDeviceInfoCommand ||
            frames[0].Sequence != 0x013a ||
            frames[1].Command != MiPlayProtocolConstants.GetDeviceInfoCommand ||
            frames[1].Sequence != 0x013b ||
            frames[2].Command != MiPlayProtocolConstants.SetLocalDeviceInfoAcknowledgementCommand ||
            frames[2].Sequence != 0x013a ||
            frames[5].Command != MiPlayProtocolConstants.GetDeviceInfoAcknowledgementCommand ||
            frames[5].Sequence != 0x013b)
        {
            return new MiPlayIdmStateDecision(false, "The first order visible in this mid-session window is not 0x0058, 0x001e, 0x0059, then a matching 0x001f.");
        }

        var getMirrorMode = frames.SingleOrDefault(frame => frame.Command == GetMirrorModeCommand);
        var getMirrorModeAcknowledgement = frames.SingleOrDefault(frame => frame.Command == GetMirrorModeAcknowledgementCommand);
        if (getMirrorMode is null ||
            getMirrorModeAcknowledgement is null ||
            getMirrorMode.Sequence != getMirrorModeAcknowledgement.Sequence ||
            getMirrorMode.Sequence != 0x013e)
        {
            return new MiPlayIdmStateDecision(false, "The captured GetMirrorMode/GetMirrorMode_Ack post-auth pair is missing or not sequence-matched.");
        }

        var frameList = frames.ToList();
        var setPlaySourceIndex = frameList.FindIndex(frame => frame.Command == MiPlayProtocolConstants.SetPlaySourceCommand);
        var getMirrorModeAcknowledgementIndex = frameList.IndexOf(getMirrorModeAcknowledgement);
        if (setPlaySourceIndex <= getMirrorModeAcknowledgementIndex ||
            frames[setPlaySourceIndex].Sequence != 0x0144 ||
            frames[setPlaySourceIndex].PayloadLength != 105)
        {
            return new MiPlayIdmStateDecision(false, "The official 0x0040 SetPlaySource frame was not observed after the device-info and 0x0034/0x0035 readiness frames.");
        }

        if (frames.Any(frame => frame.Command == MiPlayProtocolConstants.SetPlaySourceAcknowledgementCommand))
        {
            return new MiPlayIdmStateDecision(false, "This capture unexpectedly contains a 0x0041 acknowledgement; it should be modeled separately.");
        }

        if (frames.Skip(setPlaySourceIndex + 1).Count(frame => frame.Command == MiPlayProtocolConstants.HeartbeatAcknowledgementCommand) < 1)
        {
            return new MiPlayIdmStateDecision(false, "The session did not continue with heartbeat acknowledgements after the official 0x0040 frame.");
        }

        return new MiPlayIdmStateDecision(
            true,
            "Passive root tcpdump captured an official phone command window on an existing authenticated S12 session: sequence starts at 0x013a with 0x0058 local-device-info, which precedes and interleaves with 0x001e getDeviceInfo; 0x001f returns a large SafetyData payload; 0x0034/0x0035 GetMirrorMode/GetMirrorMode_Ack follows; then 0x0040 SetPlaySource is sent later without any 0x0041 in this window while heartbeats continue. Because the pcap has no TCP/SafetyAuth bootstrap and starts at sequence 0x013a, it does not identify the first command after DealSafetyDone and cannot define a fresh-session Probe order.");
    }

    private static IReadOnlyList<MiPlayRealPhonePostAuthCommandSequenceFrame> CreateFrames()
    {
        (string Direction, ushort Command, ushort Sequence, int PayloadLength, byte Padding, uint Integrity, string Meaning)[] frames =
        [
            ("phone-to-speaker", MiPlayProtocolConstants.SetLocalDeviceInfoCommand, 0x013a, 105, 0x10, 0xdb25f5f0, "official local-device-info/update frame before getDeviceInfo acknowledgement"),
            ("phone-to-speaker", MiPlayProtocolConstants.GetDeviceInfoCommand, 0x013b, 25, 0x10, 0x480566fe, "official getDeviceInfo request"),
            ("speaker-to-phone", MiPlayProtocolConstants.SetLocalDeviceInfoAcknowledgementCommand, 0x013a, 25, 0x10, 0x8b20a260, "acknowledgement for first 0x0058"),
            ("phone-to-speaker", MiPlayProtocolConstants.SetLocalDeviceInfoCommand, 0x013c, 41, 0x08, 0xa1b0f403, "second local-device-info/update frame"),
            ("phone-to-speaker", MiPlayProtocolConstants.SetLocalDeviceInfoCommand, 0x013d, 41, 0x07, 0x2bba769c, "third local-device-info/update frame"),
            ("speaker-to-phone", MiPlayProtocolConstants.GetDeviceInfoAcknowledgementCommand, 0x013b, 425, 0x01, 0x205be7f0, "large device-info acknowledgement"),
            ("phone-to-speaker", GetMirrorModeCommand, 0x013e, 25, 0x10, 0x9f312d74, "GetMirrorMode readiness/status command observed before later SetPlaySource"),
            ("speaker-to-phone", MiPlayProtocolConstants.SetLocalDeviceInfoAcknowledgementCommand, 0x013c, 25, 0x10, 0x49d860fa, "acknowledgement for second 0x0058"),
            ("phone-to-speaker", MiPlayProtocolConstants.SetLocalDeviceInfoCommand, 0x013f, 41, 0x08, 0xe9043ca0, "additional local-device-info/update frame"),
            ("phone-to-speaker", MiPlayProtocolConstants.SetLocalDeviceInfoCommand, 0x0140, 41, 0x07, 0x856bf185, "additional local-device-info/update frame"),
            ("phone-to-speaker", MiPlayProtocolConstants.SetLocalDeviceInfoCommand, 0x0141, 41, 0x08, 0x93b7b56d, "additional local-device-info/update frame"),
            ("phone-to-speaker", MiPlayProtocolConstants.SetLocalDeviceInfoCommand, 0x0142, 41, 0x07, 0x26126cfb, "additional local-device-info/update frame"),
            ("speaker-to-phone", MiPlayProtocolConstants.SetLocalDeviceInfoAcknowledgementCommand, 0x013d, 25, 0x10, 0x752b2044, "acknowledgement for third 0x0058"),
            ("speaker-to-phone", GetMirrorModeAcknowledgementCommand, 0x013e, 25, 0x0b, 0xb2d1a840, "GetMirrorMode_Ack for 0x0034"),
            ("speaker-to-phone", MiPlayProtocolConstants.SetLocalDeviceInfoAcknowledgementCommand, 0x013f, 25, 0x10, 0x8080047b, "acknowledgement for 0x013f 0x0058"),
            ("speaker-to-phone", MiPlayProtocolConstants.SetLocalDeviceInfoAcknowledgementCommand, 0x0140, 25, 0x10, 0x115a6cf5, "acknowledgement for 0x0140 0x0058"),
            ("speaker-to-phone", MiPlayProtocolConstants.SetLocalDeviceInfoAcknowledgementCommand, 0x0141, 25, 0x10, 0x7612d6d1, "acknowledgement for 0x0141 0x0058"),
            ("speaker-to-phone", MiPlayProtocolConstants.SetLocalDeviceInfoAcknowledgementCommand, 0x0142, 25, 0x10, 0x53b13eb6, "acknowledgement for 0x0142 0x0058"),
            ("phone-to-speaker", MiPlayProtocolConstants.HeartbeatCommand, 0x0143, 25, 0x10, 0x44c848f6, "heartbeat"),
            ("speaker-to-phone", MiPlayProtocolConstants.HeartbeatAcknowledgementCommand, 0x0143, 25, 0x10, 0x6b92da3d, "heartbeat acknowledgement"),
            ("phone-to-speaker", MiPlayProtocolConstants.SetPlaySourceCommand, 0x0144, 105, 0x0b, 0x27c1e649, "official SetPlaySource sent after readiness/context frames"),
            ("phone-to-speaker", MiPlayProtocolConstants.HeartbeatCommand, 0x0145, 25, 0x10, 0x80730329, "heartbeat after SetPlaySource"),
            ("speaker-to-phone", MiPlayProtocolConstants.HeartbeatAcknowledgementCommand, 0x0145, 25, 0x10, 0x36eb07f9, "heartbeat acknowledgement after SetPlaySource"),
            ("phone-to-speaker", MiPlayProtocolConstants.HeartbeatCommand, 0x0146, 25, 0x10, 0x1ffc7edd, "heartbeat after SetPlaySource"),
            ("speaker-to-phone", MiPlayProtocolConstants.HeartbeatAcknowledgementCommand, 0x0146, 25, 0x10, 0x0dc98822, "heartbeat acknowledgement after SetPlaySource"),
            ("phone-to-speaker", MiPlayProtocolConstants.HeartbeatCommand, 0x0147, 25, 0x10, 0x09a4f25b, "heartbeat after SetPlaySource"),
            ("speaker-to-phone", MiPlayProtocolConstants.HeartbeatAcknowledgementCommand, 0x0147, 25, 0x10, 0x6427fcf2, "heartbeat acknowledgement after SetPlaySource"),
            ("phone-to-speaker", MiPlayProtocolConstants.HeartbeatCommand, 0x0148, 25, 0x10, 0xf2bd53f0, "heartbeat after SetPlaySource"),
            ("speaker-to-phone", MiPlayProtocolConstants.HeartbeatAcknowledgementCommand, 0x0148, 25, 0x10, 0xc1cf1364, "heartbeat acknowledgement after SetPlaySource"),
            ("phone-to-speaker", MiPlayProtocolConstants.HeartbeatCommand, 0x0149, 25, 0x10, 0xb939bdfc, "heartbeat after SetPlaySource"),
            ("speaker-to-phone", MiPlayProtocolConstants.HeartbeatAcknowledgementCommand, 0x0149, 25, 0x10, 0xd60420fc, "heartbeat acknowledgement after SetPlaySource"),
            ("phone-to-speaker", MiPlayProtocolConstants.HeartbeatCommand, 0x014a, 25, 0x10, 0x4af3494a, "heartbeat after SetPlaySource"),
            ("speaker-to-phone", MiPlayProtocolConstants.HeartbeatAcknowledgementCommand, 0x014a, 25, 0x10, 0xb93d0ada, "heartbeat acknowledgement after SetPlaySource"),
            ("phone-to-speaker", MiPlayProtocolConstants.HeartbeatCommand, 0x014b, 25, 0x10, 0x575ff785, "heartbeat after SetPlaySource"),
            ("speaker-to-phone", MiPlayProtocolConstants.HeartbeatAcknowledgementCommand, 0x014b, 25, 0x10, 0x5dd475b4, "heartbeat acknowledgement after SetPlaySource"),
            ("phone-to-speaker", MiPlayProtocolConstants.HeartbeatCommand, 0x014c, 25, 0x10, 0xa69e5010, "heartbeat after SetPlaySource"),
            ("speaker-to-phone", MiPlayProtocolConstants.HeartbeatAcknowledgementCommand, 0x014c, 25, 0x10, 0xc85786b6, "heartbeat acknowledgement after SetPlaySource"),
            ("phone-to-speaker", MiPlayProtocolConstants.HeartbeatCommand, 0x014d, 25, 0x10, 0xf7dc6fa2, "heartbeat after SetPlaySource"),
            ("speaker-to-phone", MiPlayProtocolConstants.HeartbeatAcknowledgementCommand, 0x014d, 25, 0x10, 0x21321ef7, "heartbeat acknowledgement after SetPlaySource"),
            ("phone-to-speaker", MiPlayProtocolConstants.HeartbeatCommand, 0x014e, 25, 0x10, 0xa4555763, "heartbeat after SetPlaySource"),
            ("speaker-to-phone", MiPlayProtocolConstants.HeartbeatAcknowledgementCommand, 0x014e, 25, 0x10, 0x2dfb2ed9, "heartbeat acknowledgement after SetPlaySource"),
            ("phone-to-speaker", MiPlayProtocolConstants.HeartbeatCommand, 0x014f, 25, 0x10, 0x344fadd4, "heartbeat after SetPlaySource"),
            ("speaker-to-phone", MiPlayProtocolConstants.HeartbeatAcknowledgementCommand, 0x014f, 25, 0x10, 0xb23819b4, "heartbeat acknowledgement after SetPlaySource"),
        ];

        return [.. frames
            .Select((frame, index) => new MiPlayRealPhonePostAuthCommandSequenceFrame(
                index,
                frame.Direction,
                frame.Command,
                frame.Sequence,
                frame.PayloadLength,
                new MiPlayCapturedSafetyDataHeaderSummary(
                    HeaderLength: 9,
                    Flags: 0xE0,
                    PaddingLength: frame.Padding,
                    IntegrityValue: frame.Integrity,
                    PayloadOffset: 9,
                    PayloadLength: frame.PayloadLength - 9,
                    IsEncrypted: true,
                    HasPaddingLengthField: true,
                    HasIntegrityValue: true),
                frame.Meaning))];
    }
}
