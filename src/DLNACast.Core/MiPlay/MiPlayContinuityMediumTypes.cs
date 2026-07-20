namespace DLNACast.Core.MiPlay;

[Flags]
public enum MiPlayContinuityMediumType
{
    None = 0,
    Bluetooth = 1,
    Ble = 2,
    Mdns = 4,
    Nfc = 8,
    Uwb = 16,
    WifiP2P = 32,
    WifiHotspot = 64,
    WifiLan = 128,
    WifiLan1 = 256,
    WifiLan2 = 512,
    UltraSound = 1024,
    OutOfBand = 2048,
    WifiAware = 4096,
    Ethernet = 8192,
    WifiWlanOnP2P = 16384,
    WifiHotspot2 = 32768,
    WifiRestrict = 65536,
    BleApple = 131072,
    Remote = 262144,
    Cellular = 524288,
    WifiWlanOnWifiAware = 1048576,
}

/// <summary>
/// Offline constants mirrored from Mi Connect Service 5.1.251.10 Java
/// MediumType and native ChannelServer::RegisterNetbusListener evidence.
/// These values are diagnostics only and do not authorize Probe traffic.
/// </summary>
public static class MiPlayContinuityMediumTypes
{
    public const int AllMediumTypesMask = 0x1FFFFF;

    public const MiPlayContinuityMediumType RegisterNetbusListenerDefaultServerMediumMask =
        MiPlayContinuityMediumType.Bluetooth |
        MiPlayContinuityMediumType.Ble |
        MiPlayContinuityMediumType.WifiLan |
        MiPlayContinuityMediumType.Remote;

    public static bool HasType(
        MiPlayContinuityMediumType mask,
        MiPlayContinuityMediumType type) =>
        (mask & type) != 0;

    public static MiPlayContinuityMediumType GetMainMediumType(MiPlayContinuityMediumType type) =>
        type is MiPlayContinuityMediumType.WifiHotspot or
            MiPlayContinuityMediumType.WifiLan or
            MiPlayContinuityMediumType.WifiLan1 or
            MiPlayContinuityMediumType.WifiLan2 or
            MiPlayContinuityMediumType.Ethernet or
            MiPlayContinuityMediumType.WifiWlanOnP2P or
            MiPlayContinuityMediumType.WifiHotspot2 or
            MiPlayContinuityMediumType.WifiWlanOnWifiAware
            ? MiPlayContinuityMediumType.WifiLan
            : type;
}
