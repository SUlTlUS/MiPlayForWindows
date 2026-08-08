namespace DLNACast.Core.MiPlay;

public sealed record MiPlaySetPlaySourceOneFramePrerequisites(
    bool MutualSafetyAuthVerified,
    bool SafetyDataSessionCandidateAvailable,
    bool OfficialSenderPayloadBuilderLocalized,
    bool NativeSetPlaySourceCommandId0040Confirmed,
    bool NativeConnectCmdSession2OnlyCarriesLyraKeyMaterial,
    bool PriorEmptyAckRoutesClosedWithoutAcknowledgement,
    bool FreshUserAuthorizationPresent,
    ushort NextCommandSequence,
    string RefChannel,
    string RefFunction,
    string RefContent,
    bool ObserveOnlyFor0041,
    bool StopOnAnyUnexpectedFrameOrClose,
    bool ForbidRetry,
    bool Forbid0058,
    bool ForbidCmdOpen,
    bool ForbidAddMirror,
    bool ForbidRtsp,
    bool ForbidMediaPlaybackOrAudio);

public sealed record MiPlaySetPlaySourceOneFrameDecision(
    bool CanPreparePlan,
    bool CanSendNow,
    string Reason,
    ushort? Command = null,
    ushort? Sequence = null,
    string? PayloadText = null,
    int? PlaintextPayloadLength = null);

/// <summary>
/// Safety gate for the next distinct post-auth SetPlaySource validation.
/// This is not the old empty-payload ACK route: it prepares exactly one
/// SafetyData-wrapped 0x0040 carrying the official Android JSON source payload
/// and then only observes for 0x0041. It does not authorize 0x0058, Cmd_Open,
/// AddMirror, RTSP, media, playback, or audio.
/// </summary>
public static class MiPlaySetPlaySourceOneFrameProbePlan
{
    public const string MinimalRefChannel = "playpage";
    public const string MinimalRefFunction = "";
    public const string MinimalRefContent = "";
    public const string ExpectedAcknowledgement = "0x0041";

    public const string Boundary =
        "exactly one SafetyData-wrapped 0x0040 with official minimal JSON payload; observe only for 0x0041; stop on close/unexpected frame; no retry, fallback, 0x0058, Cmd_Open, AddMirror, RTSP, media, playback, or audio";

    public static MiPlaySetPlaySourceOneFrameDecision Evaluate(
        MiPlaySetPlaySourceOneFramePrerequisites prerequisites)
    {
        if (!prerequisites.MutualSafetyAuthVerified)
        {
            return new MiPlaySetPlaySourceOneFrameDecision(false, false, "Mutual SafetyAuth has not been verified.");
        }

        if (!prerequisites.SafetyDataSessionCandidateAvailable)
        {
            return new MiPlaySetPlaySourceOneFrameDecision(false, false, "No verified SafetyData session candidate is available for the 0x0040 one-frame plan.");
        }

        if (!prerequisites.OfficialSenderPayloadBuilderLocalized)
        {
            return new MiPlaySetPlaySourceOneFrameDecision(false, false, "The official Android SetPlaySource JSON builder has not been localized.");
        }

        if (!prerequisites.NativeSetPlaySourceCommandId0040Confirmed)
        {
            return new MiPlaySetPlaySourceOneFrameDecision(false, false, "Native source evidence has not confirmed that setPlaySource sends command 0x0040.");
        }

        if (!prerequisites.NativeConnectCmdSession2OnlyCarriesLyraKeyMaterial)
        {
            return new MiPlaySetPlaySourceOneFrameDecision(false, false, "The optional connectCmdSession2 secret-key bridge is not bounded to Lyra key material.");
        }

        if (!prerequisites.PriorEmptyAckRoutesClosedWithoutAcknowledgement)
        {
            return new MiPlaySetPlaySourceOneFrameDecision(false, false, "The previous empty-payload 0x0040 negative evidence is missing; do not skip directly to a non-empty payload probe.");
        }

        if (prerequisites.NextCommandSequence == 0)
        {
            return new MiPlaySetPlaySourceOneFrameDecision(false, false, "The next command sequence is not initialized.");
        }

        if (!IsMinimalOfficialPayload(prerequisites))
        {
            return new MiPlaySetPlaySourceOneFrameDecision(false, false, "The one-frame plan is limited to the official minimal payload {ref_channel=playpage, ref_function='', ref_content=''}.");
        }

        if (!prerequisites.ObserveOnlyFor0041)
        {
            return new MiPlaySetPlaySourceOneFrameDecision(false, false, "The plan must observe only for 0x0041 and must not interpret success as permission for later commands.");
        }

        if (!prerequisites.StopOnAnyUnexpectedFrameOrClose)
        {
            return new MiPlaySetPlaySourceOneFrameDecision(false, false, "The plan must stop on close or any unexpected frame.");
        }

        if (!prerequisites.ForbidRetry)
        {
            return new MiPlaySetPlaySourceOneFrameDecision(false, false, "The plan must forbid retry and fallback frames.");
        }

        if (!prerequisites.Forbid0058)
        {
            return new MiPlaySetPlaySourceOneFrameDecision(false, false, "0x0058 must remain forbidden for the one-frame SetPlaySource validation.");
        }

        if (!prerequisites.ForbidCmdOpen)
        {
            return new MiPlaySetPlaySourceOneFrameDecision(false, false, "Cmd_Open 0x0000 must remain forbidden for the one-frame SetPlaySource validation.");
        }

        if (!prerequisites.ForbidAddMirror)
        {
            return new MiPlaySetPlaySourceOneFrameDecision(false, false, "Cmd_AddMirror 0x002e must remain forbidden for the one-frame SetPlaySource validation.");
        }

        if (!prerequisites.ForbidRtsp)
        {
            return new MiPlaySetPlaySourceOneFrameDecision(false, false, "RTSP listener/response traffic must remain forbidden for the one-frame SetPlaySource validation.");
        }

        if (!prerequisites.ForbidMediaPlaybackOrAudio)
        {
            return new MiPlaySetPlaySourceOneFrameDecision(false, false, "Media, playback, and audio frames must remain forbidden for the one-frame SetPlaySource validation.");
        }

        var payload = MiPlaySetPlaySourcePayloadCodec.EncodeOfficialStatsDefaults(
            prerequisites.RefChannel,
            prerequisites.RefFunction,
            prerequisites.RefContent);
        var payloadText = MiPlaySetPlaySourcePayloadCodec.DecodeUtf8(payload);

        if (!prerequisites.FreshUserAuthorizationPresent)
        {
            return new MiPlaySetPlaySourceOneFrameDecision(
                true,
                false,
                "Prepared but not sendable: this requires fresh explicit user authorization for one S12 network action. If authorized, send exactly one SafetyData-wrapped Cmd_SetPlaySource 0x0040 with the official minimal JSON payload, observe only for 0x0041, then stop; no retry, fallback, 0x0058, Cmd_Open, AddMirror, RTSP, media, playback, or audio.",
                MiPlayProtocolConstants.SetPlaySourceCommand,
                prerequisites.NextCommandSequence,
                payloadText,
                payload.Length);
        }

        return new MiPlaySetPlaySourceOneFrameDecision(
            true,
            true,
            "Ready for a single authorized network action: send exactly one SafetyData-wrapped Cmd_SetPlaySource 0x0040 with the official minimal JSON payload, observe only for 0x0041, and stop on close or any unexpected frame; no retry, fallback, 0x0058, Cmd_Open, AddMirror, RTSP, media, playback, or audio.",
            MiPlayProtocolConstants.SetPlaySourceCommand,
            prerequisites.NextCommandSequence,
            payloadText,
            payload.Length);
    }

    private static bool IsMinimalOfficialPayload(MiPlaySetPlaySourceOneFramePrerequisites prerequisites) =>
        string.Equals(prerequisites.RefChannel, MinimalRefChannel, StringComparison.Ordinal) &&
        string.Equals(prerequisites.RefFunction, MinimalRefFunction, StringComparison.Ordinal) &&
        string.Equals(prerequisites.RefContent, MinimalRefContent, StringComparison.Ordinal);
}
