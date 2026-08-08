namespace DLNACast.Core.MiPlay;

public sealed record MiPlayPostAuthRouteExclusionSnapshot(
    bool UserConfirmedCurrentLx06FirmwareVersionKnown,
    bool ControlSessionVersionFrameKeptSeparateFromFirmwareVersion,
    bool MpasSetPlaySourceAckBeforePayloadParseObserved,
    bool ModernSafetyOwnerLocalizedIn18851ReceiverStack,
    bool LegacyClearImmediateSetPlaySourceClosedWithoutAck,
    bool LegacyClearAfterReadyNotifySetPlaySourceClosedWithoutAck,
    bool SafetyDataImmediateSetPlaySourceClosedWithoutAck,
    bool SafetyDataDelayedSetPlaySourceClosedWithoutAck,
    bool SafetyDataNativeNoResetOfficialJsonClosedWithoutAck,
    bool SafetyDataNativeNoResetOfficialJsonUsedSeparatedOutboundProfile,
    bool StrictNoMediaNoPlaybackBoundaryHeld,
    bool Current19413CommandSessionBridgeLocalized,
    bool CandidateFrameTargetsLocalizedBridge,
    bool CandidateFrameIsReadOnlyAckBeforeMutation);

public sealed record MiPlayPostAuthRouteExclusionDecision(
    bool CanJustifyNextLiveBusinessProbe,
    string Reason,
    string NextOfflineTarget);

/// <summary>
/// Combines the post-auth negative live validations with the receiver-side
/// static ACK-before-parse boundary. This is an offline route-exclusion matrix:
/// it does not construct or authorize any Probe frame by itself.
/// </summary>
public static class MiPlayPostAuthRouteExclusionEvidence
{
    public const string UserConfirmedCurrentLx06FirmwareVersion = "1.94.13";
    public const string ObservedControlSessionVersionFrame = "2.1.5091615";
    public const string FirmwareVersionBoundary =
        "0x0037 control-session version frames are not LX06 ROM firmware versions";
    public const string RemainingOfflineTarget =
        "localize the current 1.94.13 command-session bridge/handler owner, source/session context, and ordering/state transition that accepts modern 0x1400..0x1403 before handing business commands to the legacy CtrlClient/ServerApp dispatcher";

    public static MiPlayPostAuthRouteExclusionSnapshot CreateCurrentSnapshot() =>
        new(
            UserConfirmedCurrentLx06FirmwareVersionKnown: true,
            ControlSessionVersionFrameKeptSeparateFromFirmwareVersion: true,
            MpasSetPlaySourceAckBeforePayloadParseObserved: true,
            ModernSafetyOwnerLocalizedIn18851ReceiverStack: false,
            LegacyClearImmediateSetPlaySourceClosedWithoutAck: true,
            LegacyClearAfterReadyNotifySetPlaySourceClosedWithoutAck: true,
            SafetyDataImmediateSetPlaySourceClosedWithoutAck: true,
            SafetyDataDelayedSetPlaySourceClosedWithoutAck: true,
            SafetyDataNativeNoResetOfficialJsonClosedWithoutAck: true,
            SafetyDataNativeNoResetOfficialJsonUsedSeparatedOutboundProfile: true,
            StrictNoMediaNoPlaybackBoundaryHeld: true,
            Current19413CommandSessionBridgeLocalized: false,
            CandidateFrameTargetsLocalizedBridge: false,
            CandidateFrameIsReadOnlyAckBeforeMutation: false);

    public static MiPlayPostAuthRouteExclusionDecision EvaluateNextLiveBusinessProbe(
        MiPlayPostAuthRouteExclusionSnapshot snapshot)
    {
        if (!snapshot.UserConfirmedCurrentLx06FirmwareVersionKnown ||
            !snapshot.ControlSessionVersionFrameKeptSeparateFromFirmwareVersion)
        {
            return new MiPlayPostAuthRouteExclusionDecision(
                false,
                $"LX06 firmware version evidence is not clean: {ObservedControlSessionVersionFrame} must remain only a 0x0037 control-session version frame, while the current LX06 ROM boundary is {UserConfirmedCurrentLx06FirmwareVersion}.",
                "separate control-session version strings from firmware-version conclusions before any further live probe");
        }

        if (!snapshot.StrictNoMediaNoPlaybackBoundaryHeld)
        {
            return new MiPlayPostAuthRouteExclusionDecision(
                false,
                "At least one validation exceeded the no-media/no-playback boundary, so it cannot be used to justify a narrower next probe.",
                "restore a clean no-media/no-playback evidence boundary");
        }

        if (!snapshot.MpasSetPlaySourceAckBeforePayloadParseObserved)
        {
            return new MiPlayPostAuthRouteExclusionDecision(
                false,
                "The static ACK-before-payload-parse boundary for Cmd_SetPlaySource 0x0040 has not been proven.",
                "localize a read-only acknowledgement-before-mutation command before using live ACK-only probes");
        }

        if (!AllSetPlaySourceRoutesClosedWithoutAck(snapshot))
        {
            return new MiPlayPostAuthRouteExclusionDecision(
                false,
                "The SetPlaySource route-exclusion matrix is incomplete; not all legacy-clear, old SafetyData, and native-no-reset SafetyData variants have clean outcomes.",
                "complete or discard the incomplete ACK-only evidence before choosing another live frame");
        }

        if (!snapshot.SafetyDataNativeNoResetOfficialJsonUsedSeparatedOutboundProfile)
        {
            return new MiPlayPostAuthRouteExclusionDecision(
                false,
                "The native no-reset 0x0040 validation did not prove that the outbound command cipher was separated from the verified inbound SafetyAuth decrypt candidate.",
                "restore separated inbound-auth and outbound-command state evidence before considering another business frame");
        }

        if (!snapshot.Current19413CommandSessionBridgeLocalized ||
            !snapshot.CandidateFrameTargetsLocalizedBridge ||
            !snapshot.CandidateFrameIsReadOnlyAckBeforeMutation)
        {
            return new MiPlayPostAuthRouteExclusionDecision(
                false,
                "Five SetPlaySource routes now fail before a 0x0041 acknowledgement: legacy clear immediate, legacy clear after state=3 notify, old SafetyData immediately after mutual 0x1403, old SafetyData after a 500 ms post-auth delay, and native-no-reset SafetyData official JSON after mutual SafetyAuth. Because 1.88.51 emits 0x0041 before payload length or JSON parsing, the excluded causes now include missing ref_channel/ref_function/ref_content JSON, immediate post-auth timing, pre-ready notify timing, clear-vs-SafetyData framing alone, and the old promoted-inbound-IV outbound state alone. A new live business probe is not justified until the current 1.94.13 command-session bridge/handler owner, source/session context, and candidate read-only-before-mutation boundary are localized.",
                RemainingOfflineTarget);
        }

        return new MiPlayPostAuthRouteExclusionDecision(
            true,
            "A current 1.94.13 command-session bridge is localized and the candidate targets that bridge with a read-only acknowledgement-before-mutation boundary. A single bounded live verification can be designed, still excluding Cmd_Open, 0x0058, AddMirror, RTSP, media, playback, and audio unless separately authorized by new evidence.",
            "write the exact one-frame live plan and require a fresh pre-send announcement");
    }

    private static bool AllSetPlaySourceRoutesClosedWithoutAck(MiPlayPostAuthRouteExclusionSnapshot snapshot) =>
        snapshot.LegacyClearImmediateSetPlaySourceClosedWithoutAck &&
        snapshot.LegacyClearAfterReadyNotifySetPlaySourceClosedWithoutAck &&
        snapshot.SafetyDataImmediateSetPlaySourceClosedWithoutAck &&
        snapshot.SafetyDataDelayedSetPlaySourceClosedWithoutAck &&
        snapshot.SafetyDataNativeNoResetOfficialJsonClosedWithoutAck;
}