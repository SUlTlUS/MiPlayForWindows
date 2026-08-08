namespace DLNACast.Core.MiPlay;

public sealed record MiPlayS12FirmwareStaticSnapshot(
    string FirmwareFileName,
    string Sha256,
    string Hardware,
    string RomVersion,
    string Channel,
    string BuildTime,
    string GitTag,
    long RootSquashFsOffset,
    long RootSquashFsBytesUsed,
    int RootSquashFsInodes,
    bool RootSquashFsExtractionComplete,
    int RootFsRecordsWalked,
    int RootFsDirectories,
    int RootFsFiles,
    int RootFsSymlinks,
    int RootFsExtractionWarnings,
    long AndroidBootOffset,
    int BootRamdiskRecords,
    int BootRamdiskFiles,
    int BootRamdiskWarnings,
    bool BootKernelSearched,
    bool DeviceSideDlnaServiceObserved,
    bool DeviceSideMdplayServiceObserved,
    bool DeviceSideMdplayGetDeviceInfoObserved,
    bool DeviceSideMdplayUsesIotdcmObserved,
    bool DeviceSideMiotTokenV1Observed,
    bool IotdcmSecurityKeyStringObserved,
    bool IotdcmServiceKeyStringObserved,
    bool LegacyTcp8899StringObserved,
    bool SafetyAuthStringObserved,
    bool SafetyDataStringObserved,
    bool ModernSafetyAuthOpcodeStringObserved,
    bool BootRamdiskSafetyAuthStringObserved,
    bool BootKernelSafetyAuthStringObserved);

/// <summary>
/// Offline-only facts from the LX06/S12 firmware image. This class deliberately
/// models static evidence only: it does not parse live frames, open sockets, or
/// claim that receiver-side firmware strings prove the phone-side MiPlay
/// business-client command path.
/// </summary>
public static class MiPlayS12FirmwareStaticEvidence
{
    public const string FirmwareFileName = "mico_all_b9cbb_1.74.1.bin";
    public const string FirmwareSha256 =
        "73058C64CBED0CFC915A0E7F162FEF21F01DCA28B477377DA6285B115083624C";

    public const string Hardware = "LX06";
    public const string RomVersion = "1.74.1";
    public const string Channel = "release";
    public const string BuildTime = "Sun, 25 Apr 2021 23:11:23 +0800";
    public const string GitTag = "commit b9e9b6640c2491c7a77a22612e47790e6c8c0356";

    public const long RootSquashFsOffset = 0x2b8;
    public const long RootSquashFsBytesUsed = 36_993_414;
    public const int RootSquashFsInodes = 1_996;
    public const int RootFsRecordsWalked = 1_996;
    public const int RootFsDirectories = 111;
    public const int RootFsFiles = 1_245;
    public const int RootFsSymlinks = 639;
    public const int RootFsExtractionWarnings = 0;

    public const long AndroidBootOffset = 36_994_184;
    public const int BootRamdiskRecords = 139;
    public const int BootRamdiskFiles = 31;
    public const int BootRamdiskWarnings = 0;

    public const string DeviceSideDlnaInitScript = "etc/init.d/dlnainit";
    public const string DeviceSideDlnaBinary = "usr/bin/dlna";
    public const string DeviceSideMdplayInitScript = "etc/init.d/mdplay";
    public const string DeviceSideMdplayBinary = "usr/bin/mdplay";
    public const string DeviceSideIotdcmMdplayLibrary = "usr/lib/libiotdcm_mdplay.so";
    public const string DeviceSideIotdcmLibrary = "usr/lib/libiotdcm.so";

    public const string MdplayGetDeviceInfoSymbol = "GetDeviceInfo";
    public const string MdplayDeviceIdSymbol = "MdplayGetDeviceId";
    public const string MdplayAppIdSymbol = "MdplayGetAppid";
    public const string MdplayTokenIdSymbol = "MdplayGetTokenId";
    public const string MdplayUserIdSymbol = "MdplayGetUserId";
    public const string MdplayMiotTokenV1Format =
        "Authorization: MIOT-TOKEN-V1 app_id:%s,token:%s,session_id:%s";
    public const string MdplayIotdcmCreateFormat =
        "iotdcm_create user_id:%lld app_id:%s dev_id:%s token:%s udp_cb:%p";

    public static MiPlayS12FirmwareStaticSnapshot CreateCurrentSnapshot() =>
        new(
            FirmwareFileName: FirmwareFileName,
            Sha256: FirmwareSha256,
            Hardware: Hardware,
            RomVersion: RomVersion,
            Channel: Channel,
            BuildTime: BuildTime,
            GitTag: GitTag,
            RootSquashFsOffset: RootSquashFsOffset,
            RootSquashFsBytesUsed: RootSquashFsBytesUsed,
            RootSquashFsInodes: RootSquashFsInodes,
            RootSquashFsExtractionComplete: true,
            RootFsRecordsWalked: RootFsRecordsWalked,
            RootFsDirectories: RootFsDirectories,
            RootFsFiles: RootFsFiles,
            RootFsSymlinks: RootFsSymlinks,
            RootFsExtractionWarnings: RootFsExtractionWarnings,
            AndroidBootOffset: AndroidBootOffset,
            BootRamdiskRecords: BootRamdiskRecords,
            BootRamdiskFiles: BootRamdiskFiles,
            BootRamdiskWarnings: BootRamdiskWarnings,
            BootKernelSearched: true,
            DeviceSideDlnaServiceObserved: true,
            DeviceSideMdplayServiceObserved: true,
            DeviceSideMdplayGetDeviceInfoObserved: true,
            DeviceSideMdplayUsesIotdcmObserved: true,
            DeviceSideMiotTokenV1Observed: true,
            IotdcmSecurityKeyStringObserved: true,
            IotdcmServiceKeyStringObserved: true,
            LegacyTcp8899StringObserved: false,
            SafetyAuthStringObserved: false,
            SafetyDataStringObserved: false,
            ModernSafetyAuthOpcodeStringObserved: false,
            BootRamdiskSafetyAuthStringObserved: false,
            BootKernelSafetyAuthStringObserved: false);

    public static MiPlayIdmStateDecision EvaluateForLegacyTcp8899SafetyAuth(
        MiPlayS12FirmwareStaticSnapshot snapshot)
    {
        if (!snapshot.RootSquashFsExtractionComplete ||
            snapshot.RootFsRecordsWalked != snapshot.RootSquashFsInodes ||
            snapshot.RootFsExtractionWarnings != 0)
        {
            return new MiPlayIdmStateDecision(false, "The firmware rootfs extraction is not complete enough for a static protocol conclusion.");
        }

        if (!snapshot.BootKernelSearched ||
            snapshot.BootRamdiskWarnings != 0)
        {
            return new MiPlayIdmStateDecision(false, "The boot image/ramdisk search is incomplete.");
        }

        if (!snapshot.LegacyTcp8899StringObserved)
        {
            return new MiPlayIdmStateDecision(false, "No legacy TCP 8899 command bridge was observed in rootfs, boot ramdisk, or decompressed kernel strings.");
        }

        if (!snapshot.SafetyAuthStringObserved &&
            !snapshot.ModernSafetyAuthOpcodeStringObserved)
        {
            return new MiPlayIdmStateDecision(false, "No SafetyAuth symbol or 0x1400-0x1403 opcode string evidence was observed in the firmware image.");
        }

        return new MiPlayIdmStateDecision(
            true,
            "The firmware contains static evidence for a legacy TCP 8899 SafetyAuth command bridge.");
    }

    public static MiPlayIdmStateDecision EvaluateForDeviceSidePostAuthIdentity(
        MiPlayS12FirmwareStaticSnapshot snapshot)
    {
        if (!snapshot.DeviceSideMdplayServiceObserved ||
            !snapshot.DeviceSideMdplayGetDeviceInfoObserved)
        {
            return new MiPlayIdmStateDecision(false, "The firmware does not expose a device-side mdplay GetDeviceInfo path.");
        }

        if (!snapshot.DeviceSideMdplayUsesIotdcmObserved)
        {
            return new MiPlayIdmStateDecision(false, "The mdplay GetDeviceInfo evidence is not tied to iotdcm startup.");
        }

        if (!snapshot.DeviceSideMiotTokenV1Observed)
        {
            return new MiPlayIdmStateDecision(false, "The mdplay identity evidence is missing MIOT-TOKEN-V1 app/token/session context.");
        }

        return new MiPlayIdmStateDecision(
            true,
            "The firmware can inform device-side mdplay/iotdcm identity prerequisites, but it does not prove the legacy 8899 SafetyAuth path.");
    }
}
