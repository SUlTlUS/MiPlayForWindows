namespace DLNACast.Core.MiPlay;

public sealed record MiPlayPostAuthFirstCommandCandidate(
    string Label,
    ushort Command,
    ushort? AcknowledgementCommand,
    string Route,
    string PayloadShape,
    string CipherProfile,
    bool UsesSafetyData,
    bool LiveTestedOnS12,
    bool AcknowledgementObserved,
    bool DeviceClosedAfterFrame,
    bool SafeForNetworkUse,
    bool AuthorizesNextFrame,
    string Evidence);

public sealed record MiPlayPostAuthFirstCommandCandidateDecision(
    bool CanRepeatNativeNoResetReadOnlyGetDeviceInfo,
    bool CanSendAnotherPostAuthCandidateNow,
    string Reason,
    string NextOfflineTarget);

/// <summary>
/// Cross-cuts the currently known first-command candidates after the S12
/// SafetyAuth boundary. It intentionally keeps the successful legacy clear-text
/// 0x001e path separate from rejected post-auth SafetyData candidates.
/// </summary>
public static class MiPlayPostAuthFirstCommandCandidateMatrixEvidence
{
    public const string LegacyClearGetDeviceInfoLabel = "legacy-clear-getDeviceInfo";
    public const string NativeNoResetGetDeviceInfoLabel = "post-auth-native-no-reset-getDeviceInfo";
    public const string NativeNoResetSetPlaySourceLabel = "post-auth-native-no-reset-setPlaySource";
    public const string NativeNoResetDefaultIdentitySetLocalDeviceInfoLabel =
        "post-auth-native-no-reset-default-identity-setLocalDeviceInfo";
    public const string NativeNoResetRecoveredIdentitySetLocalDeviceInfoLabel =
        "post-auth-native-no-reset-recovered-identity-setLocalDeviceInfo";
    public const string ObservedInboundPromotedSetPlaySourceLabel = "post-auth-observed-inbound-promoted-setPlaySource";
    public const string ForkResetGetDeviceInfoLabel = "post-auth-fork-reset-getDeviceInfo";

    public const string CurrentLx06FirmwareVersion =
        MiPlayPostAuthRouteExclusionEvidence.UserConfirmedCurrentLx06FirmwareVersion;
    public const string ControlSessionVersionAcknowledgement =
        MiPlayPostAuthRouteExclusionEvidence.ObservedControlSessionVersionFrame;

    public static IReadOnlyList<MiPlayPostAuthFirstCommandCandidate> CreateCurrentMatrix() =>
        [
            new(
                Label: LegacyClearGetDeviceInfoLabel,
                Command: MiPlayProtocolConstants.GetDeviceInfoCommand,
                AcknowledgementCommand: MiPlayProtocolConstants.GetDeviceInfoAcknowledgementCommand,
                Route: "legacy clear-text 8899 after decoded state=3 notify",
                PayloadShape: "empty clear-text payload",
                CipherProfile: "none",
                UsesSafetyData: false,
                LiveTestedOnS12: true,
                AcknowledgementObserved: true,
                DeviceClosedAfterFrame: true,
                SafeForNetworkUse: false,
                AuthorizesNextFrame: false,
                Evidence: "LX06 1.94.13 answered one clear-text 0x001e with same-sequence 0x001f; this proves only the legacy read-only route."),
            new(
                Label: NativeNoResetGetDeviceInfoLabel,
                Command: MiPlayProtocolConstants.GetDeviceInfoCommand,
                AcknowledgementCommand: MiPlayProtocolConstants.GetDeviceInfoAcknowledgementCommand,
                Route: "post-auth SafetyData after mutual 0x1402/0x1403",
                PayloadShape: "empty plaintext wrapped as SafetyData",
                CipherProfile: MiPlayPostAuthSafetyDataCipherProfile.NativeNoResetOutboundProfileLabel,
                UsesSafetyData: true,
                LiveTestedOnS12: true,
                AcknowledgementObserved: false,
                DeviceClosedAfterFrame: true,
                SafeForNetworkUse: false,
                AuthorizesNextFrame: false,
                Evidence: "LX06 1.94.13 closed after exactly one native-no-reset SafetyData 0x001e sequence 0x0004; no same-sequence 0x001f was observed."),
            new(
                Label: NativeNoResetSetPlaySourceLabel,
                Command: MiPlayProtocolConstants.SetPlaySourceCommand,
                AcknowledgementCommand: MiPlayProtocolConstants.SetPlaySourceAcknowledgementCommand,
                Route: "post-auth SafetyData after mutual 0x1402/0x1403",
                PayloadShape: "official minimal Android JSON ref_channel/ref_function/ref_content",
                CipherProfile: MiPlayPostAuthSafetyDataCipherProfile.NativeNoResetOutboundProfileLabel,
                UsesSafetyData: true,
                LiveTestedOnS12: true,
                AcknowledgementObserved: false,
                DeviceClosedAfterFrame: true,
                SafeForNetworkUse: false,
                AuthorizesNextFrame: false,
                Evidence: "LX06 1.94.13 closed after one native-no-reset official JSON 0x0040; no 0x0041 was observed."),
            new(
                Label: NativeNoResetDefaultIdentitySetLocalDeviceInfoLabel,
                Command: MiPlayProtocolConstants.SetLocalDeviceInfoCommand,
                AcknowledgementCommand: MiPlayProtocolConstants.SetLocalDeviceInfoAcknowledgementCommand,
                Route: "post-auth SafetyData official-order first frame after mutual 0x1402/0x1403",
                PayloadShape: "default Probe sourceName=DLNACast Windows with empty mSourceBtMac",
                CipherProfile: MiPlayPostAuthSafetyDataCipherProfile.NativeNoResetOutboundProfileLabel,
                UsesSafetyData: true,
                LiveTestedOnS12: true,
                AcknowledgementObserved: false,
                DeviceClosedAfterFrame: true,
                SafeForNetworkUse: false,
                AuthorizesNextFrame: false,
                Evidence: "LX06 1.94.13 closed immediately after the first official-order native-no-reset 0x0058 frame when the payload used the default Windows source identity; no 0x0059, 0x001e, 0x0034, or 0x0040 was sent."),
            new(
                Label: NativeNoResetRecoveredIdentitySetLocalDeviceInfoLabel,
                Command: MiPlayProtocolConstants.SetLocalDeviceInfoCommand,
                AcknowledgementCommand: MiPlayProtocolConstants.SetLocalDeviceInfoAcknowledgementCommand,
                Route: "post-auth SafetyData official-order first frame after mutual 0x1402/0x1403",
                PayloadShape: "recovered official sourceName=Xiaomi 13 Pro with captured 32-character uppercase mSourceBtMac MD5 hash",
                CipherProfile: MiPlayPostAuthSafetyDataCipherProfile.NativeNoResetOutboundProfileLabel,
                UsesSafetyData: true,
                LiveTestedOnS12: true,
                AcknowledgementObserved: false,
                DeviceClosedAfterFrame: true,
                SafeForNetworkUse: false,
                AuthorizesNextFrame: false,
                Evidence: "LX06 1.94.13 closed immediately after the native-no-reset recovered official first 0x0058 source identity frame: plaintext 80 bytes, SafetyData 105 bytes, no 0x0059 observed, and no 0x001e/0x0034/0x0040 was sent."),
            new(
                Label: ObservedInboundPromotedSetPlaySourceLabel,
                Command: MiPlayProtocolConstants.SetPlaySourceCommand,
                AcknowledgementCommand: MiPlayProtocolConstants.SetPlaySourceAcknowledgementCommand,
                Route: "old post-auth SafetyData negative-control path",
                PayloadShape: "empty or official JSON 0x0040 variants",
                CipherProfile: MiPlayPostAuthSafetyDataCipherProfile.ObservedInboundPromotedOutboundProfileLabel,
                UsesSafetyData: true,
                LiveTestedOnS12: true,
                AcknowledgementObserved: false,
                DeviceClosedAfterFrame: true,
                SafeForNetworkUse: false,
                AuthorizesNextFrame: false,
                Evidence: "The old Probe promoted the observed inbound IV workaround to outbound post-auth encryption; 0x0040 variants closed without 0x0041 and are retained only as negative controls."),
            new(
                Label: ForkResetGetDeviceInfoLabel,
                Command: MiPlayProtocolConstants.GetDeviceInfoCommand,
                AcknowledgementCommand: MiPlayProtocolConstants.GetDeviceInfoAcknowledgementCommand,
                Route: "hypothetical DealSafetyDone fork/reset post-auth command session",
                PayloadShape: "empty plaintext wrapped as SafetyData",
                CipherProfile: MiPlayPostAuthSafetyDataStateBoundaryEvidence.DealSafetyDoneForkResetVectorLabel,
                UsesSafetyData: true,
                LiveTestedOnS12: false,
                AcknowledgementObserved: false,
                DeviceClosedAfterFrame: false,
                SafeForNetworkUse: false,
                AuthorizesNextFrame: false,
                Evidence: "No current APK/native or LX06 1.94.13 evidence proves a DealSafetyDone SafetyDataDeal reset/reinstall, so this remains offline-only."),
        ];

    public static MiPlayPostAuthFirstCommandCandidateDecision Evaluate(
        IReadOnlyList<MiPlayPostAuthFirstCommandCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var legacyClear = candidates.SingleOrDefault(candidate =>
            candidate.Label == LegacyClearGetDeviceInfoLabel);
        var nativeNoResetGetDeviceInfo = candidates.SingleOrDefault(candidate =>
            candidate.Label == NativeNoResetGetDeviceInfoLabel);
        var nativeNoResetSetPlaySource = candidates.SingleOrDefault(candidate =>
            candidate.Label == NativeNoResetSetPlaySourceLabel);
        var nativeNoResetDefaultIdentitySetLocalDeviceInfo = candidates.SingleOrDefault(candidate =>
            candidate.Label == NativeNoResetDefaultIdentitySetLocalDeviceInfoLabel);
        var nativeNoResetRecoveredIdentitySetLocalDeviceInfo = candidates.SingleOrDefault(candidate =>
            candidate.Label == NativeNoResetRecoveredIdentitySetLocalDeviceInfoLabel);

        if (legacyClear is null ||
            nativeNoResetGetDeviceInfo is null ||
            nativeNoResetSetPlaySource is null ||
            nativeNoResetDefaultIdentitySetLocalDeviceInfo is null ||
            nativeNoResetRecoveredIdentitySetLocalDeviceInfo is null)
        {
            return new MiPlayPostAuthFirstCommandCandidateDecision(
                false,
                false,
                "The candidate matrix is incomplete; it must include legacy clear 0x001e, post-auth native-no-reset 0x001e, post-auth native-no-reset 0x0040, and first 0x0058 identity evidence.",
                "rebuild the first-command matrix from the latest live evidence before considering any probe");
        }

        if (candidates.Any(candidate => candidate.SafeForNetworkUse || candidate.AuthorizesNextFrame))
        {
            return new MiPlayPostAuthFirstCommandCandidateDecision(
                false,
                false,
                "At least one candidate was incorrectly marked network-safe or next-frame-authorizing. No current first-command candidate has that status.",
                "restore all post-auth SafetyData candidates to offline-only until an official byte vector selects a state");
        }

        if (!legacyClear.AcknowledgementObserved || legacyClear.UsesSafetyData)
        {
            return new MiPlayPostAuthFirstCommandCandidateDecision(
                false,
                false,
                "The successful read-only 0x001e evidence must remain the legacy clear-text route, not a SafetyData route.",
                "keep clear-text receiver context separate from post-auth command-session evidence");
        }

        if (nativeNoResetGetDeviceInfo.AcknowledgementObserved ||
            !nativeNoResetGetDeviceInfo.DeviceClosedAfterFrame ||
            nativeNoResetSetPlaySource.AcknowledgementObserved ||
            !nativeNoResetSetPlaySource.DeviceClosedAfterFrame ||
            nativeNoResetDefaultIdentitySetLocalDeviceInfo.AcknowledgementObserved ||
            !nativeNoResetDefaultIdentitySetLocalDeviceInfo.DeviceClosedAfterFrame ||
            nativeNoResetRecoveredIdentitySetLocalDeviceInfo.AcknowledgementObserved ||
            !nativeNoResetRecoveredIdentitySetLocalDeviceInfo.DeviceClosedAfterFrame)
        {
            return new MiPlayPostAuthFirstCommandCandidateDecision(
                false,
                false,
                "The native-no-reset post-auth result set is not the current negative evidence; do not derive a new probe from a mixed matrix.",
                "reconcile the latest live output before changing probe policy");
        }

        return new MiPlayPostAuthFirstCommandCandidateDecision(
            false,
            false,
            "The current matrix has one accepted legacy clear-text 0x001e route, rejected native-no-reset SafetyData first-command candidates for read-only 0x001e and official JSON 0x0040, and rejected first 0x0058 variants for both default Windows identity and recovered official phone identity. It blocks repeating those variants and blocks any Cmd_Open/AddMirror/RTSP/media/playback/audio follow-up.",
            "recover or prove the official post-auth command-session state transition: outbound SafetyData cipher phase/IV fork, native SafetyDataDeal reset, or missing listener/session context after DealSafetyDone before any further S12 network action");
    }

    public static MiPlayPostAuthFirstCommandCandidateDecision EvaluateCurrent() =>
        Evaluate(CreateCurrentMatrix());
}
