namespace DLNACast.Core.MiPlay;

public sealed record MiPlaySourceIdentityContextBoundarySnapshot(
    bool LegacyClearGetDeviceInfoAcknowledged,
    bool LegacyDeviceInfoPayloadParsed,
    bool TargetModelObserved,
    bool TargetRomVersionObserved,
    bool TargetAudioSupportObserved,
    bool LocalSetLocalDeviceInfoJsonShapeAvailable,
    bool AndroidAppInfoAvailable,
    bool AndroidServiceNameAvailable,
    bool AndroidSignatureAvailable,
    bool AllExtractedPhoneDexIdentityTraceBuilt,
    bool PackageUtilAppInfoGenerationRecovered,
    bool PackageSignatureSha256FingerprintRecovered,
    bool ServiceNameMergeStringRecovered,
    bool AppInfoServiceNameCmdSessionControlAllDexIntersectionEmpty,
    bool SetPlaySourcePayloadShapeLocalized,
    bool OfficialSetPlaySourcePayloadBuilderLocalized,
    bool SourceIdentityToLegacy8899BridgeLocalized,
    bool NativeNoResetOfficialJsonSetPlaySourceRejected,
    bool SourceContextOrOrderingAfterSetPlaySourceUnresolved,
    bool CandidatePayloadMutatesSourceIdentity,
    bool Forbid0058,
    bool ForbidCmdOpen,
    bool ForbidAddMirror,
    bool ForbidRtspMediaPlaybackOrAudio);

public sealed record MiPlaySourceIdentityContextBoundaryDecision(
    bool CanDesignLiveSetPlaySourceProbe,
    string Reason,
    string NextOfflineTarget);

/// <summary>
/// Separates the target device context proven by legacy clear 0x001e/0x001f
/// from the source identity context required by later MiPlay business commands.
/// This class is intentionally offline-only: it neither builds nor authorizes
/// any 0x0040/0x0058/Cmd_Open/AddMirror/media frame.
/// </summary>
public static class MiPlaySourceIdentityContextBoundaryEvidence
{
    public const string ProvenTargetContext =
        "legacy clear 0x001e returned 0x001f with model=LX06, romVersion=1.94.13, support=audio";
    public const string Local0058JsonScope =
        "sourceName/mSourceBtMac/canAlonePlayCtrl/canHeadsetCtrl and model/romVersion/appVersion JSON only";
    public const string MissingAndroidSourceIdentity =
        "AppInfo(appId, signature, platformType=1, flags) plus ServiceName.toMergeString()";
    public const string AllDexIdentityTraceArtifact =
        @"artifacts\phone_firmware\mi13p_os3_0_313\phone_source_all_dex_ref_identity_trace.json";
    public const string MissingSetPlaySourcePayloadShape =
        "the source-identity/AppInfo bridge into the legacy 8899 session after the official Cmd_SetPlaySource 0x0040 JSON builder";
    public const string NextOfflineTarget =
        "recover the official source-identity-to-legacy-8899 bridge plus the exact command ordering/session state transition around 0x0040 before any next business-frame probe";
    public const string LocalizedAndroidSourceIdentity =
        "PackageUtil.generateAppInfo(Context, uid, pid, invokePkg) builds AppInfo from appId/signature/platformType=1/flags, and ServiceName.toMergeString() serializes package:name or :name";
    public const string LocalizedPackageSignature =
        "PackageUtil.getSignature reads PackageInfo.signingInfo.getApkContentsSigners()[0].toByteArray(), wraps it as X509 certificate data, hashes Certificate.getEncoded() with SHA-256, and formats uppercase colon-separated hex";
    public const string LocalizedAppInfoGeneration =
        "PackageUtil.generateAppInfo builds AppInfo with appId=package name, signature=getSignature(package), platformType=1, and flags=getPackageFlags(package); generateCustomAppInfo uses platformType=1, empty signature, flags=0";
    public const string LocalizedServiceNameMergeString =
        "ServiceName.toMergeString builds packageName:name when packageName is present, otherwise :name";
    public const string AllDexIdentityIntersectionEvidence =
        "full extracted MiLinkOS3Cn+MirrorOS3 DEX trace: CmdSessionControl referrers=206, AppInfo referrers=29, ServiceName referrers=334, Cmd/AppInfo=0, Cmd/ServiceName=0, Cmd/signature=0";
    public const string LocalizedReceiverSetPlaySourcePayload =
        "LX06 1.88.51 mpas external 0x0040 sends 0x0041 before payload parsing, then parses JSON keys ref_channel/ref_function/ref_content";
    public const string LocalizedOfficialSetPlaySourceBuilder =
        "Mi13P MiLinkOS3Cn StatsUtils.setPlaySource builds UTF-8 JSONObject.putOpt bytes for ref_channel/ref_function/ref_content and passes them to CmdSessionControl.setPlaySource(byte[])";
    public const string NativeNoResetOfficialJsonNegativeResult =
        "LX06 1.94.13 accepted mutual SafetyAuth but closed after exactly one native-no-reset official JSON 0x0040 without 0x0041";

    public static MiPlaySourceIdentityContextBoundarySnapshot CreateCurrentSnapshot() =>
        new(
            LegacyClearGetDeviceInfoAcknowledged: true,
            LegacyDeviceInfoPayloadParsed: true,
            TargetModelObserved: true,
            TargetRomVersionObserved: true,
            TargetAudioSupportObserved: true,
            LocalSetLocalDeviceInfoJsonShapeAvailable: true,
            AndroidAppInfoAvailable: true,
            AndroidServiceNameAvailable: true,
            AndroidSignatureAvailable: true,
            AllExtractedPhoneDexIdentityTraceBuilt: true,
            PackageUtilAppInfoGenerationRecovered: true,
            PackageSignatureSha256FingerprintRecovered: true,
            ServiceNameMergeStringRecovered: true,
            AppInfoServiceNameCmdSessionControlAllDexIntersectionEmpty: true,
            SetPlaySourcePayloadShapeLocalized: true,
            OfficialSetPlaySourcePayloadBuilderLocalized: true,
            SourceIdentityToLegacy8899BridgeLocalized: false,
            NativeNoResetOfficialJsonSetPlaySourceRejected: true,
            SourceContextOrOrderingAfterSetPlaySourceUnresolved: true,
            CandidatePayloadMutatesSourceIdentity: true,
            Forbid0058: true,
            ForbidCmdOpen: true,
            ForbidAddMirror: true,
            ForbidRtspMediaPlaybackOrAudio: true);

    public static MiPlaySourceIdentityContextBoundaryDecision EvaluateNextSetPlaySourceProbe(
        MiPlaySourceIdentityContextBoundarySnapshot snapshot)
    {
        if (!snapshot.LegacyClearGetDeviceInfoAcknowledged ||
            !snapshot.LegacyDeviceInfoPayloadParsed)
        {
            return new MiPlaySourceIdentityContextBoundaryDecision(
                false,
                "The target receiver context has not been proven by a parsed legacy clear 0x001f payload.",
                "complete the read-only legacy getDeviceInfo validation first");
        }

        if (!snapshot.TargetModelObserved ||
            !snapshot.TargetRomVersionObserved ||
            !snapshot.TargetAudioSupportObserved)
        {
            return new MiPlaySourceIdentityContextBoundaryDecision(
                false,
                "The parsed 0x001f payload does not yet prove the target model, ROM version, and audio support fields.",
                "finish target device-info parsing before considering any identity command");
        }

        if (!snapshot.LocalSetLocalDeviceInfoJsonShapeAvailable)
        {
            return new MiPlaySourceIdentityContextBoundaryDecision(
                false,
                "The local 0x0058 JSON shape is not available even as an offline model.",
                "keep 0x0058 forbidden and recover its JSON shape offline");
        }

        if (!snapshot.AndroidAppInfoAvailable ||
            !snapshot.AndroidServiceNameAvailable ||
            !snapshot.AndroidSignatureAvailable ||
            !snapshot.AllExtractedPhoneDexIdentityTraceBuilt ||
            !snapshot.PackageUtilAppInfoGenerationRecovered ||
            !snapshot.PackageSignatureSha256FingerprintRecovered ||
            !snapshot.ServiceNameMergeStringRecovered ||
            !snapshot.AppInfoServiceNameCmdSessionControlAllDexIntersectionEmpty)
        {
            return new MiPlaySourceIdentityContextBoundaryDecision(
                false,
                "The successful 0x001f target context does not provide the source-side Android AppInfo, ServiceName, or signature material used by the official client. The Windows 0x0058 JSON shape is not equivalent to AppInfo + ServiceName.",
                NextOfflineTarget);
        }

        if (!snapshot.SetPlaySourcePayloadShapeLocalized)
        {
            return new MiPlaySourceIdentityContextBoundaryDecision(
                false,
                "The receiver-side non-empty external Cmd_SetPlaySource 0x0040 payload shape has not been localized, and this command can mutate source identity instead of remaining read-only.",
                NextOfflineTarget);
        }

        if (!snapshot.OfficialSetPlaySourcePayloadBuilderLocalized)
        {
            return new MiPlaySourceIdentityContextBoundaryDecision(
                false,
                "The receiver-side 0x0040 shape is known, but the official sender-side payload builder is not localized.",
                NextOfflineTarget);
        }

        if (!snapshot.SourceIdentityToLegacy8899BridgeLocalized)
        {
            return new MiPlaySourceIdentityContextBoundaryDecision(
                false,
                "Static evidence localizes Android AppInfo, ServiceName.toMergeString(), package-signature SHA-256, the LX06 receiver-side 0x0040 ref_channel/ref_function/ref_content parser, and the Mi13P source-side JSON builder. A full extracted MiLinkOS3Cn+MirrorOS3 DEX trace found no caller intersection between CmdSessionControl and AppInfo/ServiceName/signature. The checked native connectCmdSession2 bridge explains only optional Lyra key JSON through setLyraInfo, not AppInfo/ServiceName in legacy 8899. After the native-no-reset official JSON 0x0040 negative result, the missing proof is still source/session context or ordering, not another minimal payload guess.",
                NextOfflineTarget);
        }

        if (snapshot.NativeNoResetOfficialJsonSetPlaySourceRejected &&
            snapshot.SourceContextOrOrderingAfterSetPlaySourceUnresolved)
        {
            return new MiPlaySourceIdentityContextBoundaryDecision(
                false,
                "The source identity bridge alone is no longer enough to justify another 0x0040: native-no-reset official JSON already closed without 0x0041. The exact command ordering, session context, or post-0x0040 state transition must be localized before a new business-frame candidate can be considered.",
                "localize official command ordering/session context after SafetyAuth, including the preconditions around 0x0040 before AddMirror/Open/media");
        }

        if (snapshot.CandidatePayloadMutatesSourceIdentity &&
            (!snapshot.Forbid0058 ||
             !snapshot.ForbidCmdOpen ||
             !snapshot.ForbidAddMirror ||
             !snapshot.ForbidRtspMediaPlaybackOrAudio))
        {
            return new MiPlaySourceIdentityContextBoundaryDecision(
                false,
                "The candidate expands beyond a single bounded identity probe and crosses into 0x0058/open/AddMirror/RTSP/media territory.",
                "restore the no-media/no-open boundary before writing a live plan");
        }

        return new MiPlaySourceIdentityContextBoundaryDecision(
            true,
            "Source AppInfo, ServiceName/signature, the receiver-side 0x0040 payload shape, the sender-side 0x0040 builder, the 8899 bridge, and the post-SafetyAuth command ordering/session context are localized while 0x0058, Cmd_Open, AddMirror, RTSP, media, playback, and audio remain forbidden. A separate pre-announced one-frame validation plan can be written.",
            "write an exact one-frame live plan and require a fresh pre-send announcement");
    }
}