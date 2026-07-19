namespace DLNACast.Core.MiPlay;

/// <summary>
/// Transport mode values used by Xiaomi Interconnectivity Services 18.0.0.3.
/// The numeric values are passed from WifiDisplaySource to RTPSender.
/// </summary>
public enum MiPlayTransportMode
{
    Unknown = 0,
    Udp = 2,
    TcpInterleaved = 4,
    MptKcp = 5
}
