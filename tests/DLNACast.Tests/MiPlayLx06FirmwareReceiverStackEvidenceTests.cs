using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayLx06FirmwareReceiverStackEvidenceTests
{
    [Fact]
    public void ConstantsCaptureDataOverlayAndOtaEvidence()
    {
        Assert.Equal("LX06", MiPlayLx06FirmwareReceiverStackEvidence.FirmwareHardware);
        Assert.Equal("1.74.1", MiPlayLx06FirmwareReceiverStackEvidence.FirmwareRomVersion);
        Assert.Equal("2021-04-25", MiPlayLx06FirmwareReceiverStackEvidence.FirmwareBuildDate);
        Assert.Equal("1.94.13", MiPlayLx06FirmwareReceiverStackEvidence.CurrentObservedLx06Version);
        Assert.Equal("1.88.51", MiPlayLx06FirmwareReceiverStackEvidence.NearestAnalyzedReceiverVersion);

        Assert.Equal("init", MiPlayLx06FirmwareReceiverStackEvidence.BootRootInitPath);
        Assert.Equal("boot_part", MiPlayLx06FirmwareReceiverStackEvidence.BootPartEnvironmentName);
        Assert.Equal("/dev/mtdblock4", MiPlayLx06FirmwareReceiverStackEvidence.RootfsBoot0MtdBlock);
        Assert.Equal("/dev/mtdblock5", MiPlayLx06FirmwareReceiverStackEvidence.RootfsBoot1MtdBlock);
        Assert.Equal(
            "exec switch_root -c /dev/console /mnt \"${init}\"",
            MiPlayLx06FirmwareReceiverStackEvidence.SwitchRootCommand);

        Assert.Equal("etc/init.d/boot", MiPlayLx06FirmwareReceiverStackEvidence.DataMountInitScript);
        Assert.Equal("ubiattach -p /dev/mtd6", MiPlayLx06FirmwareReceiverStackEvidence.DataUbiAttachCommand);
        Assert.Equal("mount -t ubifs /dev/ubi0_0 /data", MiPlayLx06FirmwareReceiverStackEvidence.DataUbifsMountCommand);
        Assert.Equal(
            "mount --bind /data/mico/$onecfg  /usr/share/mico/$onecfg",
            MiPlayLx06FirmwareReceiverStackEvidence.DataMicoConfigBindMountCommand);
        Assert.Equal(
            "mount --bind /data/mico/manifest  /usr/share/mico/manifest",
            MiPlayLx06FirmwareReceiverStackEvidence.DataMicoManifestBindMountCommand);
        Assert.Equal("procd_set_param command /data", MiPlayLx06FirmwareReceiverStackEvidence.DirectDataServiceCommandPrefix);

        Assert.Equal("sbin/sysupgrade", MiPlayLx06FirmwareReceiverStackEvidence.SysupgradeScript);
        Assert.Equal("/overlay/upper", MiPlayLx06FirmwareReceiverStackEvidence.SysupgradeOverlayDirectory);
        Assert.Equal("ubus call system sysupgrade", MiPlayLx06FirmwareReceiverStackEvidence.SysupgradeUbusCommand);
        Assert.Equal("bin/boardupgrade.sh", MiPlayLx06FirmwareReceiverStackEvidence.BoardUpgradeScript);
        Assert.Equal("bin/flash.sh", MiPlayLx06FirmwareReceiverStackEvidence.FlashScript);
        Assert.Equal("/data/status/ota", MiPlayLx06FirmwareReceiverStackEvidence.OtaStatusPath);
    }

    [Fact]
    public void ConstantsCaptureMediaplayerUbusAndFifoBridgeEvidence()
    {
        Assert.Equal("etc/init.d/mediaplayer", MiPlayLx06FirmwareReceiverStackEvidence.MediaplayerInitScript);
        Assert.Equal("usr/bin/mediaplayer", MiPlayLx06FirmwareReceiverStackEvidence.MediaplayerBinary);
        Assert.Equal(
            "procd_set_param command /usr/bin/mediaplayer",
            MiPlayLx06FirmwareReceiverStackEvidence.MediaplayerProcdCommand);
        Assert.Equal("/data/player", MiPlayLx06FirmwareReceiverStackEvidence.MediaplayerDataLinkDirectory);

        Assert.Contains("player_play_url", MiPlayLx06FirmwareReceiverStackEvidence.ObservedMediaplayerUbusMethods);
        Assert.Contains("player_play_music", MiPlayLx06FirmwareReceiverStackEvidence.ObservedMediaplayerUbusMethods);
        Assert.Contains("player_play_operation", MiPlayLx06FirmwareReceiverStackEvidence.ObservedMediaplayerUbusMethods);
        Assert.Contains("player_get_play_status", MiPlayLx06FirmwareReceiverStackEvidence.ObservedMediaplayerUbusMethods);
        Assert.Contains("player_get_context", MiPlayLx06FirmwareReceiverStackEvidence.ObservedMediaplayerUbusMethods);
        Assert.Contains("player_set_volume", MiPlayLx06FirmwareReceiverStackEvidence.ObservedMediaplayerUbusMethods);
        Assert.Contains("player_set_continuous_volume", MiPlayLx06FirmwareReceiverStackEvidence.ObservedMediaplayerUbusMethods);
        Assert.Contains("get_media_volume", MiPlayLx06FirmwareReceiverStackEvidence.ObservedMediaplayerUbusMethods);
        Assert.Contains("notify_mdplay_status", MiPlayLx06FirmwareReceiverStackEvidence.ObservedMediaplayerUbusMethods);
        Assert.Contains("player_wakeup", MiPlayLx06FirmwareReceiverStackEvidence.ObservedMediaplayerUbusMethods);

        Assert.Equal("usr/bin/miplayer", MiPlayLx06FirmwareReceiverStackEvidence.MiplayerBinary);
        Assert.Equal("miplayer_create", MiPlayLx06FirmwareReceiverStackEvidence.MiplayerCreateSymbol);
        Assert.Equal("etc/init.d/wireless", MiPlayLx06FirmwareReceiverStackEvidence.WirelessInitScript);
        Assert.Equal(
            "nice -n -10 miplayer -f $1",
            MiPlayLx06FirmwareReceiverStackEvidence.WirelessMiplayWrapperCommand);

        Assert.Equal("etc/init.d/dlnainit", MiPlayLx06FirmwareReceiverStackEvidence.DlnaInitScript);
        Assert.Equal("usr/bin/dlna", MiPlayLx06FirmwareReceiverStackEvidence.DlnaBinary);
        Assert.Equal("/data/dlna/device.xml", MiPlayLx06FirmwareReceiverStackEvidence.DlnaDataDeviceXmlPath);
        Assert.Equal("QPlayAuth", MiPlayLx06FirmwareReceiverStackEvidence.DlnaQplayAuthString);
        Assert.Equal("SetAVTransportURI", MiPlayLx06FirmwareReceiverStackEvidence.DlnaSetAvTransportUriString);

        Assert.Equal("etc/init.d/mdplay", MiPlayLx06FirmwareReceiverStackEvidence.MdplayInitScript);
        Assert.Equal("usr/bin/mdplay", MiPlayLx06FirmwareReceiverStackEvidence.MdplayBinary);
        Assert.Equal(
            "ubus call mediaplayer notify_mdplay_status '{\"status\":0}'",
            MiPlayLx06FirmwareReceiverStackEvidence.MdplayNotifyStatusStopCommand);
        Assert.Equal("/tmp/multiroom.fifo", MiPlayLx06FirmwareReceiverStackEvidence.MultiroomFifoPath);
        Assert.Equal(
            "pipe:///tmp/multiroom.fifo?name=Radio",
            MiPlayLx06FirmwareReceiverStackEvidence.MultiroomFifoPipeUrl);
        Assert.Equal("audiofifo-file", MiPlayLx06FirmwareReceiverStackEvidence.AudioFifoFileOption);
    }

    [Fact]
    public void CurrentSnapshotSeparatesPersistentDataFromDirectReceiverInjection()
    {
        var snapshot = MiPlayLx06FirmwareReceiverStackEvidence.CreateCurrentLx06FirmwareSnapshot();

        Assert.True(snapshot.DataUbifsPartitionMounted);
        Assert.True(snapshot.DataMicoConfigBindMountsObserved);
        Assert.True(snapshot.DataPlayerConfigDirectoryObserved);
        Assert.True(snapshot.RootfsSlotSelectionObserved);
        Assert.True(snapshot.OtaCanReplaceRootfsSlots);
        Assert.True(snapshot.SysupgradeOverlayPreserveObserved);
        Assert.False(snapshot.DirectDataServiceAutostartObserved);
        Assert.False(snapshot.CurrentObservedRuntimeVersionMatchesFirmware);

        var decision = MiPlayLx06FirmwareReceiverStackEvidence.EvaluateDynamicReceiverInjection(snapshot);

        Assert.False(decision.CanProceed);
        Assert.Contains("/data", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("OTA", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("no direct /data", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void CurrentSnapshotTreatsBridgeAsReusableButNotSafetyAuthReceiver()
    {
        var snapshot = MiPlayLx06FirmwareReceiverStackEvidence.CreateCurrentLx06FirmwareSnapshot();

        Assert.True(snapshot.MediaplayerProcdServiceObserved);
        Assert.True(snapshot.MediaplayerUbusServerObserved);
        Assert.True(snapshot.MediaplayerPlayUrlMethodObserved);
        Assert.True(snapshot.MediaplayerPlayMusicMethodObserved);
        Assert.True(snapshot.MediaplayerPlayOperationMethodObserved);
        Assert.True(snapshot.MediaplayerContextAndStatusMethodsObserved);
        Assert.True(snapshot.MediaplayerVolumeMethodsObserved);
        Assert.True(snapshot.MiplayerLocalCliObserved);
        Assert.True(snapshot.WirelessMiplayFunctionIsLocalPromptWrapper);
        Assert.True(snapshot.DlnaQplayBridgeObserved);
        Assert.True(snapshot.MdplayMultiroomBridgeObserved);
        Assert.True(snapshot.MultiroomFifoObserved);

        Assert.False(snapshot.SafetyAuthReceiverStringsObserved);
        Assert.False(snapshot.LegacyTcp8899ReceiverStringsObserved);

        var bridge = MiPlayLx06FirmwareReceiverStackEvidence.EvaluatePlaybackBridge(snapshot);

        Assert.True(bridge.CanProceed);
        Assert.Contains("mediaplayer/ubus/FIFO", bridge.Reason, StringComparison.Ordinal);
        Assert.Contains("not a proven SafetyAuth", bridge.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void DirectReceiverReconstructionRequiresCurrentRuntimeEvidence()
    {
        var snapshot = MiPlayLx06FirmwareReceiverStackEvidence.CreateCurrentLx06FirmwareSnapshot();

        var current = MiPlayLx06FirmwareReceiverStackEvidence.EvaluateDirectMiPlayReceiverReconstruction(snapshot);

        Assert.False(current.CanProceed);
        Assert.Contains("1.94.13", current.Reason, StringComparison.Ordinal);
        Assert.Contains("1.88.51", current.Reason, StringComparison.Ordinal);
        Assert.Contains("legacy/basic", current.Reason, StringComparison.Ordinal);
        Assert.Contains("only required for exact current modern SafetyAuth compatibility", current.Reason, StringComparison.Ordinal);

        var matchingVersionButMissingReceiver = MiPlayLx06FirmwareReceiverStackEvidence
            .EvaluateDirectMiPlayReceiverReconstruction(
                snapshot with { CurrentObservedRuntimeVersionMatchesFirmware = true });

        Assert.False(matchingVersionButMissingReceiver.CanProceed);
        Assert.Contains("SafetyAuth", matchingVersionButMissingReceiver.Reason, StringComparison.Ordinal);
        Assert.Contains("8899", matchingVersionButMissingReceiver.Reason, StringComparison.Ordinal);

        var complete = MiPlayLx06FirmwareReceiverStackEvidence.EvaluateDirectMiPlayReceiverReconstruction(
            snapshot with
            {
                CurrentObservedRuntimeVersionMatchesFirmware = true,
                SafetyAuthReceiverStringsObserved = true,
                LegacyTcp8899ReceiverStringsObserved = true,
            });

        Assert.True(complete.CanProceed);
    }
}
