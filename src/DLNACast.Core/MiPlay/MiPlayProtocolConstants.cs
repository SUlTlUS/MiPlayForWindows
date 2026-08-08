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
    public const ushort CloseDeviceCommand = 0x0002;

    // Basic playback state commands. The MirrorOS3 command-name table maps
    // 0x0004/0x0006 to Pause/Resume; these are distinct from the later
    // 0x0044/0x0046 media-player command family.
    public const ushort PauseCommand = 0x0004;
    public const ushort PauseAcknowledgementCommand = 0x0005;
    public const ushort ResumeCommand = 0x0006;
    public const ushort ResumeAcknowledgementCommand = 0x0007;

    // MirrorOS3 CmdSource::setVolume writes a raw four-byte big-endian value.
    // This is deliberately distinct from the tagged five-byte GetVolume response.
    public const ushort SetVolumeCommand = 0x000C;
    public const ushort SetVolumeAcknowledgementCommand = 0x000D;

    // Native post-auth keepalive commands; static evidence shows they still pass through SafetyData when enabled.
    public const ushort HeartbeatCommand = 0x001A;
    public const ushort HeartbeatAcknowledgementCommand = 0x001B;

    // Legacy-clear receiver status initialization. LX06 1.88.51 static dispatch
    // and the rooted 12.4.8.13 source capture agree on these command pairs.
    public const ushort GetVolumeCommand = 0x000E;
    public const ushort GetVolumeAcknowledgementCommand = 0x000F;
    public const ushort GetPositionCommand = 0x0010;
    public const ushort GetPositionAcknowledgementCommand = 0x0011;
    public const ushort SetMediaInfoCommand = 0x0012;
    public const ushort SetMediaInfoAcknowledgementCommand = 0x0013;
    public const ushort GetMediaInfoCommand = 0x0014;
    public const ushort GetMediaInfoAcknowledgementCommand = 0x0015;
    public const ushort GetStateCommand = 0x001C;
    public const ushort GetStateAcknowledgementCommand = 0x001D;

    // Native post-auth device-info commands reached from CmdSessionControl after CMD_SESSION_INFO_CONNECTED.
    public const ushort GetDeviceInfoCommand = 0x001E;
    public const ushort GetDeviceInfoAcknowledgementCommand = 0x001F;
    public const ushort SetLocalDeviceInfoCommand = 0x0058;
    public const ushort SetLocalDeviceInfoAcknowledgementCommand = 0x0059;

    // Native post-auth mirror-mode readiness commands observed in official phone capture.
    public const ushort GetMirrorModeCommand = 0x0034;
    public const ushort GetMirrorModeAcknowledgementCommand = 0x0035;

    // Device-side notify frames observed before SafetyAuth completes; native routes them to onRecvNotify.
    public const ushort NotifyCommand = 0x0022;

    // LX06 1.88.51 mpas source identity command pair; 0x0040 sends 0x0041 before JSON parsing.
    public const ushort SetPlaySourceCommand = 0x0040;
    public const ushort SetPlaySourceAcknowledgementCommand = 0x0041;

    // LX06 1.88.51 mpas local mirror preparation command pair.
    public const ushort AddMirrorCommand = 0x002E;
    public const ushort AddMirrorAcknowledgementCommand = 0x002F;

    // Legacy challenge/response that precedes the modern SafetyInfo exchange.
    public const ushort LegacySafetyChallengeCommand = 0x0028;
    public const ushort LegacySafetyAcknowledgementCommand = 0x0029;

    // Sent by CmdSource immediately after the TCP SessionInfo becomes available.
    public const ushort NativeSourceVersionCommand = 0x0036;
    public const ushort NativeSourceVersionAcknowledgementCommand = 0x0037;
    public const string NativeSourceVersion12_4_8_13 = "1.0.1123012";
    public const string NativeSourceVersion12_4_8_13Payload = NativeSourceVersion12_4_8_13 + "\0";
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
    public const int SystemAudioPlaybackDelayMicroseconds = 0;
}
