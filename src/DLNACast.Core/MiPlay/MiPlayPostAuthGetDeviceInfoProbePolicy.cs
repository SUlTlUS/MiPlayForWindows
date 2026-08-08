namespace DLNACast.Core.MiPlay;

public sealed record MiPlayPostAuthGetDeviceInfoProbePrerequisites(
    bool MutualSafetyAuthVerified,
    bool SafetyDataSessionCandidateAvailable,
    bool NativeNoResetOutboundProfileAvailable,
    bool OfficialGetDeviceInfoOrderLocalized,
    bool CmdSourceGetDeviceInfoFrameShapeLocalized,
    bool Source001fAckListenerLocalized,
    bool ReceiverGetDeviceInfoAckSemanticsLocalized,
    bool FreshUserAuthorizationPresent,
    ushort NextCommandSequence,
    bool EmptyPayloadOnly,
    bool ObserveOnlyFor001f,
    bool RequireSameSequence001f,
    bool RequireMinimumPayloadLength,
    bool StopOnAnyUnexpectedFrameOrClose,
    bool ForbidRetry,
    bool Forbid0040,
    bool Forbid0058,
    bool ForbidCmdOpen,
    bool ForbidAddMirror,
    bool ForbidRtsp,
    bool ForbidMediaPlaybackOrAudio);

public sealed record MiPlayPostAuthGetDeviceInfoProbeDecision(
    bool CanPreparePlan,
    bool CanSendNow,
    string Reason,
    ushort? Command = null,
    ushort? ExpectedAcknowledgementCommand = null,
    ushort? Sequence = null,
    int? PlaintextPayloadLength = null,
    int? MinimumAcknowledgementPayloadLength = null);

/// <summary>
/// Safety gate for the first live-readonly post-auth Cmd_GetDeviceInfo
/// validation. This policy authorizes at most one SafetyData-wrapped 0x001e
/// frame after mutual SafetyAuth and observation for a same-sequence 0x001f.
/// It never authorizes 0x0040, 0x0058, Open, AddMirror, RTSP, media,
/// playback, or audio frames.
/// </summary>
public static class MiPlayPostAuthGetDeviceInfoProbePolicy
{
    public const string Boundary =
        "exactly one SafetyData-wrapped 0x001e with empty plaintext; observe only for same-sequence 0x001f; no retry, fallback, 0x0040, 0x0058, Cmd_Open, AddMirror, RTSP, media, playback, or audio";

    public static MiPlayPostAuthGetDeviceInfoProbeDecision EvaluateReadiness(
        MiPlayPostAuthGetDeviceInfoProbePrerequisites prerequisites)
    {
        if (!prerequisites.MutualSafetyAuthVerified)
        {
            return new MiPlayPostAuthGetDeviceInfoProbeDecision(false, false, "Mutual SafetyAuth has not been verified.");
        }

        if (!prerequisites.SafetyDataSessionCandidateAvailable)
        {
            return new MiPlayPostAuthGetDeviceInfoProbeDecision(false, false, "No verified SafetyData session candidate is available for Cmd_GetDeviceInfo.");
        }

        if (!prerequisites.NativeNoResetOutboundProfileAvailable)
        {
            return new MiPlayPostAuthGetDeviceInfoProbeDecision(false, false, "The native no-reset outbound SafetyData profile cannot be reconstructed from local 0x1402/0x1403 plaintext state.");
        }

        if (!prerequisites.OfficialGetDeviceInfoOrderLocalized)
        {
            return new MiPlayPostAuthGetDeviceInfoProbeDecision(false, false, "The official cmdSessionSuccess -> getDeviceInfo order is not localized.");
        }

        if (!prerequisites.CmdSourceGetDeviceInfoFrameShapeLocalized)
        {
            return new MiPlayPostAuthGetDeviceInfoProbeDecision(false, false, "CmdSource::getDeviceInfo frame shape is not localized.");
        }

        if (!prerequisites.Source001fAckListenerLocalized)
        {
            return new MiPlayPostAuthGetDeviceInfoProbeDecision(false, false, "The source-side 0x001f acknowledgement listener is not localized.");
        }

        if (!prerequisites.ReceiverGetDeviceInfoAckSemanticsLocalized)
        {
            return new MiPlayPostAuthGetDeviceInfoProbeDecision(false, false, "The receiver-side 0x001e -> 0x001f acknowledgement semantics are not localized.");
        }

        if (prerequisites.NextCommandSequence == 0)
        {
            return new MiPlayPostAuthGetDeviceInfoProbeDecision(false, false, "The next command sequence is not initialized.");
        }

        if (!prerequisites.EmptyPayloadOnly)
        {
            return new MiPlayPostAuthGetDeviceInfoProbeDecision(false, false, "Cmd_GetDeviceInfo must use an empty plaintext payload.");
        }

        if (!prerequisites.ObserveOnlyFor001f)
        {
            return new MiPlayPostAuthGetDeviceInfoProbeDecision(false, false, "The plan must observe only for 0x001f and must not interpret success as permission for later commands.");
        }

        if (!prerequisites.RequireSameSequence001f)
        {
            return new MiPlayPostAuthGetDeviceInfoProbeDecision(false, false, "The plan must require the 0x001f acknowledgement sequence to match the 0x001e request.");
        }

        if (!prerequisites.RequireMinimumPayloadLength)
        {
            return new MiPlayPostAuthGetDeviceInfoProbeDecision(false, false, "The plan must require a minimum decrypted 0x001f payload length before treating the read-only gate as passed.");
        }

        if (!prerequisites.StopOnAnyUnexpectedFrameOrClose)
        {
            return new MiPlayPostAuthGetDeviceInfoProbeDecision(false, false, "The plan must stop on close or any unexpected frame.");
        }

        if (!prerequisites.ForbidRetry)
        {
            return new MiPlayPostAuthGetDeviceInfoProbeDecision(false, false, "The plan must forbid retry and fallback frames.");
        }

        if (!prerequisites.Forbid0040)
        {
            return new MiPlayPostAuthGetDeviceInfoProbeDecision(false, false, "Cmd_SetPlaySource 0x0040 must remain forbidden for the read-only getDeviceInfo validation.");
        }

        if (!prerequisites.Forbid0058)
        {
            return new MiPlayPostAuthGetDeviceInfoProbeDecision(false, false, "0x0058 must remain forbidden for the read-only getDeviceInfo validation.");
        }

        if (!prerequisites.ForbidCmdOpen)
        {
            return new MiPlayPostAuthGetDeviceInfoProbeDecision(false, false, "Cmd_Open 0x0000 must remain forbidden for the read-only getDeviceInfo validation.");
        }

        if (!prerequisites.ForbidAddMirror)
        {
            return new MiPlayPostAuthGetDeviceInfoProbeDecision(false, false, "Cmd_AddMirror 0x002e must remain forbidden for the read-only getDeviceInfo validation.");
        }

        if (!prerequisites.ForbidRtsp)
        {
            return new MiPlayPostAuthGetDeviceInfoProbeDecision(false, false, "RTSP listener/response traffic must remain forbidden for the read-only getDeviceInfo validation.");
        }

        if (!prerequisites.ForbidMediaPlaybackOrAudio)
        {
            return new MiPlayPostAuthGetDeviceInfoProbeDecision(false, false, "Media, playback, and audio frames must remain forbidden for the read-only getDeviceInfo validation.");
        }

        if (!prerequisites.FreshUserAuthorizationPresent)
        {
            return new MiPlayPostAuthGetDeviceInfoProbeDecision(
                true,
                false,
                $"Prepared but not sendable: this requires fresh explicit user authorization for one S12 network action. If authorized, {Boundary}.",
                MiPlayProtocolConstants.GetDeviceInfoCommand,
                MiPlayProtocolConstants.GetDeviceInfoAcknowledgementCommand,
                prerequisites.NextCommandSequence,
                0,
                MiPlayPostAuthProbePolicy.MinimumDeviceInfoAcknowledgementPayloadLength);
        }

        return new MiPlayPostAuthGetDeviceInfoProbeDecision(
            true,
            true,
            $"Ready for a single authorized read-only network action: {Boundary}.",
            MiPlayProtocolConstants.GetDeviceInfoCommand,
            MiPlayProtocolConstants.GetDeviceInfoAcknowledgementCommand,
            prerequisites.NextCommandSequence,
            0,
            MiPlayPostAuthProbePolicy.MinimumDeviceInfoAcknowledgementPayloadLength);
    }
}
