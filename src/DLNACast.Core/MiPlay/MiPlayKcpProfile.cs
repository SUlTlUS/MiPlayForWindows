namespace DLNACast.Core.MiPlay;

/// <summary>
/// KCP parameters configured by libmpt in Xiaomi Interconnectivity Services
/// 18.0.0.3 for the RTP/AVP/MPT transport.
/// </summary>
public static class MiPlayKcpProfile
{
    public const uint ConversationId = 0x1234;
    public const int MaximumTransmissionUnit = 1_400;
    public const int SendWindow = 256;
    public const int ReceiveWindow = 256;
    public const int UpdateIntervalMilliseconds = 10;
    public const int FastResend = 1;
    public const int MinimumRetransmissionTimeoutMilliseconds = 100;
    public const bool NoDelay = false;
    public const bool DisableCongestionWindow = true;
}
