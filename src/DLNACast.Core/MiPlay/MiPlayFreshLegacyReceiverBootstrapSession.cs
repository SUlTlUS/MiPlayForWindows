using System.Security.Cryptography;
using System.Text;

namespace DLNACast.Core.MiPlay;

public sealed record MiPlayFreshLegacyReceiverBootstrapSessionResult(
    bool Accepted,
    string Phase,
    ushort ObservedCommand,
    ushort ObservedSequence,
    bool LegacyAcknowledgementVerified,
    bool EmptyGetDeviceInfoObserved,
    byte[]? ResponseCandidate,
    bool SafeForNetworkUse,
    string Boundary);

public sealed record MiPlayFreshLegacyReceiverProbePolicyDecision(
    bool CanSendNow,
    bool SafeForNetworkUse,
    string Reason);

/// <summary>
/// Pure state machine for the bounded legacy receiver validation. It verifies
/// the existing 0x0028 challenge acknowledgement, accepts one empty 0x001e in
/// either order relative to 0x0029, and prepares exactly one same-sequence
/// 0x001f candidate. It never performs network I/O and never marks the candidate
/// safe by itself.
/// </summary>
public sealed class MiPlayFreshLegacyReceiverBootstrapSession
{
    private bool legacyAcknowledgementVerified;
    private ushort? pendingGetDeviceInfoSequence;
    private bool responsePrepared;

    public bool LegacyAcknowledgementVerified => legacyAcknowledgementVerified;
    public bool EmptyGetDeviceInfoObserved => pendingGetDeviceInfoSequence.HasValue;
    public ushort? PendingGetDeviceInfoSequence => pendingGetDeviceInfoSequence;
    public bool ResponsePrepared => responsePrepared;

    public MiPlayFreshLegacyReceiverBootstrapSessionResult ProcessInboundFrame(ReadOnlySpan<byte> frameBytes)
    {
        if (!MiPlayCommandFrameCodec.TryDecode(frameBytes, out var frame, out var bytesConsumed) ||
            frame is null ||
            bytesConsumed != frameBytes.Length)
        {
            return Reject(0, 0, "The inbound bytes are not one complete MiPlay command frame.");
        }

        if (frame.Command is MiPlayProtocolConstants.SafetyInfoCommand or
            MiPlayProtocolConstants.SafetyInfoAcknowledgementCommand or
            MiPlayProtocolConstants.SafetyAuthCommand or
            MiPlayProtocolConstants.SafetyAuthAcknowledgementCommand)
        {
            return Reject(frame.Command, frame.Sequence, "Modern SafetyInfo/SafetyAuth appeared on the bounded legacy-clear branch.");
        }

        if (frame.Command == MiPlayProtocolConstants.LegacySafetyAcknowledgementCommand)
        {
            if (legacyAcknowledgementVerified)
            {
                return Reject(frame.Command, frame.Sequence, "A duplicate legacy 0x0029 acknowledgement was observed.");
            }

            var expected = MiPlayLegacySafetyChallengeCodec.CreateAcknowledgement(
                MiPlayPassiveSenderCaptureProfile.ChallengeSequence,
                Encoding.ASCII.GetBytes(MiPlayPassiveSenderCaptureProfile.ChallengeText));
            var expectedPayload = Encoding.ASCII.GetBytes(expected.Response);
            if (frame.Sequence != expected.Sequence ||
                !CryptographicOperations.FixedTimeEquals(frame.Payload, expectedPayload))
            {
                return Reject(frame.Command, frame.Sequence, "The legacy 0x0029 acknowledgement did not verify against the one permitted 0x0028 challenge.");
            }

            legacyAcknowledgementVerified = true;
            return pendingGetDeviceInfoSequence.HasValue
                ? PrepareResponse(frame.Command, frame.Sequence, "legacy-auth-after-getDeviceInfo")
                : Observe(frame, "legacy-auth-verified", "Verified legacy 0x0029; waiting for one empty clear 0x001e.");
        }

        if (frame.Command == MiPlayProtocolConstants.GetDeviceInfoCommand)
        {
            if (frame.Payload.Length != 0)
            {
                return Reject(frame.Command, frame.Sequence, "The bounded receiver accepts only the captured empty clear 0x001e shape.");
            }

            if (pendingGetDeviceInfoSequence.HasValue)
            {
                return Reject(frame.Command, frame.Sequence, "A duplicate 0x001e request was observed before the one-frame validation completed.");
            }

            pendingGetDeviceInfoSequence = frame.Sequence;
            return legacyAcknowledgementVerified
                ? PrepareResponse(frame.Command, frame.Sequence, "getDeviceInfo-after-legacy-auth")
                : Observe(frame, "getDeviceInfo-pending-auth", "Captured empty clear 0x001e before 0x0029; the candidate is held until legacy acknowledgement verification.");
        }

        if (frame.Command is MiPlayProtocolConstants.NativeSourceVersionCommand or
            MiPlayProtocolConstants.SetLocalDeviceInfoCommand or
            MiPlayProtocolConstants.HeartbeatCommand)
        {
            return Observe(
                frame,
                responsePrepared ? "post-response-observation" : "bootstrap-observation",
                "Observed an allowed source frame; this state machine prepares no response for it.");
        }

        return Reject(
            frame.Command,
            frame.Sequence,
            $"Unexpected command 0x{frame.Command:X4} is outside the bounded fresh legacy receiver bootstrap.");
    }

    private MiPlayFreshLegacyReceiverBootstrapSessionResult PrepareResponse(
        ushort observedCommand,
        ushort observedSequence,
        string phase)
    {
        if (responsePrepared || !pendingGetDeviceInfoSequence.HasValue)
        {
            return Reject(observedCommand, observedSequence, "The one permitted 0x001f candidate has already been prepared.");
        }

        responsePrepared = true;
        var plan = MiPlayFreshLegacyReceiverBootstrapPlanner.CreateOfflinePlan(
            pendingGetDeviceInfoSequence.Value);
        return new MiPlayFreshLegacyReceiverBootstrapSessionResult(
            Accepted: true,
            phase,
            observedCommand,
            observedSequence,
            LegacyAcknowledgementVerified: true,
            EmptyGetDeviceInfoObserved: true,
            plan.GetDeviceInfoAcknowledgementFrame,
            SafeForNetworkUse: false,
            "Prepared one same-sequence clear 0x001f candidate. A separate explicit-authorization policy must approve any send.");
    }

    private MiPlayFreshLegacyReceiverBootstrapSessionResult Observe(
        MiPlayCommandFrame frame,
        string phase,
        string boundary) =>
        new(
            Accepted: true,
            phase,
            frame.Command,
            frame.Sequence,
            legacyAcknowledgementVerified,
            pendingGetDeviceInfoSequence.HasValue,
            ResponseCandidate: null,
            SafeForNetworkUse: false,
            boundary);

    private MiPlayFreshLegacyReceiverBootstrapSessionResult Reject(
        ushort command,
        ushort sequence,
        string boundary) =>
        new(
            Accepted: false,
            Phase: "stopped",
            command,
            sequence,
            legacyAcknowledgementVerified,
            pendingGetDeviceInfoSequence.HasValue,
            ResponseCandidate: null,
            SafeForNetworkUse: false,
            boundary);
}

public static class MiPlayFreshLegacyReceiverProbePolicy
{
    public static MiPlayFreshLegacyReceiverProbePolicyDecision Evaluate(
        bool explicitUserAuthorization,
        MiPlayFreshLegacyReceiverBootstrapSessionResult sessionResult,
        int outboundLegacyChallengeCount,
        int outboundGetDeviceInfoAcknowledgementCount,
        bool noOtherOutboundFrames)
    {
        ArgumentNullException.ThrowIfNull(sessionResult);

        if (!explicitUserAuthorization)
        {
            return new MiPlayFreshLegacyReceiverProbePolicyDecision(
                false,
                false,
                "Fresh explicit user authorization is required before sending the one candidate 0x001f.");
        }

        if (!sessionResult.Accepted ||
            !sessionResult.LegacyAcknowledgementVerified ||
            !sessionResult.EmptyGetDeviceInfoObserved ||
            sessionResult.ResponseCandidate is null)
        {
            return new MiPlayFreshLegacyReceiverProbePolicyDecision(
                false,
                false,
                "The session has not verified 0x0029 and one empty clear 0x001e or has no response candidate.");
        }

        if (outboundLegacyChallengeCount != 1 ||
            outboundGetDeviceInfoAcknowledgementCount != 0 ||
            !noOtherOutboundFrames)
        {
            return new MiPlayFreshLegacyReceiverProbePolicyDecision(
                false,
                false,
                "Outbound accounting is not exactly one 0x0028 and zero prior 0x001f with no other frames.");
        }

        if (!MiPlayCommandFrameCodec.TryDecode(
                sessionResult.ResponseCandidate,
                out var response,
                out var bytesConsumed) ||
            response is null ||
            bytesConsumed != sessionResult.ResponseCandidate.Length ||
            response.Command != MiPlayProtocolConstants.GetDeviceInfoAcknowledgementCommand)
        {
            return new MiPlayFreshLegacyReceiverProbePolicyDecision(
                false,
                false,
                "The prepared response is not one strict 0x001f command frame.");
        }

        return new MiPlayFreshLegacyReceiverProbePolicyDecision(
            true,
            true,
            "Explicit authorization plus verified legacy auth, one empty clear 0x001e, and exact outbound accounting permit one same-sequence 0x001f only. No second response or other command is permitted.");
    }
}
