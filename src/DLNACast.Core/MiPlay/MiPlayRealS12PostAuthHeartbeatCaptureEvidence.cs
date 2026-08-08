namespace DLNACast.Core.MiPlay;

public sealed record MiPlayRealS12PostAuthHeartbeatFrame(
    string Direction,
    ushort Command,
    ushort Sequence,
    int PayloadLength,
    MiPlayCapturedSafetyDataHeaderSummary SafetyDataHeader);

public sealed record MiPlayRealS12PostAuthHeartbeatCaptureSnapshot(
    string ArtifactPath,
    string PhoneEndpoint,
    string SpeakerEndpoint,
    IReadOnlyList<MiPlayRealS12PostAuthHeartbeatFrame> Frames,
    bool CapturedWithRootTcpdump,
    bool SentNoProbeFrames);

/// <summary>
/// Root tcpdump evidence captured from the real phone talking to the real LX06
/// S12 at 192.168.10.7:8899. It records post-auth heartbeat traffic only; no
/// generated Probe frame was sent during this capture.
/// </summary>
public static class MiPlayRealS12PostAuthHeartbeatCaptureEvidence
{
    public const string ArtifactPath = "artifacts/phone_live/2210132C_OS3.0.313.0/root-captures/miplay-root-8899-scriptcheck-20260726-120328.pcap";
    public const string PhoneEndpoint = "192.168.10.20:44754";
    public const string SpeakerEndpoint = "192.168.10.7:8899";
    public const string TriggeredWindowArtifactPath = "artifacts/phone_live/2210132C_OS3.0.313.0/root-captures/miplay-root-8899-triggered-20260726-121154.pcap";
    public const string TriggeredWindowPhoneEndpoint = "192.168.10.20:43720";
    public const string TriggeredWindowSpeakerEndpoint = "192.168.10.7:8899";

    public static IReadOnlyList<string> FrameBase64 =>
        [
            "JAAaADIAAAAZAAcB4BCHNPElvtZguzawrjQK2RvqVq/x9A==",
            "JAAbADIAAAAZAAcB4BBOzFkj6/nu1nS/3D/xUTpO0QTWbg==",
            "JAAaADMAAAAZAAcB4BDU/EUiFRi/0vYq5vuXkCNlPt7vBA==",
            "JAAbADMAAAAZAAcB4BDwxVIMCTz7ZopapFcgy+fh784vuA==",
        ];

    public static IReadOnlyList<string> TriggeredWindowFrameBase64 =>
        [
            "JAAaAGUAAAAZAAcB4BBwQ6iZHioVY2iUFcNtYTdrxCzYaA==",
            "JAAbAGUAAAAZAAcB4BAFIW3QNPb82O19XSBp0g7mNLRoBA==",
            "JAAaAGYAAAAZAAcB4BBvd9rCInGhUlDy4WZAli3CPOtO7Q==",
            "JAAbAGYAAAAZAAcB4BBNK6vC6oGzHi8PgGRip1enpQ4sfw==",
            "JAAaAGcAAAAZAAcB4BDMy41IQ0azTT/RADk3XoCFkrbQow==",
            "JAAbAGcAAAAZAAcB4BBRfmVBIv9UIhpqDd5lV+nUBBBHaA==",
            "JAAaAGgAAAAZAAcB4BCdp5BYyMtPFpGto2EMz/3UQbelGw==",
            "JAAbAGgAAAAZAAcB4BAEHt8kYbyB99fgV9AwFD7VUAcdGA==",
            "JAAaAGkAAAAZAAcB4BAwT8+pJSxzTV7TliWNFP++iiZYnA==",
            "JAAbAGkAAAAZAAcB4BAzMcdg/+j3bOSuh4otQKbDHfWpQg==",
            "JAAaAGoAAAAZAAcB4BBwnVbAxp3PNqolw4a1glsIhb2Xyg==",
            "JAAbAGoAAAAZAAcB4BBGtpozKB5xS3FEHTSqRZn0/JwCOg==",
            "JAAaAGsAAAAZAAcB4BAOslRruRMwBT+Bj4Qy4fa4HJ7HAg==",
            "JAAbAGsAAAAZAAcB4BBKga4IueB4rG/I4VTYit6dIPglVA==",
            "JAAaAGwAAAAZAAcB4BAit+xEV6HZw1VJFGo5p6j3JSc33A==",
            "JAAbAGwAAAAZAAcB4BCXBFwv7tizsY4d5o/AdQOY9yI0CA==",
        ];

    public static MiPlayRealS12PostAuthHeartbeatCaptureSnapshot CreateCurrentSnapshot() =>
        CreateSnapshot(ArtifactPath, PhoneEndpoint, SpeakerEndpoint, FrameBase64);

    public static MiPlayRealS12PostAuthHeartbeatCaptureSnapshot CreateTriggeredWindowSnapshot() =>
        CreateSnapshot(TriggeredWindowArtifactPath, TriggeredWindowPhoneEndpoint, TriggeredWindowSpeakerEndpoint, TriggeredWindowFrameBase64);

    private static MiPlayRealS12PostAuthHeartbeatCaptureSnapshot CreateSnapshot(
        string artifactPath,
        string phoneEndpoint,
        string speakerEndpoint,
        IReadOnlyList<string> frameBase64)
    {
        var frames = new List<MiPlayRealS12PostAuthHeartbeatFrame>();

        for (var i = 0; i < frameBase64.Count; i++)
        {
            var data = Convert.FromBase64String(frameBase64[i]);
            if (!MiPlayCommandFrameCodec.TryDecode(data, out var frame, out var consumed) ||
                frame is null ||
                consumed != data.Length ||
                !MiPlaySafetyDataHeaderCodec.TryDecodeVersion1(frame.Payload, out var header) ||
                header is null)
            {
                throw new InvalidDataException($"Captured heartbeat frame {i} failed structural decode.");
            }

            frames.Add(new MiPlayRealS12PostAuthHeartbeatFrame(
                frame.Command == MiPlayProtocolConstants.HeartbeatCommand ? "phone-to-speaker" : "speaker-to-phone",
                frame.Command,
                frame.Sequence,
                frame.Payload.Length,
                new MiPlayCapturedSafetyDataHeaderSummary(
                    header.HeaderLength,
                    header.Flags,
                    header.PaddingLength,
                    header.IntegrityValue,
                    header.PayloadOffset,
                    header.PayloadLength,
                    header.IsEncrypted,
                    header.HasPaddingLengthField,
                    header.HasIntegrityValue)));
        }

        return new MiPlayRealS12PostAuthHeartbeatCaptureSnapshot(
            artifactPath,
            phoneEndpoint,
            speakerEndpoint,
            frames,
            CapturedWithRootTcpdump: true,
            SentNoProbeFrames: true);
    }

    public static MiPlayIdmStateDecision EvaluatePostAuthHeartbeatBoundary(
        MiPlayRealS12PostAuthHeartbeatCaptureSnapshot snapshot)
    {
        if (!snapshot.CapturedWithRootTcpdump || !snapshot.SentNoProbeFrames)
        {
            return new MiPlayIdmStateDecision(false, "The heartbeat sample must come from passive root tcpdump with no generated Probe frames.");
        }

        if (snapshot.Frames.Count < 2 || snapshot.Frames.Count % 2 != 0)
        {
            return new MiPlayIdmStateDecision(false, "The captured frames do not form the expected 0x001a/0x001b heartbeat request/ack sequence.");
        }

        for (var i = 0; i < snapshot.Frames.Count; i += 2)
        {
            var request = snapshot.Frames[i];
            var acknowledgement = snapshot.Frames[i + 1];

            if (request.Direction != "phone-to-speaker" ||
                acknowledgement.Direction != "speaker-to-phone" ||
                request.Command != MiPlayProtocolConstants.HeartbeatCommand ||
                acknowledgement.Command != MiPlayProtocolConstants.HeartbeatAcknowledgementCommand ||
                request.Sequence != acknowledgement.Sequence ||
                (i > 0 && request.Sequence != snapshot.Frames[i - 2].Sequence + 1))
            {
                return new MiPlayIdmStateDecision(false, "The captured frames do not form the expected 0x001a/0x001b heartbeat request/ack sequence.");
            }
        }

        if (snapshot.Frames.Any(frame =>
                frame.PayloadLength != 25 ||
                frame.SafetyDataHeader.HeaderLength != 9 ||
                frame.SafetyDataHeader.Flags != 0xE0 ||
                frame.SafetyDataHeader.PaddingLength != 0x10 ||
                frame.SafetyDataHeader.PayloadLength != 16 ||
                !frame.SafetyDataHeader.IsEncrypted ||
                !frame.SafetyDataHeader.HasPaddingLengthField ||
                !frame.SafetyDataHeader.HasIntegrityValue))
        {
            return new MiPlayIdmStateDecision(false, "At least one heartbeat frame does not carry the captured SafetyData v1 9-byte header plus one encrypted AES block.");
        }

        return new MiPlayIdmStateDecision(
            true,
            $"Passive root tcpdump captured {snapshot.Frames.Count / 2} real post-auth 0x001a/0x001b heartbeat pair(s) between the phone and S12. The outer command and sequence are clear, while each 25-byte payload is a SafetyData v1 container with flags 0xe0, padding 0x10, CRC, and one encrypted AES block.");
    }
}
