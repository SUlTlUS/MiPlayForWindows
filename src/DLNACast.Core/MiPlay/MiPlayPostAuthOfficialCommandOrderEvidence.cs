namespace DLNACast.Core.MiPlay;

public sealed record MiPlayPostAuthOfficialCommandOrderSnapshot(
    bool PhoneFirmwareDexCmdSessionControlLocalized,
    bool ConnectCmdSessionCreatesNativeCmdHandler,
    bool ControlMethodsShareCmdHandlerAndSessionType,
    bool StartCommandChannelCallsConnectCmdSession,
    bool CmdSessionSuccessCallsGetDeviceInfo,
    bool DeviceRefreshCanCallGetDeviceInfoAgain,
    bool SetPlaySourceCalledFromStatsOnPlayOrActiveSessionChange,
    bool SetPlaySourceUsesDeviceRefMapsAndCmdSessionControlMap,
    bool SetPlaySourceIsNotCalledDirectlyByCmdSessionSuccess,
    bool OpenDeviceHasSeparateControlEntrypoints,
    bool NativeCmdSourceSendClusterObserved,
    bool CommandNameMapAlignsGetDeviceInfoSetPlaySourceAddMirrorOpen,
    bool RootTcpdumpRuntimeOrderObserved,
    bool RuntimeOrderIncludesLocalDeviceInfoBeforeGetDeviceInfo,
    bool RuntimeOrderIncludesGetMirrorModeBeforeSetPlaySource,
    bool CurrentMilinkNativeIdentifiesGetMirrorModePair,
    bool RuntimeSetPlaySourceContinuesHeartbeatWithout0041InWindow,
    bool NativeNoResetOfficialJsonSetPlaySourceRejected,
    bool CurrentProbeSkippedOfficialGetDeviceInfoReadyContext,
    bool NoNetworkOperationPerformed);

public sealed record MiPlayPostAuthOfficialCommandOrderDecision(
    bool CanTreatImmediatePostAuthSetPlaySourceAsOfficial,
    bool CanDesignNextReadOnlyDeviceInfoGate,
    string Reason,
    string NextOfflineTarget);

/// <summary>
/// Offline-only model for the source-side command order recovered from the
/// Mi13P OS3 phone firmware. It distinguishes the native CmdSessionControl
/// handle lifecycle from later 0x0040 source-statistics events and does not
/// authorize any network frame by itself.
/// </summary>
public static class MiPlayPostAuthOfficialCommandOrderEvidence
{
    public const string PhoneFirmwareScope =
        "D:/17系稳定版Pro_260602_Mi13P_OS3.0.313_92ed product_a MiLinkOS3Cn/MirrorOS3 artifacts";

    public const string CmdSessionXrefArtifact =
        "artifacts/phone_firmware/mi13p_os3_0_313/phone_source_dex_cmdsession_xrefs.json";

    public const string RefIdentityTraceArtifact =
        "artifacts/phone_firmware/mi13p_os3_0_313/phone_source_all_dex_ref_identity_trace.json";

    public const string CommandNameMapArtifact =
        "artifacts/phone_firmware/mi13p_os3_0_313/mirroros3_command_name_map.json";

    public const int CmdSessionControlConnectCmdSessionJavaCodeOffset = 0x294780;
    public const int CmdSessionControlCreateCmdSessionJavaCodeOffset = 0x294900;
    public const int CmdSessionControlGetDeviceInfoJavaCodeOffset = 0x295014;
    public const int CmdSessionControlOpenDeviceJavaCodeOffset = 0x295460;
    public const int CmdSessionControlSetPlaySourceJavaCodeOffset = 0x295C84;

    public const int MiPlayAudioServiceStartCommandChannelCodeOffset = 0x27A8AC;
    public const int MiPlayAudioServiceCmdSessionSuccessCodeOffset = 0x278360;
    public const int MiPlayAudioServiceOnTopActiveSessionChangeCodeOffset = 0x279F94;
    public const int MiplayMultiDisplayManageOnPlayCodeOffset = 0x2B0B40;
    public const int MiplaySessionCtrProxyOnRefreshDeviceInfoCodeOffset = 0x2B63E8;
    public const int StatsUtilsSetPlaySourceCodeOffset = 0x2C1988;

    public const long ProductNativeCmdSourceClusterOffset = 0x31712588;
    public const long ProductNativeLocalCmdControlClusterOffset = 0x83F7008F;

    public const string NativeCmdSourceStringCluster =
        "CmdSource/getCmdData/sendPayload/sendAuthAck/DealSafetyAuth/connectCmdSession/createCmdSession";

    public const string OfficialPostConnectReadOnlyOrder =
        "startCommandChannel -> CmdSessionControl.connectCmdSession -> cmdSessionSuccess -> CmdSessionControl.getDeviceInfo";

    public const string OfficialSetPlaySourceEventOrder =
        "onTopActiveSessionChange or MiplayMultiDisplayManage.onPlay -> StatsUtils.setPlaySource -> CmdSessionControl.setPlaySource(byte[])";

    public const string CommandIdAlignment =
        "0x001e GetDeviceInfo, 0x001f GetDeviceInfo_Ack, 0x0034 GetMirrorMode, 0x0035 GetMirrorMode_Ack, 0x0040 SetPlaySource, 0x0041 SetPlaySource_Ack, 0x002e AddMirror, 0x002f AddMirror_Ack, 0x0000 Open";

    public const string CurrentNegativeBoundary =
        "LX06 1.94.13 accepted mutual SafetyAuth but closed after one native-no-reset official JSON 0x0040 without 0x0041";

    public const string CurrentGap =
        "the Probe has not reproduced the official getDeviceInfo success/ready context that precedes later source-statistics SetPlaySource events";

    public const string RuntimeRootTcpdumpArtifact =
        "artifacts/phone_live/2210132C_OS3.0.313.0/root-captures/miplay-root-8899-reconnect-20260726-122421.pcap";

    public const string RuntimePostAuthOrder =
        "0x0058 -> 0x001e -> 0x0059 -> more 0x0058 -> 0x001f -> 0x0034/0x0035 GetMirrorMode/GetMirrorMode_Ack -> more 0x0058/0x0059 -> heartbeat -> 0x0040 -> heartbeat";

    public static MiPlayPostAuthOfficialCommandOrderSnapshot CreateCurrentSnapshot() =>
        new(
            PhoneFirmwareDexCmdSessionControlLocalized: true,
            ConnectCmdSessionCreatesNativeCmdHandler: true,
            ControlMethodsShareCmdHandlerAndSessionType: true,
            StartCommandChannelCallsConnectCmdSession: true,
            CmdSessionSuccessCallsGetDeviceInfo: true,
            DeviceRefreshCanCallGetDeviceInfoAgain: true,
            SetPlaySourceCalledFromStatsOnPlayOrActiveSessionChange: true,
            SetPlaySourceUsesDeviceRefMapsAndCmdSessionControlMap: true,
            SetPlaySourceIsNotCalledDirectlyByCmdSessionSuccess: true,
            OpenDeviceHasSeparateControlEntrypoints: true,
            NativeCmdSourceSendClusterObserved: true,
            CommandNameMapAlignsGetDeviceInfoSetPlaySourceAddMirrorOpen: true,
            RootTcpdumpRuntimeOrderObserved: true,
            RuntimeOrderIncludesLocalDeviceInfoBeforeGetDeviceInfo: true,
            RuntimeOrderIncludesGetMirrorModeBeforeSetPlaySource: true,
            CurrentMilinkNativeIdentifiesGetMirrorModePair: true,
            RuntimeSetPlaySourceContinuesHeartbeatWithout0041InWindow: true,
            NativeNoResetOfficialJsonSetPlaySourceRejected: true,
            CurrentProbeSkippedOfficialGetDeviceInfoReadyContext: true,
            NoNetworkOperationPerformed: true);

    public static MiPlayPostAuthOfficialCommandOrderDecision Evaluate(
        MiPlayPostAuthOfficialCommandOrderSnapshot snapshot)
    {
        if (!snapshot.NoNetworkOperationPerformed)
        {
            return new MiPlayPostAuthOfficialCommandOrderDecision(
                false,
                false,
                "This evidence must remain offline-only; a network operation would invalidate the current static boundary.",
                "restore an offline-only evidence boundary before changing probe policy");
        }

        if (!snapshot.PhoneFirmwareDexCmdSessionControlLocalized ||
            !snapshot.NativeCmdSourceSendClusterObserved ||
            !snapshot.CommandNameMapAlignsGetDeviceInfoSetPlaySourceAddMirrorOpen)
        {
            return new MiPlayPostAuthOfficialCommandOrderDecision(
                false,
                false,
                "The phone-firmware command-session evidence is incomplete, so official ordering cannot be inferred.",
                "localize CmdSessionControl wrappers, native CmdSource send strings, and command-name constants in the same phone-firmware build");
        }

        if (!snapshot.ConnectCmdSessionCreatesNativeCmdHandler ||
            !snapshot.ControlMethodsShareCmdHandlerAndSessionType)
        {
            return new MiPlayPostAuthOfficialCommandOrderDecision(
                false,
                false,
                "CmdSessionControl command wrappers are not proven to share the same native cmdHandler/sessionType state.",
                "recover the Java wrapper field loads and native handler ownership before any live command candidate");
        }

        if (!snapshot.CmdSessionSuccessCallsGetDeviceInfo)
        {
            return new MiPlayPostAuthOfficialCommandOrderDecision(
                false,
                false,
                "The official command-session success path is not proven to issue getDeviceInfo first.",
                "trace cmdSessionSuccess before interpreting post-auth command order");
        }

        if (!snapshot.SetPlaySourceCalledFromStatsOnPlayOrActiveSessionChange ||
            !snapshot.SetPlaySourceUsesDeviceRefMapsAndCmdSessionControlMap)
        {
            return new MiPlayPostAuthOfficialCommandOrderDecision(
                false,
                false,
                "The SetPlaySource source-event context is not localized; do not treat a minimal 0x0040 as an official source event.",
                "trace StatsUtils.setPlaySource callers and cmdSessionControlMap access");
        }

        if (snapshot.RootTcpdumpRuntimeOrderObserved)
        {
            if (!snapshot.RuntimeOrderIncludesLocalDeviceInfoBeforeGetDeviceInfo ||
                !snapshot.RuntimeOrderIncludesGetMirrorModeBeforeSetPlaySource ||
                !snapshot.CurrentMilinkNativeIdentifiesGetMirrorModePair ||
                !snapshot.RuntimeSetPlaySourceContinuesHeartbeatWithout0041InWindow)
            {
                return new MiPlayPostAuthOfficialCommandOrderDecision(
                    false,
                    false,
                    "The root tcpdump/runtime-native order is present but missing one of the observed readiness/context facts.",
                    "re-parse the rooted phone pcap and native com.milink.service command-session evidence, then separate local-device-info, getDeviceInfo, GetMirrorMode, and SetPlaySource order");
            }

            return new MiPlayPostAuthOfficialCommandOrderDecision(
                false,
                false,
                $"Immediate post-auth SetPlaySource is not the official order, and standalone post-auth getDeviceInfo is also too narrow. The rooted official sender sequence is {RuntimePostAuthOrder}. Because {CurrentNegativeBoundary}, the next target is offline byte/state recovery for that full sequence, not another generated 0x001e, 0x0040, 0x0058, Open, AddMirror, RTSP, media, playback, or audio probe.",
                "recover the matching bootstrap/session SafetyData state and plaintext semantics for official 0x0058/0x001e/0x001f/0x0034/0x0035 GetMirrorMode/0x0040 before any new live plan");
        }

        if (snapshot.NativeNoResetOfficialJsonSetPlaySourceRejected &&
            snapshot.CurrentProbeSkippedOfficialGetDeviceInfoReadyContext)
        {
            return new MiPlayPostAuthOfficialCommandOrderDecision(
                false,
                true,
                $"Immediate post-auth SetPlaySource is not the official order. The recovered source path is {OfficialPostConnectReadOnlyOrder}; {OfficialSetPlaySourceEventOrder}. Because {CurrentNegativeBoundary}, the next candidate should be a read-only getDeviceInfo/0x001f ready-context gate, not another 0x0040/Open/AddMirror/media frame.",
                "recover byte-level current-firmware 0x001e/0x001f SafetyData readiness semantics and listener state before any mutating command");
        }

        return new MiPlayPostAuthOfficialCommandOrderDecision(
            false,
            false,
            "The official source order is not yet strict enough to plan a live probe.",
            "separate read-only getDeviceInfo readiness from mutating SetPlaySource/Open/AddMirror paths");
    }
}
