namespace DLNACast.Core.MiPlay;

public sealed record MiPlayOfficialPostAuthSequenceDryRunStep(
    MiPlayOfficialPostAuthSequenceStepKind Kind,
    ushort Command,
    ushort Sequence,
    int PlaintextPayloadLength,
    int SafetyDataPayloadLength,
    int CommandFrameLength,
    bool AcknowledgementGate);

public sealed record MiPlayOfficialPostAuthSequenceDryRunSnapshot(
    ushort FirstCommandSequence,
    bool UsesRecoveredOfficialSourceIdentity,
    int RecoveredOfficialFirstPlaintextLength,
    int RecoveredOfficialFirstSafetyDataPayloadLength,
    int PreviousDefaultWindowsFirstSafetyDataPayloadLength,
    bool FirstFrameMatchesRecoveredPhonePcapLength,
    bool PreviousDefaultWindowsFirstFrameWasRejectedLive,
    bool SafeForNetworkUse,
    IReadOnlyList<MiPlayOfficialPostAuthSequenceDryRunStep> Steps);

public sealed record MiPlayOfficialPostAuthSequenceDryRunDecision(
    bool PreparedRecoveredOfficialFirstFrame,
    bool AuthorizesNetworkSend,
    string Reason);

/// <summary>
/// Offline-only byte-shape evidence for the recovered official post-auth
/// sequence. It computes SafetyData container lengths without opening a socket,
/// selecting keys, or sending any S12 frame.
/// </summary>
public static class MiPlayOfficialPostAuthSequenceDryRunEvidence
{
    public const int AesBlockLength = 16;
    public const int SafetyDataVersion1HeaderLength =
        MiPlayRealPhonePostAuthPlaintextEvidence.SafetyDataVersion1HeaderLength;

    public static MiPlayOfficialPostAuthSequenceDryRunSnapshot CreateCurrentSnapshot(
        ushort firstCommandSequence = MiPlayOfficialPostAuthSequenceLiveValidationEvidence.FirstPostAuthCommandSequence)
    {
        var steps = MiPlayOfficialPostAuthSequenceProbePlan.CreateSteps(firstCommandSequence);
        var dryRunSteps = steps
            .Select(step =>
            {
                var safetyDataLength = ComputeSafetyDataVersion1PayloadLength(step.PlaintextPayload.Length);
                return new MiPlayOfficialPostAuthSequenceDryRunStep(
                    step.Kind,
                    step.Command,
                    step.Sequence,
                    step.PlaintextPayload.Length,
                    safetyDataLength,
                    MiPlayProtocolConstants.CommandHeaderLength + safetyDataLength,
                    step.AcknowledgementRequiredBeforeSetPlaySource);
            })
            .ToList();

        var previousDefaultWindowsLength = ComputeSafetyDataVersion1PayloadLength(
            MiPlayOfficialPostAuthSequenceLiveValidationEvidence.FirstPostAuthPlaintextPayloadLength);
        var firstStep = steps[0];
        var firstDryRunStep = dryRunSteps[0];

        return new MiPlayOfficialPostAuthSequenceDryRunSnapshot(
            firstCommandSequence,
            UsesRecoveredOfficialSourceIdentity: firstStep.PlaintextPayload.SequenceEqual(
                MiPlayLocalDeviceInfoPayloadCodec.EncodeRecoveredOfficialSourceIdentity()),
            RecoveredOfficialFirstPlaintextLength: firstStep.PlaintextPayload.Length,
            RecoveredOfficialFirstSafetyDataPayloadLength: firstDryRunStep.SafetyDataPayloadLength,
            PreviousDefaultWindowsFirstSafetyDataPayloadLength: previousDefaultWindowsLength,
            FirstFrameMatchesRecoveredPhonePcapLength: firstDryRunStep.SafetyDataPayloadLength ==
                MiPlayRealPhonePostAuthPlaintextEvidence.FirstSetLocalDeviceInfoSafetyDataPayloadLength,
            PreviousDefaultWindowsFirstFrameWasRejectedLive: true,
            SafeForNetworkUse: false,
            Steps: dryRunSteps);
    }

    public static MiPlayOfficialPostAuthSequenceDryRunDecision Evaluate(
        MiPlayOfficialPostAuthSequenceDryRunSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!snapshot.UsesRecoveredOfficialSourceIdentity ||
            !snapshot.FirstFrameMatchesRecoveredPhonePcapLength ||
            snapshot.RecoveredOfficialFirstSafetyDataPayloadLength ==
            snapshot.PreviousDefaultWindowsFirstSafetyDataPayloadLength)
        {
            return new MiPlayOfficialPostAuthSequenceDryRunDecision(
                PreparedRecoveredOfficialFirstFrame: false,
                AuthorizesNetworkSend: false,
                Reason: "The dry-run does not yet distinguish the recovered official first 0x0058 source identity from the rejected default Windows 0x0058 frame.");
        }

        if (snapshot.SafeForNetworkUse)
        {
            return new MiPlayOfficialPostAuthSequenceDryRunDecision(
                PreparedRecoveredOfficialFirstFrame: false,
                AuthorizesNetworkSend: false,
                Reason: "A dry-run snapshot must never be marked network-safe.");
        }

        return new MiPlayOfficialPostAuthSequenceDryRunDecision(
            PreparedRecoveredOfficialFirstFrame: true,
            AuthorizesNetworkSend: false,
            Reason: "The offline official-sequence dry-run prepares the recovered first 0x0058 source identity as an 80-byte plaintext / 105-byte SafetyData container, distinct from the rejected 73-byte default Windows identity. The recovered-identity frame is also a live negative now, so this dry-run is only a regression guard and does not authorize an S12 send.");
    }

    public static int ComputeSafetyDataVersion1PayloadLength(int plaintextLength)
    {
        if (plaintextLength < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(plaintextLength), "Plaintext length must not be negative.");
        }

        var paddingLength = AesBlockLength - plaintextLength % AesBlockLength;
        return checked(SafetyDataVersion1HeaderLength + plaintextLength + paddingLength);
    }
}
