using System.Collections.ObjectModel;

namespace DLNACast.Core.MiPlay;

public sealed record MiPlayPhoneFirmwareSourceFieldSnapshot(
    bool RefChannelFieldObserved,
    bool RefChannelGetterAndSetterObserved,
    bool RefChannelGetterFeedsSetPlaySource,
    bool RefChannelSetterCallerIsMultiDisplayManage,
    bool RefChannelValuesRecovered,
    bool RefContentPackageMapRecovered,
    bool RefFunctionValuesRecovered,
    bool TopActiveSessionChangeUpdatesRefContentAndSetPlaySource,
    bool StartCommandChannelCreatesLegacyCmdSessionControl,
    bool StartCommandChannelUsesMiDeviceMacNameIpPort,
    bool OptionalLyraSecretKeyCommandObserved,
    bool SecretKeyCommandCarriesLyraKeyMaterialOnly,
    bool NativeConnectCmdSession2SecretKeyBridgeRecovered,
    bool NativeSetLyraInfoParsesSecretKeyCommandOnly,
    bool NativeSetPlaySourceCommandId0040Recovered,
    bool StartCommandChannelHasNoServiceNameOrAppInfoReferences,
    bool LyraContinuityServiceNamePathObservedSeparately,
    bool CmdSessionSuccessTriggersGetDeviceInfo,
    bool NoSpeakerOrLanOperationPerformed,
    bool ForbidLiveNonEmptySetPlaySourceOpenMediaAudio);

public sealed record MiPlayPhoneFirmwareSourceFieldDecision(
    bool CanBuildOfflineSetPlaySourcePayloadExamples,
    bool CanAuthorizeLiveSetPlaySourceProbe,
    string SourceFieldConclusion,
    string MissingBridge,
    string Boundary);

/// <summary>
/// Offline source-field evidence from Mi13P MiLinkOS3Cn DEX tables. This class
/// records the official ref_channel/ref_function/ref_content sources used by
/// Cmd_SetPlaySource (0x0040), while keeping legacy 8899 identity bridging and
/// all live business frames out of scope.
/// </summary>
public static class MiPlayPhoneFirmwareSourceFieldEvidence
{
    public const string DexTraceArtifact =
        @"artifacts\phone_firmware\mi13p_os3_0_313\phone_source_ref_identity_trace.json";
    public const string DexSource =
        @"artifacts\phone_firmware\mi13p_os3_0_313\apk_extract\MiLinkOS3Cn\classes3.dex";

    public const string RefChannelField =
        "Lcom/xiaomi/miplay/mylibrary/MiDevice;->ref_channel:Ljava/lang/String;";
    public const string SetPlaySourceBuilder =
        "StatsUtils.setPlaySource(DeviceManager, Map) reads MiDevice.getRef_channel(), ref_functionMap[ref_function], ref_contentMap[ref_content], then calls ontrackDataToJson and CmdSessionControl.setPlaySource(byte[])";
    public const string RefChannelSetterBoundary =
        "MiDevice.setRef_channel(String) is called from MiplayMultiDisplayManage.getMultiPort; normal setPlaySource reads the field and does not synthesize it locally";
    public const string TopActiveSessionChangePath =
        "MiPlayAudioService.onTopActiveSessionChange calls StatsUtils.setRefContent(package), StatsUtils.setPlaySource(DeviceManager, cmdSessionControlMap), and StatsUtils.setRecordPackageName(package)";
    public const string LegacyCommandChannelPath =
        "MiPlayAudioService.startCommandChannel constructs CmdSessionControl(MiDevice), installs MiplaySessionCallbackProxy, and calls connectCmdSession with MiDevice mac/name/ip/port plus an optional secretKeyCommand";
    public const string LegacyCommandSessionInputs =
        "CmdSessionControl.connectCmdSession arguments are sourced from MiDevice.getMac(), getName(), getIp(), getPort(), and optionally ProtocolSession/cache SecretKeyCommand JSON";
    public const string SecretKeyCommandJsonShape =
        "ProtocolSession.parseSecretKeyCommand/toJson reads and writes wlan0ip, authKey, streamKey, and streamIV";
    public const string SecretKeyCommandBoundary =
        "SecretKeyCommand is Lyra key material JSON (wlan0ip/authKey/streamKey/streamIV); targeted DEX xrefs do not show AppInfo, ServiceName, signature, package identity, or ref_* fields inside this JSON";
    public const string NativeSetPlaySource0040Path =
        "MiLinkOS3Cn libmirror-jni.so CmdControl::setPlaySource at 0x18b698-0x18b6b4 and CmdSource::setPlaySource at 0x18b724-0x18b740 load cmdType 0x40, increment CmdSource seq at +0x2c0, and call sendCmdPayload(cmd=0x40, seq, payload, len)";
    public const string NativeConnectCmdSession2SecretKeyBridge =
        "MiLinkOS3Cn libmirror-jni.so connectCmdSession2 xref at 0x17f410-0x17f5b8 gets CmdControl by handle, converts the optional Java secretKeyCommand string, calls the vtable slot matching setLyraInfo before vtable connectCmdSession, then connects with addr/port/sessionType";
    public const string NativeSetLyraInfoJsonBoundary =
        "CmdSource::setLyraInfo at 0x18da68 parses JSON keys wlan0ip, authKey, streamKey, streamIV; stores authKey/streamKey/streamIV into CmdSource string fields (+0x360/+0x378/+0x390) and sets Lyra flags, with no AppInfo, ServiceName, signature, package identity, or ref_* references";
    public const string SeparateLyraServiceNamePath =
        "CommonUtil.getServiceName and LyraChannelManager feed ContinuityChannelManager.createChannel/registerChannel with ServiceName; this is separate from CmdSessionControl in current Java xrefs";
    public const string MissingLegacyIdentityBridge =
        "no current targeted DEX xref shows AppInfo/ServiceName/signature entering CmdSessionControl or the legacy 8899 SetPlaySource session";

    public static IReadOnlyDictionary<int, string> RefChannelByCode { get; } =
        new ReadOnlyDictionary<int, string>(new Dictionary<int, string>
        {
            [0] = "controlcenter",
            [1] = "nearfield",
            [2] = "xiaoai_phone",
            [3] = "farfield",
            [4] = "lockscreen",
            [5] = "notification",
            [6] = "playpage",
            [7] = "world",
            [8] = "relay_card",
            [9] = "nfc",
        });

    public static IReadOnlyDictionary<string, string> RefContentByPackage { get; } =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["com.miui.player"] = "music_miui",
            ["com.netease.cloudmusic"] = "music_wangyiyun",
            ["com.tencent.qqmusic"] = "music_qq",
            ["com.kugou.android"] = "music_kugou",
            ["cn.kuwo.player"] = "music_kuwo",
            ["com.ximalaya.ting.android"] = "fm_himalaya",
            ["fm.qingting.qtradio"] = "fm_qingting",
            ["com.yibasan.lizhifm"] = "fm_lizhi",
            ["com.luojilab.player"] = "fm_dedao",
        });

    public static IReadOnlySet<string> RefFunctionValues { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "single_room",
            "multi_room",
            "stereo",
        };

    public static string GetRefChannelOrDefault(int code) =>
        RefChannelByCode.TryGetValue(code, out var value) ? value : "controlcenter";

    public static string GetRefContentOrEmpty(string? packageName) =>
        packageName is not null && RefContentByPackage.TryGetValue(packageName, out var value)
            ? value
            : string.Empty;

    public static MiPlayPhoneFirmwareSourceFieldSnapshot CreateCurrentSnapshot() =>
        new(
            RefChannelFieldObserved: true,
            RefChannelGetterAndSetterObserved: true,
            RefChannelGetterFeedsSetPlaySource: true,
            RefChannelSetterCallerIsMultiDisplayManage: true,
            RefChannelValuesRecovered: true,
            RefContentPackageMapRecovered: true,
            RefFunctionValuesRecovered: true,
            TopActiveSessionChangeUpdatesRefContentAndSetPlaySource: true,
            StartCommandChannelCreatesLegacyCmdSessionControl: true,
            StartCommandChannelUsesMiDeviceMacNameIpPort: true,
            OptionalLyraSecretKeyCommandObserved: true,
            SecretKeyCommandCarriesLyraKeyMaterialOnly: true,
            NativeConnectCmdSession2SecretKeyBridgeRecovered: true,
            NativeSetLyraInfoParsesSecretKeyCommandOnly: true,
            NativeSetPlaySourceCommandId0040Recovered: true,
            StartCommandChannelHasNoServiceNameOrAppInfoReferences: true,
            LyraContinuityServiceNamePathObservedSeparately: true,
            CmdSessionSuccessTriggersGetDeviceInfo: true,
            NoSpeakerOrLanOperationPerformed: true,
            ForbidLiveNonEmptySetPlaySourceOpenMediaAudio: true);

    public static MiPlayPhoneFirmwareSourceFieldDecision Evaluate(
        MiPlayPhoneFirmwareSourceFieldSnapshot snapshot)
    {
        if (!snapshot.RefChannelFieldObserved ||
            !snapshot.RefChannelGetterAndSetterObserved ||
            !snapshot.RefChannelValuesRecovered ||
            !snapshot.RefContentPackageMapRecovered ||
            !snapshot.RefFunctionValuesRecovered)
        {
            return new MiPlayPhoneFirmwareSourceFieldDecision(
                false,
                false,
                "The official SetPlaySource source fields are not fully localized.",
                "continue targeted DEX tracing of StatsUtils and MiDevice fields",
                "Do not build or send non-empty 0x0040 payloads.");
        }

        var conclusion =
            "MiLinkOS3Cn localizes the official SetPlaySource fields: MiDevice.ref_channel, StatsUtils ref_content package mapping, and ref_function values single_room/multi_room/stereo. onTopActiveSessionChange updates ref_content and sends SetPlaySource through the legacy CmdSessionControl map. startCommandChannel feeds legacy connectCmdSession from MiDevice mac/name/ip/port and optional Lyra SecretKeyCommand JSON. Native libmirror-jni confirms setPlaySource sends cmd 0x40, while connectCmdSession2 bridges only wlan0ip/authKey/streamKey/streamIV Lyra key material into setLyraInfo, not the missing AppInfo/ServiceName identity bridge.";

        if (!snapshot.StartCommandChannelCreatesLegacyCmdSessionControl ||
            !snapshot.StartCommandChannelUsesMiDeviceMacNameIpPort ||
            !snapshot.OptionalLyraSecretKeyCommandObserved ||
            !snapshot.SecretKeyCommandCarriesLyraKeyMaterialOnly ||
            !snapshot.NativeConnectCmdSession2SecretKeyBridgeRecovered ||
            !snapshot.NativeSetLyraInfoParsesSecretKeyCommandOnly ||
            !snapshot.NativeSetPlaySourceCommandId0040Recovered ||
            !snapshot.CmdSessionSuccessTriggersGetDeviceInfo)
        {
            return new MiPlayPhoneFirmwareSourceFieldDecision(
                true,
                false,
                conclusion,
                "legacy command-session success ordering is incomplete",
                "Keep live probes forbidden until command-session state is fully modeled.");
        }

        if (!snapshot.StartCommandChannelHasNoServiceNameOrAppInfoReferences ||
            !snapshot.LyraContinuityServiceNamePathObservedSeparately)
        {
            return new MiPlayPhoneFirmwareSourceFieldDecision(
                true,
                false,
                conclusion,
                "ServiceName/AppInfo xrefs changed; classify whether this is a legacy 8899 bridge before any live plan",
                "Treat the source identity bridge as unstable until the new xrefs are classified.");
        }

        return new MiPlayPhoneFirmwareSourceFieldDecision(
            true,
            false,
            conclusion,
            MissingLegacyIdentityBridge + "; native connectCmdSession2 currently explains only the optional Lyra key JSON bridge",
            "Offline payload examples are now well grounded, but live non-empty 0x0040, 0x0058, Cmd_Open, AddMirror, RTSP, media, playback, and audio remain forbidden.");
    }
}