namespace DLNACast.Core.MiPlay;

public sealed record MiPlaySetPlaySourcePayloadSemanticsSnapshot(
    bool Firmware18851ReceiverOnly,
    bool Current19413NativeNoResetOfficialJsonRejected,
    bool ExternalSetPlaySource0040DispatchObserved,
    bool SetPlaySource0041AckBeforePayloadParse,
    bool NonEmptyPayloadRequiresJsonParse,
    bool RefChannelKeyObserved,
    bool RefFunctionKeyObserved,
    bool RefContentKeyObserved,
    bool RefFieldsAssignedAfterParse,
    bool InternalPipeUses005aNotExternal0040,
    bool AndroidAppInfoGeneratedFromCallingUidPackageSignature,
    bool AndroidSignatureIsSha256CertificateFingerprint,
    bool AndroidPlatformTypeIsOne,
    bool AndroidServiceNameMergeStringObserved,
    bool AppInfoPassedToNativeChannelRegistration,
    bool AppInfoPassedToNativeChannelCreation,
    bool ApkNativeDirectLegacySetPlaySourceStringsAbsent,
    bool ApkNativeDirectReceiverRefKeysAbsent,
    bool ApkNativeDirect8899BuilderStringsAbsent,
    bool ApkNativeGenericMiplayTransportSymbolsObserved,
    bool OfficialSender0040BuilderLocalized,
    bool SourceIdentityToLegacy8899BridgeLocalized,
    bool ExternalAddMirror002eUnhandledByServerDispatcher,
    bool AddMirrorAck002fCanRearmCmdOpen,
    bool SenderInfoPreparedCanSendCmdOpen0000,
    bool LocalAddMirrorCanPrecedeLocalCmdOpen,
    bool ForbidLiveNonEmptySetPlaySource,
    bool Forbid0058OpenAddMirrorRtspMediaPlaybackAudio);

public sealed record MiPlaySetPlaySourcePayloadSemanticsDecision(
    bool CanBuildLiveNonEmptySetPlaySource,
    string StaticReceiverConclusion,
    string SourceIdentityConclusion,
    string OfficialOrderConclusion,
    string Boundary);

/// <summary>
/// Offline-only semantic model for LX06 Cmd_SetPlaySource and official Android
/// source identity. It deliberately separates receiver parsing, sender payload
/// construction, source-identity bridging, and live semantic validation.
/// </summary>
public static class MiPlaySetPlaySourcePayloadSemanticsEvidence
{
    public const ushort ExternalSetPlaySourceCommand = 0x0040;
    public const ushort ExternalSetPlaySourceAckCommand = 0x0041;
    public const ushort InternalPipeSetPlaySourceCommand = 0x005a;
    public const ushort CmdAddMirror = 0x002e;
    public const ushort CmdAddMirrorAck = 0x002f;
    public const ushort CmdOpen = 0x0000;

    public const string RefChannelKey = "ref_channel";
    public const string RefFunctionKey = "ref_function";
    public const string RefContentKey = "ref_content";
    public const string ReceiverPayloadShape = "{\"ref_channel\":...,\"ref_function\":...,\"ref_content\":...}";
    public const string LocalAddMirrorPayloadTemplate = "<local-ip>:7236&from:<local-ip>&islocal:1";
    public const string AndroidSourceIdentityShape = "AppInfo(appId, signature, platformType=1, flags) + ServiceName.toMergeString()";
    public const string SignatureAlgorithm = "X509 certificate SHA-256 fingerprint, colon-separated uppercase hex";
    public const string ApkNativeLegacyBridgeScanScope = "Mi Connect Service 5.1.251.10 libmicontinuity.so + libidmservicemgr.so";
    public const string ApkNativeDirectLegacyBridgeNegativeStrings = "ref_channel/ref_function/ref_content/Cmd_SetPlaySource/setPlaySource/Cmd_AddMirror/Cmd_Open/OpenMirror/8899 builder";
    public const string ApkNativeObservedTransportFamily = "lyra::netbus::mpt::MiplayTransport* plus Continuity/NetBus/IDM channel APIs";
    public const string PhoneFirmwareOfficial0040Builder =
        "Mi13P MiLinkOS3Cn classes3.dex StatsUtils.setPlaySource(DeviceManager, Map) -> StatsUtils.ontrackDataToJson(ref_channel, ref_function, ref_content) -> CmdSessionControl.setPlaySource(byte[])";
    public const string Official0040PayloadShape =
        "{\"ref_channel\":\"...\",\"ref_function\":\"...\",\"ref_content\":\"...\"} encoded as UTF-8 JSONObject.putOpt output";

    public static MiPlaySetPlaySourcePayloadSemanticsSnapshot CreateCurrentSnapshot() =>
        new(
            Firmware18851ReceiverOnly: true,
            Current19413NativeNoResetOfficialJsonRejected: true,
            ExternalSetPlaySource0040DispatchObserved: true,
            SetPlaySource0041AckBeforePayloadParse: true,
            NonEmptyPayloadRequiresJsonParse: true,
            RefChannelKeyObserved: true,
            RefFunctionKeyObserved: true,
            RefContentKeyObserved: true,
            RefFieldsAssignedAfterParse: true,
            InternalPipeUses005aNotExternal0040: true,
            AndroidAppInfoGeneratedFromCallingUidPackageSignature: true,
            AndroidSignatureIsSha256CertificateFingerprint: true,
            AndroidPlatformTypeIsOne: true,
            AndroidServiceNameMergeStringObserved: true,
            AppInfoPassedToNativeChannelRegistration: true,
            AppInfoPassedToNativeChannelCreation: true,
            ApkNativeDirectLegacySetPlaySourceStringsAbsent: true,
            ApkNativeDirectReceiverRefKeysAbsent: true,
            ApkNativeDirect8899BuilderStringsAbsent: true,
            ApkNativeGenericMiplayTransportSymbolsObserved: true,
            OfficialSender0040BuilderLocalized: true,
            SourceIdentityToLegacy8899BridgeLocalized: false,
            ExternalAddMirror002eUnhandledByServerDispatcher: true,
            AddMirrorAck002fCanRearmCmdOpen: true,
            SenderInfoPreparedCanSendCmdOpen0000: true,
            LocalAddMirrorCanPrecedeLocalCmdOpen: true,
            ForbidLiveNonEmptySetPlaySource: true,
            Forbid0058OpenAddMirrorRtspMediaPlaybackAudio: true);

    public static MiPlaySetPlaySourcePayloadSemanticsDecision Evaluate(
        MiPlaySetPlaySourcePayloadSemanticsSnapshot snapshot)
    {
        if (!snapshot.ExternalSetPlaySource0040DispatchObserved ||
            !snapshot.SetPlaySource0041AckBeforePayloadParse ||
            !snapshot.NonEmptyPayloadRequiresJsonParse ||
            !snapshot.RefChannelKeyObserved ||
            !snapshot.RefFunctionKeyObserved ||
            !snapshot.RefContentKeyObserved ||
            !snapshot.RefFieldsAssignedAfterParse)
        {
            return new MiPlaySetPlaySourcePayloadSemanticsDecision(
                false,
                "Receiver-side non-empty Cmd_SetPlaySource parsing is incomplete.",
                "No source identity conclusion can be made until receiver parsing is stable.",
                "No official order can be derived from incomplete receiver evidence.",
                "Keep all live non-empty 0x0040/open/media probes forbidden.");
        }

        if (!snapshot.AndroidAppInfoGeneratedFromCallingUidPackageSignature ||
            !snapshot.AndroidSignatureIsSha256CertificateFingerprint ||
            !snapshot.AndroidPlatformTypeIsOne ||
            !snapshot.AndroidServiceNameMergeStringObserved ||
            !snapshot.AppInfoPassedToNativeChannelRegistration ||
            !snapshot.AppInfoPassedToNativeChannelCreation)
        {
            return new MiPlaySetPlaySourcePayloadSemanticsDecision(
                false,
                "LX06 1.88.51 receiver-side 0x0040 parses ref_channel/ref_function/ref_content after an ACK-before-parse boundary.",
                "Official Android source identity is not fully localized.",
                "0x0040/AddMirror/Open order remains receiver-local only.",
                "Do not build a live non-empty 0x0040 payload.");
        }

        var receiverConclusion =
            "LX06 1.88.51 mpas dispatches external Cmd_SetPlaySource as 0x0040, sends 0x0041 before payload presence/JSON parsing, and only the non-empty path parses ref_channel/ref_function/ref_content before assigning receiver source-reference fields. Its pipe helper uses 0x005a, not external 0x0040.";
        var sourceConclusion =
            "Mi Connect APK 5.1.251.10 builds official source identity from Binder UID/PID/package: PackageUtil.generateAppInfo selects appId, computes a SHA-256 certificate fingerprint signature, sets platformType=1 and package flags, while ServiceName.toMergeString serializes package:name or :name. That AppInfo is passed to native channel register/create calls. The Mi13P phone firmware localizes the official source-side 0x0040 payload builder in MiLinkOS3Cn: StatsUtils.setPlaySource walks devices, calls ontrackDataToJson(ref_channel, ref_function, ref_content), and passes the resulting UTF-8 JSONObject bytes to CmdSessionControl.setPlaySource(byte[]). A direct scan of the earlier Mi Connect native libmicontinuity.so/libidmservicemgr.so did not locate an AppInfo/ServiceName-to-legacy-8899 bridge; observed native symbols stay in Continuity/NetBus/IDM and generic MiplayTransport/KCP infrastructure.";
        var orderConclusion =
            "The localized receiver order is: 0x0040 SetPlaySource can mutate source-reference fields; receiver-local AddMirror is emitted as 0x002e and expects 0x002f Ack before re-arming master Cmd_Open; sender-info-prepared/local paths can send Cmd_Open 0x0000. External incoming 0x002e is unhandled by ServerApp, so direct AddMirror is a role/direction error. The official external-source order is not complete until the source-identity-to-8899 bridge and 0x0040 state transition are found.";

        if (!snapshot.ApkNativeDirectLegacySetPlaySourceStringsAbsent ||
            !snapshot.ApkNativeDirectReceiverRefKeysAbsent ||
            !snapshot.ApkNativeDirect8899BuilderStringsAbsent)
        {
            return new MiPlaySetPlaySourcePayloadSemanticsDecision(
                false,
                receiverConclusion,
                "The APK native direct legacy bridge scan changed; inspect the newly observed strings before designing any probe.",
                orderConclusion,
                "Treat the bridge evidence as unstable until the new native hits are classified.");
        }

        if (!snapshot.OfficialSender0040BuilderLocalized ||
            !snapshot.SourceIdentityToLegacy8899BridgeLocalized ||
            snapshot.Current19413NativeNoResetOfficialJsonRejected ||
            snapshot.ForbidLiveNonEmptySetPlaySource ||
            !snapshot.Forbid0058OpenAddMirrorRtspMediaPlaybackAudio)
        {
            return new MiPlaySetPlaySourcePayloadSemanticsDecision(
                false,
                receiverConclusion,
                sourceConclusion,
                orderConclusion,
                "Static receiver structure and the official source-side 0x0040 JSON builder are localized, and LX06 1.94.13 has now rejected exactly one native-no-reset official minimal JSON 0x0040 without returning 0x0041. The remaining missing proof is no longer merely payload shape or old promoted-IV state; it is the AppInfo/ServiceName/source-context bridge into the legacy 8899 command session, the exact command ordering/state transition before 0x0040, or current 1.94.13 handler ownership. Do not send non-empty 0x0040, 0x0058, Cmd_Open, AddMirror, RTSP, media, playback, or audio.");
        }

        return new MiPlaySetPlaySourcePayloadSemanticsDecision(
            true,
            receiverConclusion,
            sourceConclusion,
            orderConclusion,
            "Only after the bridge/ordering/handler state is localized and a new read-only-before-mutation candidate exists could a separate plan and fresh authorization be considered.");
    }
}