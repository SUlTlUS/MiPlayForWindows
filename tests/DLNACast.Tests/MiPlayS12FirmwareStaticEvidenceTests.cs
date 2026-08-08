using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayS12FirmwareStaticEvidenceTests
{
    [Fact]
    public void ConstantsCaptureCurrentLx06FirmwareMetadataAndExtractionShape()
    {
        Assert.Equal("mico_all_b9cbb_1.74.1.bin", MiPlayS12FirmwareStaticEvidence.FirmwareFileName);
        Assert.Equal(
            "73058C64CBED0CFC915A0E7F162FEF21F01DCA28B477377DA6285B115083624C",
            MiPlayS12FirmwareStaticEvidence.FirmwareSha256);

        Assert.Equal("LX06", MiPlayS12FirmwareStaticEvidence.Hardware);
        Assert.Equal("1.74.1", MiPlayS12FirmwareStaticEvidence.RomVersion);
        Assert.Equal("release", MiPlayS12FirmwareStaticEvidence.Channel);
        Assert.Equal("Sun, 25 Apr 2021 23:11:23 +0800", MiPlayS12FirmwareStaticEvidence.BuildTime);
        Assert.Equal("commit b9e9b6640c2491c7a77a22612e47790e6c8c0356", MiPlayS12FirmwareStaticEvidence.GitTag);

        Assert.Equal(0x2b8, MiPlayS12FirmwareStaticEvidence.RootSquashFsOffset);
        Assert.Equal(36_993_414, MiPlayS12FirmwareStaticEvidence.RootSquashFsBytesUsed);
        Assert.Equal(1_996, MiPlayS12FirmwareStaticEvidence.RootSquashFsInodes);
        Assert.Equal(1_996, MiPlayS12FirmwareStaticEvidence.RootFsRecordsWalked);
        Assert.Equal(111, MiPlayS12FirmwareStaticEvidence.RootFsDirectories);
        Assert.Equal(1_245, MiPlayS12FirmwareStaticEvidence.RootFsFiles);
        Assert.Equal(639, MiPlayS12FirmwareStaticEvidence.RootFsSymlinks);
        Assert.Equal(0, MiPlayS12FirmwareStaticEvidence.RootFsExtractionWarnings);

        Assert.Equal(36_994_184, MiPlayS12FirmwareStaticEvidence.AndroidBootOffset);
        Assert.Equal(139, MiPlayS12FirmwareStaticEvidence.BootRamdiskRecords);
        Assert.Equal(31, MiPlayS12FirmwareStaticEvidence.BootRamdiskFiles);
        Assert.Equal(0, MiPlayS12FirmwareStaticEvidence.BootRamdiskWarnings);
    }

    [Fact]
    public void ConstantsCaptureDeviceSideMdplayIdentityEvidence()
    {
        Assert.Equal("etc/init.d/dlnainit", MiPlayS12FirmwareStaticEvidence.DeviceSideDlnaInitScript);
        Assert.Equal("usr/bin/dlna", MiPlayS12FirmwareStaticEvidence.DeviceSideDlnaBinary);
        Assert.Equal("etc/init.d/mdplay", MiPlayS12FirmwareStaticEvidence.DeviceSideMdplayInitScript);
        Assert.Equal("usr/bin/mdplay", MiPlayS12FirmwareStaticEvidence.DeviceSideMdplayBinary);
        Assert.Equal("usr/lib/libiotdcm_mdplay.so", MiPlayS12FirmwareStaticEvidence.DeviceSideIotdcmMdplayLibrary);
        Assert.Equal("usr/lib/libiotdcm.so", MiPlayS12FirmwareStaticEvidence.DeviceSideIotdcmLibrary);

        Assert.Equal("GetDeviceInfo", MiPlayS12FirmwareStaticEvidence.MdplayGetDeviceInfoSymbol);
        Assert.Equal("MdplayGetDeviceId", MiPlayS12FirmwareStaticEvidence.MdplayDeviceIdSymbol);
        Assert.Equal("MdplayGetAppid", MiPlayS12FirmwareStaticEvidence.MdplayAppIdSymbol);
        Assert.Equal("MdplayGetTokenId", MiPlayS12FirmwareStaticEvidence.MdplayTokenIdSymbol);
        Assert.Equal("MdplayGetUserId", MiPlayS12FirmwareStaticEvidence.MdplayUserIdSymbol);
        Assert.Equal(
            "Authorization: MIOT-TOKEN-V1 app_id:%s,token:%s,session_id:%s",
            MiPlayS12FirmwareStaticEvidence.MdplayMiotTokenV1Format);
        Assert.Equal(
            "iotdcm_create user_id:%lld app_id:%s dev_id:%s token:%s udp_cb:%p",
            MiPlayS12FirmwareStaticEvidence.MdplayIotdcmCreateFormat);
    }

    [Fact]
    public void CurrentFirmwareSnapshotCanInformDeviceSideIdentityButNotTcp8899SafetyAuth()
    {
        var snapshot = MiPlayS12FirmwareStaticEvidence.CreateCurrentSnapshot();

        Assert.True(snapshot.RootSquashFsExtractionComplete);
        Assert.Equal(snapshot.RootSquashFsInodes, snapshot.RootFsRecordsWalked);
        Assert.Equal(0, snapshot.RootFsExtractionWarnings);
        Assert.True(snapshot.BootKernelSearched);
        Assert.True(snapshot.DeviceSideDlnaServiceObserved);
        Assert.True(snapshot.DeviceSideMdplayServiceObserved);
        Assert.True(snapshot.DeviceSideMdplayGetDeviceInfoObserved);
        Assert.True(snapshot.DeviceSideMdplayUsesIotdcmObserved);
        Assert.True(snapshot.DeviceSideMiotTokenV1Observed);
        Assert.True(snapshot.IotdcmSecurityKeyStringObserved);
        Assert.True(snapshot.IotdcmServiceKeyStringObserved);

        Assert.False(snapshot.LegacyTcp8899StringObserved);
        Assert.False(snapshot.SafetyAuthStringObserved);
        Assert.False(snapshot.SafetyDataStringObserved);
        Assert.False(snapshot.ModernSafetyAuthOpcodeStringObserved);
        Assert.False(snapshot.BootRamdiskSafetyAuthStringObserved);
        Assert.False(snapshot.BootKernelSafetyAuthStringObserved);

        var deviceIdentity = MiPlayS12FirmwareStaticEvidence.EvaluateForDeviceSidePostAuthIdentity(snapshot);
        Assert.True(deviceIdentity.CanProceed);
        Assert.Contains("mdplay/iotdcm", deviceIdentity.Reason, StringComparison.Ordinal);

        var tcp8899 = MiPlayS12FirmwareStaticEvidence.EvaluateForLegacyTcp8899SafetyAuth(snapshot);
        Assert.False(tcp8899.CanProceed);
        Assert.Contains("No legacy TCP 8899", tcp8899.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void FirmwareTcp8899ConclusionRequiresCompleteExtractionAndSafetyAuthBridgeEvidence()
    {
        var complete = MiPlayS12FirmwareStaticEvidence.CreateCurrentSnapshot() with
        {
            LegacyTcp8899StringObserved = true,
            ModernSafetyAuthOpcodeStringObserved = true,
        };

        Assert.True(MiPlayS12FirmwareStaticEvidence.EvaluateForLegacyTcp8899SafetyAuth(complete).CanProceed);

        var incompleteRoot = MiPlayS12FirmwareStaticEvidence.EvaluateForLegacyTcp8899SafetyAuth(
            complete with { RootFsRecordsWalked = complete.RootSquashFsInodes - 1 });
        var incompleteBoot = MiPlayS12FirmwareStaticEvidence.EvaluateForLegacyTcp8899SafetyAuth(
            complete with { BootKernelSearched = false });
        var missingBridge = MiPlayS12FirmwareStaticEvidence.EvaluateForLegacyTcp8899SafetyAuth(
            complete with { LegacyTcp8899StringObserved = false });
        var missingSafetyAuth = MiPlayS12FirmwareStaticEvidence.EvaluateForLegacyTcp8899SafetyAuth(
            complete with
            {
                SafetyAuthStringObserved = false,
                ModernSafetyAuthOpcodeStringObserved = false,
            });

        Assert.False(incompleteRoot.CanProceed);
        Assert.Contains("rootfs extraction", incompleteRoot.Reason, StringComparison.Ordinal);
        Assert.False(incompleteBoot.CanProceed);
        Assert.Contains("boot image", incompleteBoot.Reason, StringComparison.Ordinal);
        Assert.False(missingBridge.CanProceed);
        Assert.Contains("8899", missingBridge.Reason, StringComparison.Ordinal);
        Assert.False(missingSafetyAuth.CanProceed);
        Assert.Contains("SafetyAuth", missingSafetyAuth.Reason, StringComparison.Ordinal);
    }
}
