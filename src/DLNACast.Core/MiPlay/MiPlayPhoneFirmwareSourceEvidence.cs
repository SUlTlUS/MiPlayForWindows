namespace DLNACast.Core.MiPlay;

public sealed record MiPlayPhoneFirmwareSourceSnapshot(
    bool FirmwareDirectoryCataloged,
    bool LogicalProductAndSystemExtPartitionsExtracted,
    bool MinimalErofsDirectoryIndexBuilt,
    bool MirrorOs3CandidateLocalized,
    bool MiLinkOs3CandidateLocalized,
    bool CmdSessionControlJniObserved,
    bool CmdSourceAndCmdControlSymbolsObserved,
    bool CreateCmdSessionAddrPortObserved,
    bool SendOpenDeviceLogObserved,
    bool SafetyAuthAckObserved,
    bool CmdAuthObserved,
    bool SafetyKeyDealAuthKeyAndAesIvObserved,
    bool CmdTypeAndAckLoggingObserved,
    bool NativeControlVersionObserved,
    bool PhoneFirmwareSourceSideLegacyStackObserved,
    bool CandidateFilesExtractedFromErofs,
    bool ApkZipIntegrityVerified,
    bool DexCmdSessionXrefsRecovered,
    bool OfficialSender0040BuilderLocalized,
    bool AppInfoServiceNameToLegacy8899BridgeLocalized,
    bool WireCommandIdsRecoveredFromPhoneFirmware,
    bool ErofsLayout3CompressedFilesRemainUnextracted,
    bool NoSpeakerOrLanOperationPerformed,
    bool Forbid0058OpenAddMirrorRtspMediaPlaybackAudio);

public sealed record MiPlayPhoneFirmwareSourceDecision(
    bool CanDesignLiveBusinessProbe,
    bool CanBuildNonEmptySetPlaySource,
    string StaticSourceConclusion,
    string MissingProof,
    string Boundary);

/// <summary>
/// Offline evidence from the Mi 13 Pro HyperOS phone firmware. This captures
/// source-side legacy MiPlay command-session evidence without promoting it to
/// live probe authorization.
/// </summary>
public static class MiPlayPhoneFirmwareSourceEvidence
{
    public const string FirmwareSourceDirectory =
        @"D:\17系稳定版Pro_260602_Mi13P_OS3.0.313_92ed";
    public const string ArtifactDirectory =
        @"artifacts\phone_firmware\mi13p_os3_0_313";
    public const string ProductPartition = "product_a.img";
    public const string SystemExtPartition = "system_ext_a.img";

    public const string MirrorOs3Candidate =
        "product_a:/priv-app/MirrorOS3/MirrorOS3.apk + oat/arm64/MirrorOS3.vdex/odex";
    public const string MiLinkOs3Candidate =
        "product_a:/app/MiLinkOS3Cn/MiLinkOS3Cn.apk + oat/arm64/MiLinkOS3Cn.vdex/odex";
    public const string MediCastIoCandidate =
        "system_ext_a:/app/MiuiAudioMonitor/lib/arm64/libCastSdk-jni.so plus com.xiaomi.miplay.ipc_binder/mediacastio strings";

    public const string CmdSessionControlJniContext =
        "product_a raw strings localize Java_com_xiaomi_miplay_mylibrary_mirror_CmdSessionControl_* and com/xiaomi/miplay/mylibrary/mirror/CmdSessionControl";
    public const string SourceCommandSessionContext =
        "product_a+0x83f700c4 contains createCmdSession addr/port:%d, send openDevice %.*s, getVersion:%s 3.2.5121919, cmdType:%s, isAck, authUsedTime, and DealSafetyD";
    public const string SourceControlSymbolContext =
        "product_a+0x310ee47d and +0x31711df9 contain mirror::CmdControl/openDevice, CmdSource, getCmdNameFromCode, ParseDataMsg, AES_CBC_decrypt_buffer, SafetyKeyDeal::genAuthKey, and genAesIv";
    public const string SafetyAuthContext =
        "product_a+0x31123880/+0x3174d31b contain Cmd_SafetyAuth_Ack; product_a+0x31130659/+0x3175b875 contain Cmd_Auth";
    public const string OpenAckNameTableContext =
        "product_a+0x83f70d8e contains Open_Ack/HeartBeat/Add/Del/Info/Safety/Auth-style command-name strings, but this string table is not numeric wire-ID proof";
    public const string ExtractedCandidateFiles =
        "MirrorOS3.apk/vdex/odex and MiLinkOS3Cn.apk/vdex/odex extracted with scripts/extract-erofs-files.py; APK ZipFile.testzip() passed";
    public const string RecoveredWireCommandIds =
        "MirrorOS3 libmirror-jni.so getCmdNameFromCode maps 0x0040=SetPlaySource, 0x0041=SetPlaySource_Ack, 0x001e=GetDeviceInfo, 0x001f=GetDeviceInfo_Ack, 0x0028=Auth, 0x0029=Auth_Ack, 0x1402=SafetyAuth, 0x1403=SafetyAuth_Ack";
    public const string OfficialSetPlaySourceBuilder =
        "MiLinkOS3Cn classes3.dex StatsUtils.setPlaySource(DeviceManager, Map) calls StatsUtils.ontrackDataToJson(ref_channel, ref_function, ref_content) and CmdSessionControl.setPlaySource(byte[])";
    public const string OfficialSetPlaySourcePayload =
        "JSONObject.putOpt order: ref_channel, ref_function, ref_content; UTF-8 bytes are passed unchanged to native CmdSessionControl.setPlaySource";

    public const string Missing0040Builder =
        "resolved: the phone firmware localizes the non-empty 0x0040 sender payload builder; keep this constant only as a historical boundary label";
    public const string MissingIdentityBridge =
        "the phone firmware localizes a legacy command session, but not the AppInfo/ServiceName/signature path into that 8899 session";
    public const string ExtractionBoundary =
        "layout=3 EROFS extraction is complete for selected APK/VDEX/ODEX candidates; remaining function-level ordering still requires targeted DEX/ELF tracing, not whole-APK JADX";

    public static MiPlayPhoneFirmwareSourceSnapshot CreateCurrentSnapshot() =>
        new(
            FirmwareDirectoryCataloged: true,
            LogicalProductAndSystemExtPartitionsExtracted: true,
            MinimalErofsDirectoryIndexBuilt: true,
            MirrorOs3CandidateLocalized: true,
            MiLinkOs3CandidateLocalized: true,
            CmdSessionControlJniObserved: true,
            CmdSourceAndCmdControlSymbolsObserved: true,
            CreateCmdSessionAddrPortObserved: true,
            SendOpenDeviceLogObserved: true,
            SafetyAuthAckObserved: true,
            CmdAuthObserved: true,
            SafetyKeyDealAuthKeyAndAesIvObserved: true,
            CmdTypeAndAckLoggingObserved: true,
            NativeControlVersionObserved: true,
            PhoneFirmwareSourceSideLegacyStackObserved: true,
            CandidateFilesExtractedFromErofs: true,
            ApkZipIntegrityVerified: true,
            DexCmdSessionXrefsRecovered: true,
            OfficialSender0040BuilderLocalized: true,
            AppInfoServiceNameToLegacy8899BridgeLocalized: false,
            WireCommandIdsRecoveredFromPhoneFirmware: true,
            ErofsLayout3CompressedFilesRemainUnextracted: false,
            NoSpeakerOrLanOperationPerformed: true,
            Forbid0058OpenAddMirrorRtspMediaPlaybackAudio: true);

    public static MiPlayPhoneFirmwareSourceDecision Evaluate(
        MiPlayPhoneFirmwareSourceSnapshot snapshot)
    {
        if (!snapshot.FirmwareDirectoryCataloged ||
            !snapshot.LogicalProductAndSystemExtPartitionsExtracted ||
            !snapshot.MinimalErofsDirectoryIndexBuilt)
        {
            return new MiPlayPhoneFirmwareSourceDecision(
                false,
                false,
                "The phone firmware has not yet been cataloged to partition/path level.",
                "extract logical partitions and build a minimal EROFS directory index first",
                "Do not use phone-firmware evidence for any live probe.");
        }

        if (!snapshot.PhoneFirmwareSourceSideLegacyStackObserved ||
            !snapshot.CmdSessionControlJniObserved ||
            !snapshot.CreateCmdSessionAddrPortObserved ||
            !snapshot.SendOpenDeviceLogObserved)
        {
            return new MiPlayPhoneFirmwareSourceDecision(
                false,
                false,
                "The source-side legacy command-session stack is not localized strongly enough.",
                "continue raw and file-level static analysis of MirrorOS3/MiLinkOS3",
                "Do not design 0x0040/open/media probes.");
        }

        var sourceConclusion =
            "Mi13P HyperOS phone firmware localizes a source-side legacy MiPlay command stack in product_a, with MirrorOS3/MiLinkOS3 candidates, CmdSessionControl JNI, CmdSource/CmdControl, createCmdSession addr/port, send openDevice, SafetyAuth/Cmd_Auth acknowledgement names, SafetyKeyDeal key/IV helpers, extracted APK/VDEX/ODEX files, numeric command-name mappings, and native control version 3.2.5121919.";

        if (!snapshot.CandidateFilesExtractedFromErofs ||
            !snapshot.ApkZipIntegrityVerified ||
            !snapshot.DexCmdSessionXrefsRecovered)
        {
            return new MiPlayPhoneFirmwareSourceDecision(
                false,
                false,
                sourceConclusion,
                ExtractionBoundary,
                "Keep non-empty 0x0040, 0x0058, Cmd_Open/openDevice, AddMirror, RTSP, media, playback, and audio forbidden.");
        }

        if (!snapshot.OfficialSender0040BuilderLocalized ||
            !snapshot.WireCommandIdsRecoveredFromPhoneFirmware)
        {
            return new MiPlayPhoneFirmwareSourceDecision(
                false,
                false,
                sourceConclusion,
                "recover the numeric command map and source-side 0x0040 payload builder from extracted phone firmware files",
                "Keep non-empty 0x0040, 0x0058, Cmd_Open/openDevice, AddMirror, RTSP, media, playback, and audio forbidden.");
        }

        if (!snapshot.AppInfoServiceNameToLegacy8899BridgeLocalized)
        {
            return new MiPlayPhoneFirmwareSourceDecision(
                false,
                true,
                sourceConclusion,
                $"{OfficialSetPlaySourceBuilder}; {RecoveredWireCommandIds}; still missing: {MissingIdentityBridge}",
                "Offline construction of the official 0x0040 JSON bytes is now supported, but live non-empty 0x0040 and all open/media frames remain forbidden until the source-identity bridge and LX06 1.94.13 semantics are separately validated.");
        }

        if (snapshot.ErofsLayout3CompressedFilesRemainUnextracted ||
            !snapshot.NoSpeakerOrLanOperationPerformed ||
            !snapshot.Forbid0058OpenAddMirrorRtspMediaPlaybackAudio)
        {
            return new MiPlayPhoneFirmwareSourceDecision(
                false,
                false,
                sourceConclusion,
                ExtractionBoundary,
                "Resolve the offline extraction/safety boundary before writing any live plan.");
        }

        return new MiPlayPhoneFirmwareSourceDecision(
            true,
            true,
            sourceConclusion,
            "A non-empty 0x0040 builder and identity bridge would still need a separate exact validation plan and fresh authorization.",
            "Only a separately reviewed identity-only 0x0040 plan could be considered; media/open paths remain out of scope.");
    }
}