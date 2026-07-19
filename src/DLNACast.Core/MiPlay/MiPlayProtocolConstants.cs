namespace DLNACast.Core.MiPlay;

/// <summary>
/// Constants observed in Xiaomi Interconnectivity Services 18.0.0.3.
/// This protocol surface is experimental and is not wired into the normal DLNA cast path.
/// </summary>
public static class MiPlayProtocolConstants
{
    public const byte CommandFrameMagic = 0x24;
    public const int CommandHeaderLength = 9;
    public const ushort OpenDeviceCommand = 0;

    // Native post-auth keepalive commands; static evidence shows they still pass through SafetyData when enabled.
    public const ushort HeartbeatCommand = 0x001A;
    public const ushort HeartbeatAcknowledgementCommand = 0x001B;

    // Native post-auth device-info commands reached from CmdSessionControl after CMD_SESSION_INFO_CONNECTED.
    public const ushort GetDeviceInfoCommand = 0x001E;
    public const ushort SetLocalDeviceInfoCommand = 0x0058;

    // Legacy challenge/response that precedes the modern SafetyInfo exchange.
    public const ushort LegacySafetyChallengeCommand = 0x0028;
    public const ushort LegacySafetyAcknowledgementCommand = 0x0029;

    // Sent by CmdSource immediately after the TCP SessionInfo becomes available.
    public const ushort NativeSourceVersionCommand = 0x0036;
    public const ushort NativeSourceVersionAcknowledgementCommand = 0x0037;
    public const string NativeSourceVersion18_0_0_3 = "3.1.6030516";
    public const string NativeSourceVersion18_0_0_3Payload = NativeSourceVersion18_0_0_3 + "\0";

    public const ushort SafetyInfoCommand = 0x1400;
    public const ushort SafetyInfoAcknowledgementCommand = 0x1401;
    public const ushort SafetyAuthCommand = 0x1402;
    public const ushort SafetyAuthAcknowledgementCommand = 0x1403;
    public const byte SafetyValueType = 30;

    public const int DefaultControlPort = 8899;
    public const int DefaultMediaPort = 7236;

    public const int AacBitRate = 256_000;
    public const int SampleRate = 48_000;
    public const int Channels = 2;
    public const int BitsPerSample = 16;

    public const byte MpegTsRtpPayloadType = 33;
    public const int RtpHeaderLength = 12;
    public const int MpegTsPacketLength = 188;
    public const int MaximumRtpPacketLength = 1_472;
    public const int MpegTsPacketsPerRtpPacket =
        (MaximumRtpPacketLength - RtpHeaderLength) / MpegTsPacketLength;

    public const int FiveGigahertzPlaybackDelayMicroseconds = 800_000;
    public const int OtherNetworkPlaybackDelayMicroseconds = 1_000_000;
}
