using System.Text;

namespace DLNACast.Core.MiPlay;

public sealed record MiPlayPassiveSenderBootstrapCaptureSnapshot(
    string ArtifactName,
    string PhoneEndpoint,
    string CaptureEndpoint,
    bool SentOnlyLegacyChallenge,
    ushort OutboundChallengeCommand,
    ushort OutboundChallengeSequence,
    string OutboundChallengeText,
    ushort NativeSourceVersionCommand,
    ushort NativeSourceVersionSequence,
    string NativeSourceVersion,
    ushort LegacyAcknowledgementCommand,
    ushort LegacyAcknowledgementSequence,
    bool LegacyAcknowledgementValid,
    ushort SafetyInfoCommand,
    ushort SafetyInfoSequence,
    MiPlaySafetyInfoOffer SafetyInfoOffer,
    bool PhoneClosedAfterNoSafetyInfoAcknowledgement);

/// <summary>
/// Golden evidence from the rooted-phone passive sender capture on 2026-07-26.
/// The capture profile advertised a distinct test receiver and sent only one
/// pre-auth legacy 0x0028 challenge. All listed inbound frames were volunteered
/// by the official phone sender.
/// </summary>
public static class MiPlayPassiveSenderBootstrapCaptureEvidence
{
    public const string ArtifactName = "passive-sender-20260726-111422.stdout.log";
    public const string PhoneEndpoint = "192.168.10.20:49432";
    public const string CaptureEndpoint = "192.168.10.9:8899";

    public const string NativeSourceVersionFrameBase64 = "JAA2AAAAAAAMMy4xLjYwMzA1MTYA";
    public const string LegacyAcknowledgementFrameBase64 = "JAApAAAAAAAoODg5YTVkNTI2NzE2ZTc2Y2FmZWMyN2YwZjFiNzY4ODczYTI3Y2UwZg==";
    public const string SafetyInfoOfferFrameBase64 = "JBQAAAEAAACBA2NtZB4AAAB4ewoJImFlc0l2VHlwZXMiOiAiMyIsCgkiYWVzS2V5VHlwZXMiOiAiMyIsCgkiYXV0aEFsZ29yaXRobVR5cGVzIjogIjciLAoJImF1dGhLZXlUeXBlcyI6ICIxIiwKCSJpbnRlZ3JpdHlUeXBlcyI6ICIxIiAKfSAK";

    public static MiPlayPassiveSenderBootstrapCaptureSnapshot CreateCurrentSnapshot()
    {
        var nativeVersionFrame = Convert.FromBase64String(NativeSourceVersionFrameBase64);
        var legacyAcknowledgementFrame = Convert.FromBase64String(LegacyAcknowledgementFrameBase64);
        var safetyInfoFrame = Convert.FromBase64String(SafetyInfoOfferFrameBase64);

        if (!MiPlayCommandFrameCodec.TryDecode(nativeVersionFrame, out var nativeVersion, out _) ||
            nativeVersion is null ||
            nativeVersion.Command != MiPlayProtocolConstants.NativeSourceVersionCommand)
        {
            throw new InvalidDataException("The captured native source version frame is not a valid 0x0036 frame.");
        }

        if (!MiPlayCommandFrameCodec.TryDecode(legacyAcknowledgementFrame, out var legacyAcknowledgement, out _) ||
            legacyAcknowledgement is null ||
            legacyAcknowledgement.Command != MiPlayProtocolConstants.LegacySafetyAcknowledgementCommand)
        {
            throw new InvalidDataException("The captured legacy acknowledgement frame is not a valid 0x0029 frame.");
        }

        if (!MiPlaySafetyCommandCodec.TryDecode(safetyInfoFrame, out var safetyInfoCommand, out _) ||
            safetyInfoCommand is null ||
            safetyInfoCommand.Command != MiPlayProtocolConstants.SafetyInfoCommand)
        {
            throw new InvalidDataException("The captured SafetyInfo frame is not a valid 0x1400 frame.");
        }

        var expectedAcknowledgement = MiPlayLegacySafetyChallengeCodec.CreateAcknowledgement(
            MiPlayPassiveSenderCaptureProfile.ChallengeSequence,
            Encoding.ASCII.GetBytes(MiPlayPassiveSenderCaptureProfile.ChallengeText));

        var sourceVersionPayload = nativeVersion.Payload.AsSpan();
        if (sourceVersionPayload.Length > 0 && sourceVersionPayload[^1] == 0)
        {
            sourceVersionPayload = sourceVersionPayload[..^1];
        }

        var offer = DecodeCapturedOffer(safetyInfoCommand.JsonPayload);

        return new MiPlayPassiveSenderBootstrapCaptureSnapshot(
            ArtifactName,
            PhoneEndpoint,
            CaptureEndpoint,
            SentOnlyLegacyChallenge: true,
            OutboundChallengeCommand: MiPlayProtocolConstants.LegacySafetyChallengeCommand,
            OutboundChallengeSequence: MiPlayPassiveSenderCaptureProfile.ChallengeSequence,
            OutboundChallengeText: MiPlayPassiveSenderCaptureProfile.ChallengeText,
            NativeSourceVersionCommand: nativeVersion.Command,
            NativeSourceVersionSequence: nativeVersion.Sequence,
            NativeSourceVersion: Encoding.ASCII.GetString(sourceVersionPayload),
            LegacyAcknowledgementCommand: legacyAcknowledgement.Command,
            LegacyAcknowledgementSequence: legacyAcknowledgement.Sequence,
            LegacyAcknowledgementValid: string.Equals(
                Encoding.ASCII.GetString(legacyAcknowledgement.Payload),
                expectedAcknowledgement.Response,
                StringComparison.Ordinal),
            SafetyInfoCommand: safetyInfoCommand.Command,
            SafetyInfoSequence: safetyInfoCommand.Sequence,
            SafetyInfoOffer: offer,
            PhoneClosedAfterNoSafetyInfoAcknowledgement: true);
    }

    public static MiPlayIdmStateDecision EvaluateCaptureBoundary(
        MiPlayPassiveSenderBootstrapCaptureSnapshot snapshot)
    {
        if (!snapshot.SentOnlyLegacyChallenge ||
            snapshot.OutboundChallengeCommand != MiPlayProtocolConstants.LegacySafetyChallengeCommand)
        {
            return new MiPlayIdmStateDecision(false, "The passive sender bootstrap capture is only valid if the test receiver sent exactly one legacy 0x0028 challenge.");
        }

        if (!snapshot.LegacyAcknowledgementValid)
        {
            return new MiPlayIdmStateDecision(false, "The phone sender 0x0029 response did not validate against the captured legacy challenge.");
        }

        if (snapshot.SafetyInfoOffer.AuthKeyTypes != 1 ||
            snapshot.SafetyInfoOffer.AuthAlgorithmTypes != 7 ||
            snapshot.SafetyInfoOffer.IntegrityTypes != 1 ||
            snapshot.SafetyInfoOffer.AesKeyTypes != 3 ||
            snapshot.SafetyInfoOffer.AesIvTypes != 3)
        {
            return new MiPlayIdmStateDecision(false, "The phone sender SafetyInfo offer does not match the captured 2026-07-26 rooted-phone bootstrap vector.");
        }

        return new MiPlayIdmStateDecision(
            true,
            "The official phone sender voluntarily connected to the distinct test receiver, sent native source version 3.1.6030516, returned a valid legacy 0x0029 response, then offered SafetyInfo 0x1400 with authKeyTypes=1, authAlgorithmTypes=7, integrityTypes=1, aesKeyTypes=3, aesIvTypes=3. The test receiver sent no 0x1401/0x1402/business/media frames.");
    }

    private static MiPlaySafetyInfoOffer DecodeCapturedOffer(ReadOnlySpan<byte> payload)
    {
        var json = Encoding.UTF8.GetString(payload);
        return new MiPlaySafetyInfoOffer(
            AuthKeyTypes: ReadRequiredUintString(json, "authKeyTypes"),
            AuthAlgorithmTypes: ReadRequiredUintString(json, "authAlgorithmTypes"),
            IntegrityTypes: ReadRequiredUintString(json, "integrityTypes"),
            AesKeyTypes: ReadRequiredUintString(json, "aesKeyTypes"),
            AesIvTypes: ReadRequiredUintString(json, "aesIvTypes"));
    }

    private static uint ReadRequiredUintString(string json, string fieldName)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty(fieldName, out var property) ||
            property.ValueKind != System.Text.Json.JsonValueKind.String ||
            !uint.TryParse(property.GetString(), out var value))
        {
            throw new FormatException($"Missing or invalid SafetyInfo field '{fieldName}'.");
        }

        return value;
    }
}
