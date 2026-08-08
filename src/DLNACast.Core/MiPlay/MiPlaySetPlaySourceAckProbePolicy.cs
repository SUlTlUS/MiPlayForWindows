namespace DLNACast.Core.MiPlay;

public sealed record MiPlaySetPlaySourceAckPrerequisites(
    bool MutualSafetyAuthVerified,
    bool SafetyDataSessionCandidateAvailable,
    bool MpasExternalSetPlaySourceDispatchObserved,
    bool MpasAcknowledgesBeforePayloadParse,
    ushort NextCommandSequence,
    bool EmptyPayloadOnly,
    bool NoMediaBoundary,
    bool ForbidCmdOpen,
    bool Forbid0058,
    bool ForbidAddMirror,
    bool ForbidRtsp,
    bool ForbidPlaybackOrAudio);

public sealed record MiPlaySetPlaySourceAckDecision(
    bool CanSend,
    string Reason,
    ushort? Command = null,
    ushort? Sequence = null,
    int? PlaintextPayloadLength = null);

/// <summary>
/// Safety gate for a single post-auth 0x0040 Cmd_SetPlaySource ACK-only probe.
/// It authorizes one empty-plaintext SafetyData frame and then observation for
/// 0x0041; it does not authorize identity JSON, Cmd_Open, 0x0058, AddMirror,
/// RTSP, media, playback, or audio.
/// </summary>
public static class MiPlaySetPlaySourceAckProbePolicy
{
    public static MiPlaySetPlaySourceAckDecision EvaluateAckReadiness(
        MiPlaySetPlaySourceAckPrerequisites prerequisites)
    {
        if (!prerequisites.MutualSafetyAuthVerified)
        {
            return new MiPlaySetPlaySourceAckDecision(false, "Mutual SafetyAuth has not been verified.");
        }

        if (!prerequisites.SafetyDataSessionCandidateAvailable)
        {
            return new MiPlaySetPlaySourceAckDecision(false, "No verified SafetyData session candidate is available for Cmd_SetPlaySource.");
        }

        if (!prerequisites.MpasExternalSetPlaySourceDispatchObserved)
        {
            return new MiPlaySetPlaySourceAckDecision(false, "The LX06 mpas external 0x0040 Cmd_SetPlaySource dispatcher evidence is missing.");
        }

        if (!prerequisites.MpasAcknowledgesBeforePayloadParse)
        {
            return new MiPlaySetPlaySourceAckDecision(false, "The ACK-before-payload-parse boundary has not been established.");
        }

        if (prerequisites.NextCommandSequence == 0)
        {
            return new MiPlaySetPlaySourceAckDecision(false, "The next command sequence is not initialized.");
        }

        if (!prerequisites.EmptyPayloadOnly)
        {
            return new MiPlaySetPlaySourceAckDecision(false, "The first Cmd_SetPlaySource validation must use an empty plaintext payload only.");
        }

        if (!prerequisites.NoMediaBoundary)
        {
            return new MiPlaySetPlaySourceAckDecision(false, "The probe boundary must explicitly forbid media, RTSP, playback, and audio frames.");
        }

        if (!prerequisites.ForbidCmdOpen)
        {
            return new MiPlaySetPlaySourceAckDecision(false, "Cmd_Open 0x0000 must remain forbidden for the Cmd_SetPlaySource ACK-only validation.");
        }

        if (!prerequisites.Forbid0058)
        {
            return new MiPlaySetPlaySourceAckDecision(false, "0x0058 must remain forbidden for the Cmd_SetPlaySource ACK-only validation.");
        }

        if (!prerequisites.ForbidAddMirror)
        {
            return new MiPlaySetPlaySourceAckDecision(false, "Cmd_AddMirror 0x002e must remain forbidden for the Cmd_SetPlaySource ACK-only validation.");
        }

        if (!prerequisites.ForbidRtsp)
        {
            return new MiPlaySetPlaySourceAckDecision(false, "RTSP listener/response traffic must remain forbidden for the Cmd_SetPlaySource ACK-only validation.");
        }

        if (!prerequisites.ForbidPlaybackOrAudio)
        {
            return new MiPlaySetPlaySourceAckDecision(false, "Playback, media, and audio frames must remain forbidden for the Cmd_SetPlaySource ACK-only validation.");
        }

        return new MiPlaySetPlaySourceAckDecision(
            true,
            "Ready to send exactly one empty-plaintext SafetyData-wrapped Cmd_SetPlaySource 0x0040 and then only observe for 0x0041; send no JSON source identity, Cmd_Open, 0x0058, AddMirror, RTSP, media, playback, audio, retry, or fallback control frame.",
            MiPlayProtocolConstants.SetPlaySourceCommand,
            prerequisites.NextCommandSequence,
            0);
    }
}