namespace DLNACast.Core.MiPlay;

public sealed record MiPlayLx06FirmwareReceiverStackSnapshot(
    bool DataUbifsPartitionMounted,
    bool DataMicoConfigBindMountsObserved,
    bool DataPlayerConfigDirectoryObserved,
    bool DirectDataServiceAutostartObserved,
    bool RootfsSlotSelectionObserved,
    bool OtaCanReplaceRootfsSlots,
    bool SysupgradeOverlayPreserveObserved,
    bool CurrentObservedRuntimeVersionMatchesFirmware,
    bool MediaplayerProcdServiceObserved,
    bool MediaplayerUbusServerObserved,
    bool MediaplayerPlayUrlMethodObserved,
    bool MediaplayerPlayMusicMethodObserved,
    bool MediaplayerPlayOperationMethodObserved,
    bool MediaplayerContextAndStatusMethodsObserved,
    bool MediaplayerVolumeMethodsObserved,
    bool MiplayerLocalCliObserved,
    bool WirelessMiplayFunctionIsLocalPromptWrapper,
    bool DlnaQplayBridgeObserved,
    bool MdplayMultiroomBridgeObserved,
    bool MultiroomFifoObserved,
    bool SafetyAuthReceiverStringsObserved,
    bool LegacyTcp8899ReceiverStringsObserved);

/// <summary>
/// Offline-only receiver-stack evidence from the LX06/S12 1.74.1 firmware.
/// This keeps persistent-data/OTA/runtime-injection evidence separate from the
/// local playback bridge, because neither one proves the modern 8899
/// SafetyAuth receiver used by the currently observed S12 runtime.
/// </summary>
public static class MiPlayLx06FirmwareReceiverStackEvidence
{
    public const string FirmwareHardware = "LX06";
    public const string FirmwareRomVersion = "1.74.1";
    public const string FirmwareBuildDate = "2021-04-25";
    public const string CurrentObservedLx06Version = "1.94.13";
    public const string NearestAnalyzedReceiverVersion = "1.88.51";

    public const string BootRootInitPath = "init";
    public const string BootPartEnvironmentName = "boot_part";
    public const string RootfsBoot0MtdBlock = "/dev/mtdblock4";
    public const string RootfsBoot1MtdBlock = "/dev/mtdblock5";
    public const string SwitchRootCommand = "exec switch_root -c /dev/console /mnt \"${init}\"";

    public const string DataMountInitScript = "etc/init.d/boot";
    public const string DataUbiAttachCommand = "ubiattach -p /dev/mtd6";
    public const string DataUbifsMountCommand = "mount -t ubifs /dev/ubi0_0 /data";
    public const string DataMicoConfigBindMountCommand = "mount --bind /data/mico/$onecfg  /usr/share/mico/$onecfg";
    public const string DataMicoManifestBindMountCommand = "mount --bind /data/mico/manifest  /usr/share/mico/manifest";
    public const string DirectDataServiceCommandPrefix = "procd_set_param command /data";

    public const string SysupgradeScript = "sbin/sysupgrade";
    public const string SysupgradeOverlayDirectory = "/overlay/upper";
    public const string SysupgradeUbusCommand = "ubus call system sysupgrade";
    public const string BoardUpgradeScript = "bin/boardupgrade.sh";
    public const string FlashScript = "bin/flash.sh";
    public const string OtaStatusPath = "/data/status/ota";

    public const string MediaplayerInitScript = "etc/init.d/mediaplayer";
    public const string MediaplayerBinary = "usr/bin/mediaplayer";
    public const string MediaplayerProcdCommand = "procd_set_param command /usr/bin/mediaplayer";
    public const string MediaplayerDataLinkDirectory = "/data/player";

    public const string MediaplayerPlayUrlMethod = "player_play_url";
    public const string MediaplayerPlayMusicMethod = "player_play_music";
    public const string MediaplayerPlayOperationMethod = "player_play_operation";
    public const string MediaplayerGetPlayStatusMethod = "player_get_play_status";
    public const string MediaplayerGetContextMethod = "player_get_context";
    public const string MediaplayerSetVolumeMethod = "player_set_volume";
    public const string MediaplayerSetContinuousVolumeMethod = "player_set_continuous_volume";
    public const string MediaplayerGetMediaVolumeMethod = "get_media_volume";
    public const string MediaplayerNotifyMdplayStatusMethod = "notify_mdplay_status";
    public const string MediaplayerWakeupMethod = "player_wakeup";

    public const string MiplayerBinary = "usr/bin/miplayer";
    public const string MiplayerCreateSymbol = "miplayer_create";
    public const string WirelessInitScript = "etc/init.d/wireless";
    public const string WirelessMiplayWrapperCommand = "nice -n -10 miplayer -f $1";

    public const string DlnaInitScript = "etc/init.d/dlnainit";
    public const string DlnaBinary = "usr/bin/dlna";
    public const string DlnaDataDeviceXmlPath = "/data/dlna/device.xml";
    public const string DlnaQplayAuthString = "QPlayAuth";
    public const string DlnaSetAvTransportUriString = "SetAVTransportURI";

    public const string MdplayInitScript = "etc/init.d/mdplay";
    public const string MdplayBinary = "usr/bin/mdplay";
    public const string MdplayNotifyStatusStopCommand = "ubus call mediaplayer notify_mdplay_status '{\"status\":0}'";
    public const string MultiroomFifoPath = "/tmp/multiroom.fifo";
    public const string MultiroomFifoPipeUrl = "pipe:///tmp/multiroom.fifo?name=Radio";
    public const string AudioFifoFileOption = "audiofifo-file";

    public static readonly string[] ObservedMediaplayerUbusMethods =
    [
        MediaplayerPlayUrlMethod,
        MediaplayerPlayMusicMethod,
        MediaplayerPlayOperationMethod,
        MediaplayerGetPlayStatusMethod,
        MediaplayerGetContextMethod,
        MediaplayerSetVolumeMethod,
        MediaplayerSetContinuousVolumeMethod,
        MediaplayerGetMediaVolumeMethod,
        MediaplayerNotifyMdplayStatusMethod,
        MediaplayerWakeupMethod,
    ];

    public static MiPlayLx06FirmwareReceiverStackSnapshot CreateCurrentLx06FirmwareSnapshot() =>
        new(
            DataUbifsPartitionMounted: true,
            DataMicoConfigBindMountsObserved: true,
            DataPlayerConfigDirectoryObserved: true,
            DirectDataServiceAutostartObserved: false,
            RootfsSlotSelectionObserved: true,
            OtaCanReplaceRootfsSlots: true,
            SysupgradeOverlayPreserveObserved: true,
            CurrentObservedRuntimeVersionMatchesFirmware: false,
            MediaplayerProcdServiceObserved: true,
            MediaplayerUbusServerObserved: true,
            MediaplayerPlayUrlMethodObserved: true,
            MediaplayerPlayMusicMethodObserved: true,
            MediaplayerPlayOperationMethodObserved: true,
            MediaplayerContextAndStatusMethodsObserved: true,
            MediaplayerVolumeMethodsObserved: true,
            MiplayerLocalCliObserved: true,
            WirelessMiplayFunctionIsLocalPromptWrapper: true,
            DlnaQplayBridgeObserved: true,
            MdplayMultiroomBridgeObserved: true,
            MultiroomFifoObserved: true,
            SafetyAuthReceiverStringsObserved: false,
            LegacyTcp8899ReceiverStringsObserved: false);

    public static MiPlayIdmStateDecision EvaluateDynamicReceiverInjection(
        MiPlayLx06FirmwareReceiverStackSnapshot snapshot)
    {
        if (!snapshot.DataUbifsPartitionMounted)
        {
            return new MiPlayIdmStateDecision(false, "The firmware does not show the persistent /data UBIFS partition being mounted.");
        }

        if (snapshot.DirectDataServiceAutostartObserved)
        {
            return new MiPlayIdmStateDecision(true, "A direct /data service autostart path was observed.");
        }

        if (!snapshot.RootfsSlotSelectionObserved || !snapshot.OtaCanReplaceRootfsSlots)
        {
            return new MiPlayIdmStateDecision(false, "The firmware does not show enough A/B rootfs or OTA replacement evidence to explain a newer receiver stack.");
        }

        return new MiPlayIdmStateDecision(
            false,
            "The 1.74.1 image proves persistent /data state plus OTA/rootfs replacement, but no direct /data receiver-service autostart path. The later 1.88.51 image should be used for mpas receiver evidence.");
    }

    public static MiPlayIdmStateDecision EvaluatePlaybackBridge(
        MiPlayLx06FirmwareReceiverStackSnapshot snapshot)
    {
        if (!snapshot.MediaplayerProcdServiceObserved || !snapshot.MediaplayerUbusServerObserved)
        {
            return new MiPlayIdmStateDecision(false, "The mediaplayer procd/ubus service boundary is not proven.");
        }

        if (!snapshot.MediaplayerPlayUrlMethodObserved ||
            !snapshot.MediaplayerPlayMusicMethodObserved ||
            !snapshot.MediaplayerPlayOperationMethodObserved)
        {
            return new MiPlayIdmStateDecision(false, "The mediaplayer play URL/music/operation methods are incomplete.");
        }

        if (!snapshot.MiplayerLocalCliObserved || !snapshot.WirelessMiplayFunctionIsLocalPromptWrapper)
        {
            return new MiPlayIdmStateDecision(false, "The local miplayer prompt wrapper evidence is incomplete.");
        }

        if (!snapshot.DlnaQplayBridgeObserved || !snapshot.MdplayMultiroomBridgeObserved || !snapshot.MultiroomFifoObserved)
        {
            return new MiPlayIdmStateDecision(false, "The DLNA/QPlay or mdplay multiroom FIFO bridge evidence is incomplete.");
        }

        return new MiPlayIdmStateDecision(
            true,
            "The 1.74.1 firmware exposes a reusable local mediaplayer/ubus/FIFO playback bridge, not a proven SafetyAuth MiPlay receiver.");
    }

    public static MiPlayIdmStateDecision EvaluateDirectMiPlayReceiverReconstruction(
        MiPlayLx06FirmwareReceiverStackSnapshot snapshot)
    {
        if (!snapshot.CurrentObservedRuntimeVersionMatchesFirmware)
        {
            return new MiPlayIdmStateDecision(
                false,
                $"The corrected LX06 runtime baseline is {CurrentObservedLx06Version}, which is newer than firmware {FirmwareRomVersion}; use the nearer {NearestAnalyzedReceiverVersion} mpas/mpap receiver image for bounded legacy/basic reconstruction, while a matching OTA or read-only device file/process map is only required for exact current modern SafetyAuth compatibility.");
        }

        if (!snapshot.SafetyAuthReceiverStringsObserved || !snapshot.LegacyTcp8899ReceiverStringsObserved)
        {
            return new MiPlayIdmStateDecision(false, "No SafetyAuth/legacy TCP 8899 receiver implementation was proven in the analyzed firmware image.");
        }

        return new MiPlayIdmStateDecision(
            true,
            "The receiver image/version and SafetyAuth TCP 8899 evidence are sufficient to start direct receiver reconstruction.");
    }
}