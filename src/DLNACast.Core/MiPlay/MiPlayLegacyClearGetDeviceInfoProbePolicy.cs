namespace DLNACast.Core.MiPlay;

public sealed record MiPlayLegacyClearGetDeviceInfoPrerequisites(
    bool LegacyChallengeAcknowledged,
    bool NativeVersionBootstrapSent,
    bool MpasGetDeviceInfoDispatchObserved,
    bool MpasGetDeviceInfoAcknowledgementObserved,
    bool MpasGetDeviceInfoAsyncPreparePathObserved,
    bool ReadyStateNotifyObservedBeforeSend,
    ushort NextCommandSequence,
    bool EmptyPayloadOnly,
    bool NoModernSafetyInfoOrSafetyAuth,
    bool NoSafetyDataEncryption,
    bool NoSetPlaySource,
    bool NoMediaBoundary,
    bool ForbidCmdOpen,
    bool Forbid0058,
    bool ForbidAddMirror,
    bool ForbidRtsp,
    bool ForbidPlaybackOrAudio);

public sealed record MiPlayLegacyClearGetDeviceInfoDecision(
    bool CanSend,
    string Reason,
    ushort? Command = null,
    ushort? ExpectedAcknowledgementCommand = null,
    ushort? Sequence = null,
    int? PlaintextPayloadLength = null);

/// <summary>
/// Safety gate for a single read-only LX06 legacy clear-text Cmd_GetDeviceInfo
/// validation. This is intentionally narrower than SetPlaySource, AddMirror,
/// Cmd_Open, RTSP, playback, or any audio/media path.
/// </summary>
public static class MiPlayLegacyClearGetDeviceInfoProbePolicy
{
    public static MiPlayLegacyClearGetDeviceInfoDecision EvaluateReadiness(
        MiPlayLegacyClearGetDeviceInfoPrerequisites prerequisites)
    {
        if (!prerequisites.LegacyChallengeAcknowledged)
        {
            return new MiPlayLegacyClearGetDeviceInfoDecision(false, "Legacy 0x0028 -> 0x0029 acknowledgement has not been verified.");
        }

        if (!prerequisites.NativeVersionBootstrapSent)
        {
            return new MiPlayLegacyClearGetDeviceInfoDecision(false, "The native source-version bootstrap has not been sent for this receiver profile.");
        }

        if (!prerequisites.MpasGetDeviceInfoDispatchObserved)
        {
            return new MiPlayLegacyClearGetDeviceInfoDecision(false, "The LX06 mpas 0x001e Cmd_GetDeviceInfo dispatcher evidence is missing.");
        }

        if (!prerequisites.MpasGetDeviceInfoAcknowledgementObserved)
        {
            return new MiPlayLegacyClearGetDeviceInfoDecision(false, "The LX06 mpas 0x001e -> 0x001f acknowledgement evidence is missing.");
        }

        if (!prerequisites.MpasGetDeviceInfoAsyncPreparePathObserved)
        {
            return new MiPlayLegacyClearGetDeviceInfoDecision(false, "The async device-info preparation path has not been documented, so a delayed 0x001f cannot be interpreted safely.");
        }

        if (!prerequisites.ReadyStateNotifyObservedBeforeSend)
        {
            return new MiPlayLegacyClearGetDeviceInfoDecision(false, "The clear 0x001e validation must wait for decoded notify label=state integerValue=3 before sending.");
        }

        if (prerequisites.NextCommandSequence == 0)
        {
            return new MiPlayLegacyClearGetDeviceInfoDecision(false, "The next clear command sequence is not initialized.");
        }

        if (!prerequisites.EmptyPayloadOnly)
        {
            return new MiPlayLegacyClearGetDeviceInfoDecision(false, "The legacy clear Cmd_GetDeviceInfo validation must use an empty payload only.");
        }

        if (!prerequisites.NoModernSafetyInfoOrSafetyAuth)
        {
            return new MiPlayLegacyClearGetDeviceInfoDecision(false, "The legacy clear probe must not mix in 0x1400 SafetyInfo or 0x1402/0x1403 SafetyAuth.");
        }

        if (!prerequisites.NoSafetyDataEncryption)
        {
            return new MiPlayLegacyClearGetDeviceInfoDecision(false, "The legacy clear probe must not SafetyData-wrap the 0x001e payload.");
        }

        if (!prerequisites.NoSetPlaySource)
        {
            return new MiPlayLegacyClearGetDeviceInfoDecision(false, "Cmd_SetPlaySource 0x0040 must remain forbidden for the getDeviceInfo-only validation.");
        }

        if (!prerequisites.NoMediaBoundary)
        {
            return new MiPlayLegacyClearGetDeviceInfoDecision(false, "The legacy clear getDeviceInfo boundary must explicitly forbid media, RTSP, playback, and audio frames.");
        }

        if (!prerequisites.ForbidCmdOpen)
        {
            return new MiPlayLegacyClearGetDeviceInfoDecision(false, "Cmd_Open 0x0000 must remain forbidden for the legacy clear getDeviceInfo validation.");
        }

        if (!prerequisites.Forbid0058)
        {
            return new MiPlayLegacyClearGetDeviceInfoDecision(false, "0x0058 must remain forbidden for the legacy clear getDeviceInfo validation.");
        }

        if (!prerequisites.ForbidAddMirror)
        {
            return new MiPlayLegacyClearGetDeviceInfoDecision(false, "Cmd_AddMirror 0x002e must remain forbidden for the legacy clear getDeviceInfo validation.");
        }

        if (!prerequisites.ForbidRtsp)
        {
            return new MiPlayLegacyClearGetDeviceInfoDecision(false, "RTSP listener/response traffic must remain forbidden for the legacy clear getDeviceInfo validation.");
        }

        if (!prerequisites.ForbidPlaybackOrAudio)
        {
            return new MiPlayLegacyClearGetDeviceInfoDecision(false, "Playback, media, and audio frames must remain forbidden for the legacy clear getDeviceInfo validation.");
        }

        return new MiPlayLegacyClearGetDeviceInfoDecision(
            true,
            "Ready to send exactly one empty clear-text Cmd_GetDeviceInfo 0x001e after legacy 0x0029 and decoded state=3 notify, then only observe for clear 0x001f; send no 0x1400, 0x1402, 0x1403, SafetyData, Cmd_SetPlaySource, JSON source identity, Cmd_Open, 0x0058, AddMirror, RTSP, media, playback, audio, retry, or fallback control frame.",
            MiPlayProtocolConstants.GetDeviceInfoCommand,
            MiPlayProtocolConstants.GetDeviceInfoAcknowledgementCommand,
            prerequisites.NextCommandSequence,
            0);
    }
}