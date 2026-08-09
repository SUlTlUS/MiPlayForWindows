using System.Buffers.Binary;

namespace DLNACast.Core.MiPlay;

public enum MiPlayAudioSourceControlPhase
{
    Created = 0,
    AwaitingDeviceInfoAcknowledgement = 1,
    AwaitingMirrorModeAcknowledgement = 2,
    ControlPrefixComplete = 3,
    Stopped = 4,
}

public sealed record MiPlayAudioSourceControlTransition(
    bool Accepted,
    bool Completed,
    MiPlayAudioSourceControlPhase Phase,
    ushort ObservedCommand,
    ushort ObservedSequence,
    IReadOnlyList<MiPlayOfficialPostAuthSequenceStep> OutboundPlaintextSteps,
    bool SafeForNetworkUse,
    bool AllowsOpenAddMirrorRtspOrMedia,
    string Boundary);

/// <summary>
/// Pure source-side controller for the recovered phone-to-speaker command
/// prefix. It consumes already-decrypted command payloads and emits plaintext
/// steps only. It owns no socket, SafetyAuth state, SafetyData cipher, RTSP
/// listener, media encoder, playback command, or audio sender.
/// </summary>
public sealed class MiPlayAudioSourceControlSession
{
    public const bool SafeForNetworkUse = false;
    public const string Boundary =
        "offline Windows-source control prefix only; fresh SafetyData order and source identity are not accepted by S12 yet; stop after 0x0040 without Open, AddMirror, RTSP, playback, media, or audio";

    private readonly IReadOnlyList<MiPlayOfficialPostAuthSequenceStep> steps;
    private readonly HashSet<ushort> acknowledgedLocalDeviceInfoSequences = [];
    private MiPlayAudioSourceControlPhase phase = MiPlayAudioSourceControlPhase.Created;

    public MiPlayAudioSourceControlSession(
        IReadOnlyList<MiPlayOfficialPostAuthSequenceStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        ValidateStepOrder(steps);
        this.steps = [.. steps];
    }

    public MiPlayAudioSourceControlPhase Phase => phase;

    public static MiPlayAudioSourceControlSession CreateRecoveredCaptureComparisonSession(
        ushort firstCommandSequence) =>
        new(MiPlayOfficialPostAuthSequenceProbePlan.CreateSteps(firstCommandSequence));

    public MiPlayAudioSourceControlTransition CreateInitialOfflineBatch()
    {
        if (phase != MiPlayAudioSourceControlPhase.Created)
        {
            return Reject(0, 0, "The source control prefix was already started or stopped.");
        }

        var prefix = steps
            .TakeWhile(step => step.Kind != MiPlayOfficialPostAuthSequenceStepKind.SendGetMirrorMode)
            .ToArray();
        phase = MiPlayAudioSourceControlPhase.AwaitingDeviceInfoAcknowledgement;

        return Accept(
            observedCommand: 0,
            observedSequence: 0,
            prefix,
            completed: false,
            "Prepared the recovered source-name, getDeviceInfo, canAlonePlayCtrl, and alonePlayCapacity plaintext prefix. It remains offline and must wait for a parsed same-sequence 0x001f before producing 0x0034.");
    }

    public MiPlayAudioSourceControlTransition ProcessInboundPlaintext(
        ushort command,
        ushort sequence,
        ReadOnlySpan<byte> plaintext)
    {
        if (phase is MiPlayAudioSourceControlPhase.Created or
            MiPlayAudioSourceControlPhase.ControlPrefixComplete or
            MiPlayAudioSourceControlPhase.Stopped)
        {
            return Reject(command, sequence, "No inbound command is accepted in the current source control phase.");
        }

        if (command == MiPlayProtocolConstants.SetLocalDeviceInfoAcknowledgementCommand)
        {
            return ProcessLocalDeviceInfoAcknowledgement(sequence, plaintext);
        }

        if (phase == MiPlayAudioSourceControlPhase.AwaitingDeviceInfoAcknowledgement)
        {
            return ProcessDeviceInfoAcknowledgement(command, sequence, plaintext);
        }

        return ProcessMirrorModeAcknowledgement(command, sequence, plaintext);
    }

    private MiPlayAudioSourceControlTransition ProcessLocalDeviceInfoAcknowledgement(
        ushort sequence,
        ReadOnlySpan<byte> plaintext)
    {
        var validSequences = steps
            .Where(step => step.Command == MiPlayProtocolConstants.SetLocalDeviceInfoCommand)
            .Select(step => step.Sequence);
        if (!plaintext.IsEmpty ||
            !validSequences.Contains(sequence) ||
            !acknowledgedLocalDeviceInfoSequences.Add(sequence))
        {
            return Reject(
                MiPlayProtocolConstants.SetLocalDeviceInfoAcknowledgementCommand,
                sequence,
                "The interleaved 0x0059 must be an empty, same-sequence acknowledgement for one previously prepared 0x0058 and may occur only once.");
        }

        return Accept(
            MiPlayProtocolConstants.SetLocalDeviceInfoAcknowledgementCommand,
            sequence,
            [],
            completed: false,
            "Accepted one empty same-sequence 0x0059 while retaining the required 0x001f or 0x0035 gate; no outbound step is produced.");
    }

    private MiPlayAudioSourceControlTransition ProcessDeviceInfoAcknowledgement(
        ushort command,
        ushort sequence,
        ReadOnlySpan<byte> plaintext)
    {
        var request = Step(MiPlayOfficialPostAuthSequenceStepKind.SendGetDeviceInfo);
        if (command != MiPlayProtocolConstants.GetDeviceInfoAcknowledgementCommand ||
            sequence != request.Sequence ||
            plaintext.Length < MiPlayPostAuthProbePolicy.MinimumDeviceInfoAcknowledgementPayloadLength ||
            !MiPlayLegacyDeviceInfoPayloadCodec.TryDecode(
                plaintext,
                out var deviceInfo,
                out var bytesConsumed) ||
            deviceInfo is null ||
            bytesConsumed != plaintext.Length)
        {
            return Reject(
                command,
                sequence,
                "Expected a parseable, sufficient same-sequence 0x001f device-info acknowledgement before producing GetMirrorMode.");
        }

        phase = MiPlayAudioSourceControlPhase.AwaitingMirrorModeAcknowledgement;
        return Accept(
            command,
            sequence,
            [Step(MiPlayOfficialPostAuthSequenceStepKind.SendGetMirrorMode)],
            completed: false,
            "Parsed the same-sequence 0x001f and produced the empty 0x0034 GetMirrorMode plaintext step. It must wait for a same-sequence valueType=0, mirrorMode=2 0x0035.");
    }

    private MiPlayAudioSourceControlTransition ProcessMirrorModeAcknowledgement(
        ushort command,
        ushort sequence,
        ReadOnlySpan<byte> plaintext)
    {
        var request = Step(MiPlayOfficialPostAuthSequenceStepKind.SendGetMirrorMode);
        var validPayload =
            plaintext.Length == 5 &&
            plaintext[0] == 0 &&
            BinaryPrimitives.ReadUInt32BigEndian(plaintext[1..]) == 2;
        if (command != MiPlayProtocolConstants.GetMirrorModeAcknowledgementCommand ||
            sequence != request.Sequence ||
            !validPayload)
        {
            return Reject(
                command,
                sequence,
                "Expected a same-sequence 0x0035 payload with valueType=0 and mirrorMode=2 before producing SetPlaySource.");
        }

        phase = MiPlayAudioSourceControlPhase.ControlPrefixComplete;
        return Accept(
            command,
            sequence,
            [Step(MiPlayOfficialPostAuthSequenceStepKind.SendSetPlaySource)],
            completed: true,
            "Produced the recovered runtime 0x0040 plaintext step after the verified device-info and mirror-mode gates. The controller stops here and never permits Open, AddMirror, RTSP, playback, media, or audio.");
    }

    private MiPlayOfficialPostAuthSequenceStep Step(MiPlayOfficialPostAuthSequenceStepKind kind) =>
        steps.Single(step => step.Kind == kind);

    private MiPlayAudioSourceControlTransition Accept(
        ushort observedCommand,
        ushort observedSequence,
        IReadOnlyList<MiPlayOfficialPostAuthSequenceStep> outboundSteps,
        bool completed,
        string boundary) =>
        new(
            Accepted: true,
            Completed: completed,
            phase,
            observedCommand,
            observedSequence,
            outboundSteps,
            SafeForNetworkUse: false,
            AllowsOpenAddMirrorRtspOrMedia: false,
            boundary);

    private MiPlayAudioSourceControlTransition Reject(
        ushort observedCommand,
        ushort observedSequence,
        string boundary)
    {
        phase = MiPlayAudioSourceControlPhase.Stopped;
        return new MiPlayAudioSourceControlTransition(
            Accepted: false,
            Completed: false,
            phase,
            observedCommand,
            observedSequence,
            OutboundPlaintextSteps: [],
            SafeForNetworkUse: false,
            AllowsOpenAddMirrorRtspOrMedia: false,
            boundary);
    }

    private static void ValidateStepOrder(
        IReadOnlyList<MiPlayOfficialPostAuthSequenceStep> candidateSteps)
    {
        MiPlayOfficialPostAuthSequenceStepKind[] requiredOrder =
        [
            MiPlayOfficialPostAuthSequenceStepKind.SendSourceName,
            MiPlayOfficialPostAuthSequenceStepKind.SendGetDeviceInfo,
            MiPlayOfficialPostAuthSequenceStepKind.SendCanAlonePlayCtrl,
            MiPlayOfficialPostAuthSequenceStepKind.SendAlonePlayCapacity,
            MiPlayOfficialPostAuthSequenceStepKind.SendGetMirrorMode,
            MiPlayOfficialPostAuthSequenceStepKind.SendSetPlaySource,
        ];

        if (candidateSteps.Count != requiredOrder.Length ||
            !candidateSteps.Select(step => step.Kind).SequenceEqual(requiredOrder) ||
            candidateSteps.Zip(candidateSteps.Skip(1), (left, right) => right.Sequence == left.Sequence + 1).Any(valid => !valid))
        {
            throw new ArgumentException(
                "The MiPlay audio-source controller requires the exact six-step recovered command order with contiguous sequences.",
                nameof(candidateSteps));
        }
    }
}
