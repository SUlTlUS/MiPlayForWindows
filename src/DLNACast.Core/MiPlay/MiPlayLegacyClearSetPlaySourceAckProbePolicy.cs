namespace DLNACast.Core.MiPlay;

public sealed record MiPlayLegacyClearSetPlaySourceAckPrerequisites(
    bool LegacyChallengeAcknowledged,
    bool NativeVersionBootstrapSent,
    bool MpasModernSafetyCommandConstantsAbsentObserved,
    bool MpasExternalSetPlaySourceDispatchObserved,
    bool MpasAcknowledgesBeforePayloadParse,
    ushort NextCommandSequence,
    bool EmptyPayloadOnly,
    bool NoModernSafetyInfoOrSafetyAuth,
    bool NoSafetyDataEncryption,
    bool NoMediaBoundary,
    bool ForbidCmdOpen,
    bool Forbid0058,
    bool ForbidAddMirror,
    bool ForbidRtsp,
    bool ForbidPlaybackOrAudio);

public sealed record MiPlayLegacyClearSetPlaySourceAckDecision(
    bool CanSend,
    string Reason,
    ushort? Command = null,
    ushort? Sequence = null,
    int? PlaintextPayloadLength = null);

/// <summary>
/// Safety gate for testing the LX06 1.88.x legacy clear-text control dispatcher.
/// The probe is narrower than any playback/open path: after legacy 0x0028/0x0029
/// it sends one empty clear-text 0x0040 and only observes for clear 0x0041.
/// </summary>
public static class MiPlayLegacyClearSetPlaySourceAckProbePolicy
{
    public static MiPlayLegacyClearSetPlaySourceAckDecision EvaluateAckReadiness(
        MiPlayLegacyClearSetPlaySourceAckPrerequisites prerequisites)
    {
        if (!prerequisites.LegacyChallengeAcknowledged)
        {
            return new MiPlayLegacyClearSetPlaySourceAckDecision(false, "Legacy 0x0028 -> 0x0029 acknowledgement has not been verified.");
        }

        if (!prerequisites.NativeVersionBootstrapSent)
        {
            return new MiPlayLegacyClearSetPlaySourceAckDecision(false, "The native source-version bootstrap has not been sent for this receiver profile.");
        }

        if (!prerequisites.MpasModernSafetyCommandConstantsAbsentObserved)
        {
            return new MiPlayLegacyClearSetPlaySourceAckDecision(false, "The LX06 mpas modern 0x1400..0x1403 absence boundary has not been documented.");
        }

        if (!prerequisites.MpasExternalSetPlaySourceDispatchObserved)
        {
            return new MiPlayLegacyClearSetPlaySourceAckDecision(false, "The LX06 mpas external 0x0040 Cmd_SetPlaySource dispatcher evidence is missing.");
        }

        if (!prerequisites.MpasAcknowledgesBeforePayloadParse)
        {
            return new MiPlayLegacyClearSetPlaySourceAckDecision(false, "The clear 0x0040 ACK-before-payload-parse boundary has not been established.");
        }

        if (prerequisites.NextCommandSequence == 0)
        {
            return new MiPlayLegacyClearSetPlaySourceAckDecision(false, "The next clear command sequence is not initialized.");
        }

        if (!prerequisites.EmptyPayloadOnly)
        {
            return new MiPlayLegacyClearSetPlaySourceAckDecision(false, "The legacy clear Cmd_SetPlaySource validation must use an empty payload only.");
        }

        if (!prerequisites.NoModernSafetyInfoOrSafetyAuth)
        {
            return new MiPlayLegacyClearSetPlaySourceAckDecision(false, "The legacy clear probe must not mix in 0x1400 SafetyInfo or 0x1402/0x1403 SafetyAuth.");
        }

        if (!prerequisites.NoSafetyDataEncryption)
        {
            return new MiPlayLegacyClearSetPlaySourceAckDecision(false, "The legacy clear probe must not SafetyData-wrap the 0x0040 payload.");
        }

        if (!prerequisites.NoMediaBoundary)
        {
            return new MiPlayLegacyClearSetPlaySourceAckDecision(false, "The legacy clear probe boundary must explicitly forbid media, RTSP, playback, and audio frames.");
        }

        if (!prerequisites.ForbidCmdOpen)
        {
            return new MiPlayLegacyClearSetPlaySourceAckDecision(false, "Cmd_Open 0x0000 must remain forbidden for the legacy clear ACK validation.");
        }

        if (!prerequisites.Forbid0058)
        {
            return new MiPlayLegacyClearSetPlaySourceAckDecision(false, "0x0058 must remain forbidden for the legacy clear ACK validation.");
        }

        if (!prerequisites.ForbidAddMirror)
        {
            return new MiPlayLegacyClearSetPlaySourceAckDecision(false, "Cmd_AddMirror 0x002e must remain forbidden for the legacy clear ACK validation.");
        }

        if (!prerequisites.ForbidRtsp)
        {
            return new MiPlayLegacyClearSetPlaySourceAckDecision(false, "RTSP listener/response traffic must remain forbidden for the legacy clear ACK validation.");
        }

        if (!prerequisites.ForbidPlaybackOrAudio)
        {
            return new MiPlayLegacyClearSetPlaySourceAckDecision(false, "Playback, media, and audio frames must remain forbidden for the legacy clear ACK validation.");
        }

        return new MiPlayLegacyClearSetPlaySourceAckDecision(
            true,
            "Ready to send exactly one empty clear-text Cmd_SetPlaySource 0x0040 after legacy 0x0029 and then only observe for clear 0x0041; send no 0x1400, 0x1402, 0x1403, SafetyData, JSON source identity, Cmd_Open, 0x0058, AddMirror, RTSP, media, playback, audio, retry, or fallback control frame.",
            MiPlayProtocolConstants.SetPlaySourceCommand,
            prerequisites.NextCommandSequence,
            0);
    }
}
