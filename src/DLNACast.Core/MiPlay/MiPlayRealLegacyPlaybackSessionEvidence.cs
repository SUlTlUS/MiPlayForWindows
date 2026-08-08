using System.Net;
using System.Security.Cryptography;

namespace DLNACast.Core.MiPlay;

public enum MiPlayObservedPlaybackDirection
{
    SourceToReceiver,
    ReceiverToSource,
}

public sealed record MiPlayObservedRtspStep(
    int Order,
    int CaptureLine,
    MiPlayObservedPlaybackDirection Direction,
    string StartLine,
    int CSeq,
    string? Body);

public sealed record MiPlayRealLegacyPlaybackSessionSnapshot(
    string ArtifactPath,
    string ArtifactSha256Hex,
    string SourceAddress,
    string SelectedReceiverAddress,
    int ControlPort,
    int SourceListenerPort,
    string SetPlaySourceJson,
    byte[] SetPlaySourceFrame,
    byte[] OpenFrame,
    IReadOnlyList<MiPlayObservedRtspStep> InitialRtspSteps,
    bool UsesLegacyClearControl,
    bool SetPlaySourceWasBroadcastToBothReceivers,
    bool OpenWasSentOnlyToSelectedReceiver,
    bool SetPlaySourceAcknowledgementObserved,
    bool OpenAcknowledgementObserved,
    bool AddMirrorObserved,
    bool ReceiverOpenedReverseRtsp,
    bool UsesUdpTimerResponder,
    bool UsesSeparateTcpAudioChannel,
    bool UsesAacMpegTsRtp,
    bool ContainsCapturedMediaBytes,
    bool SafeForNetworkUse);

/// <summary>
/// Hash-pinned, metadata-only evidence from a rooted MI PAD 4/Plus switching
/// its system media output to the LX06 at 192.168.10.4 and playing audio.
/// Captured AAC bytes are intentionally not embedded or returned.
/// </summary>
public static class MiPlayRealLegacyPlaybackSessionEvidence
{
    public const string ArtifactPath =
        "artifacts/phone_live/fresh-source-captures/mipad4-miplay-full-switch-playback-20260807-141132.strace";

    public const string ArtifactSha256Hex =
        "499252CB2EFE79EE443526BD58C9AED13EEFAED366F3CE2FDE3D4885454FD8E3";

    public const string SourceAddress = "192.168.10.58";
    public const string SelectedReceiverAddress = "192.168.10.4";
    public const string OtherReceiverAddress = "192.168.10.3";
    public const int SourceListenerPort = 7274;
    public const int TimerServerPort = 36524;

    public const ushort OtherReceiverSetPlaySourceSequence = 0x00c9;
    public const ushort SelectedReceiverSetPlaySourceSequence = 0x00bb;
    public const ushort SelectedReceiverOpenSequence = 0x00bc;

    public const string SetPlaySourceRefChannel = "controlcenter";
    public const string SetPlaySourceRefFunction = "single_room";
    public const string SetPlaySourceRefContent = "music_wangyiyun";
    public const string SetPlaySourceJson =
        "{\"ref_channel\":\"controlcenter\",\"ref_function\":\"single_room\",\"ref_content\":\"music_wangyiyun\"}";

    public const string SetPlaySourcePayloadSha256Hex =
        "DD3F97B8E79BC9C7D5B4D451CF407DA5268C075FB1989B1820D5C14808B2EFCE";
    public const string OtherReceiverSetPlaySourceFrameSha256Hex =
        "31CDD2065352C7A9BA32D6F18AE95F3F769122C2DDFC9DA58FCAF43A93CBE84E";
    public const string SelectedReceiverSetPlaySourceFrameSha256Hex =
        "D134EC00B1A41A21877DE8013CFB59D2C5F43ED9AC4406588AC1DB6E066664F6";

    public const string OpenPayloadText = "wfd://192.168.10.58:7274?mirrorMode=1";
    public const string OpenPayloadSha256Hex =
        "4CEBDEA18AC93FE7267749A3D1B53AAFB7B0A76E7E548F10DDE533F97A482E0D";
    public const string OpenFrameSha256Hex =
        "CFFE210A6F5B64D885873743A4831D08054E552397BD32AA63113CFD219F32BF";

    public const string ReceiverAudioCapabilities =
        "wfd_audio_codecs: AAC 00000001 00\r\n" +
        "wfd_video_formats: none\r\n" +
        "wfd_video_enctype: none\r\n" +
        "wfd_video_gamuttype: none\r\n" +
        "wfd_video_bitrate: none\r\n" +
        "wfd_current_video_info: none\r\n" +
        "wfd_client_rtp_ports: RTP/AVP/TCP;interleaved mode=play\r\n" +
        "miplay_support_image: none\r\n" +
        "wfd_standby_resume_capability: supported\r\n" +
        "device_info: -1 -1 -1 -1 -1 -1 -1\r\n";

    public const string SourceSelectedParameters =
        "wfd_audio_codecs: AAC 00000001 00\r\n" +
        "wfd_client_rtp_ports: RTP/AVP/TCP;interleaved mode=play\r\n" +
        "wfd_presentation_URL: rtsp://192.168.10.58/wfd1.0/streamid=0 none\r\n";

    public static MiPlayRealLegacyPlaybackSessionSnapshot CreateCurrentSnapshot()
    {
        var setPlaySourcePayload = MiPlaySetPlaySourcePayloadCodec.Encode(
            SetPlaySourceRefChannel,
            SetPlaySourceRefFunction,
            SetPlaySourceRefContent);
        var setPlaySourceFrame = MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.SetPlaySourceCommand,
            SelectedReceiverSetPlaySourceSequence,
            setPlaySourcePayload);
        var openFrame = new MiPlayOpenDeviceRequest(
                IPAddress.Parse(SourceAddress),
                SourceListenerPort)
            .ToCommandFrame(SelectedReceiverOpenSequence);

        return new MiPlayRealLegacyPlaybackSessionSnapshot(
            ArtifactPath,
            ArtifactSha256Hex,
            SourceAddress,
            SelectedReceiverAddress,
            MiPlayProtocolConstants.DefaultControlPort,
            SourceListenerPort,
            SetPlaySourceJson,
            setPlaySourceFrame,
            openFrame,
            CreateInitialRtspSteps(),
            UsesLegacyClearControl: true,
            SetPlaySourceWasBroadcastToBothReceivers: true,
            OpenWasSentOnlyToSelectedReceiver: true,
            SetPlaySourceAcknowledgementObserved: false,
            OpenAcknowledgementObserved: false,
            AddMirrorObserved: false,
            ReceiverOpenedReverseRtsp: true,
            UsesUdpTimerResponder: true,
            UsesSeparateTcpAudioChannel: true,
            UsesAacMpegTsRtp: true,
            ContainsCapturedMediaBytes: false,
            SafeForNetworkUse: false);
    }

    public static bool MatchesPinnedHashes(MiPlayRealLegacyPlaybackSessionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return Hash(snapshot.SetPlaySourceFrame) == SelectedReceiverSetPlaySourceFrameSha256Hex &&
               Hash(snapshot.SetPlaySourceFrame.AsSpan(MiPlayProtocolConstants.CommandHeaderLength)) ==
                   SetPlaySourcePayloadSha256Hex &&
               Hash(snapshot.OpenFrame) == OpenFrameSha256Hex &&
               Hash(snapshot.OpenFrame.AsSpan(MiPlayProtocolConstants.CommandHeaderLength)) ==
                   OpenPayloadSha256Hex;
    }

    private static IReadOnlyList<MiPlayObservedRtspStep> CreateInitialRtspSteps() =>
        [
            new(1, 1536, MiPlayObservedPlaybackDirection.SourceToReceiver,
                "OPTIONS * RTSP/1.0", 1, null),
            new(2, 1558, MiPlayObservedPlaybackDirection.ReceiverToSource,
                "RTSP/1.0 200 OK", 1, null),
            new(3, 1559, MiPlayObservedPlaybackDirection.ReceiverToSource,
                "OPTIONS * RTSP/1.0", 1, null),
            new(4, 1560, MiPlayObservedPlaybackDirection.SourceToReceiver,
                "RTSP/1.0 200 OK", 1, null),
            new(5, 1561, MiPlayObservedPlaybackDirection.SourceToReceiver,
                "GET_PARAMETER rtsp://localhost/wfd1.0 RTSP/1.0", 2,
                "wfd_video_formats\r\nwfd_audio_codecs\r\nwfd_client_rtp_ports\r\n" +
                "wfd_tcp_enable\r\nwfd_tcp_multi_session_enable\r\nwfd_image_enable\r\n" +
                "wfd_dynamic_video_enable\r\nwfd_standby_resume_capability\r\n" +
                "wfd_video_bitrate\r\nwfd_current_video_info\r\n"),
            new(6, 1562, MiPlayObservedPlaybackDirection.ReceiverToSource,
                "RTSP/1.0 200 OK", 2, ReceiverAudioCapabilities),
            new(7, 1569, MiPlayObservedPlaybackDirection.SourceToReceiver,
                "SET_PARAMETER rtsp://localhost/wfd1.0 RTSP/1.0", 3, SourceSelectedParameters),
            new(8, 1570, MiPlayObservedPlaybackDirection.ReceiverToSource,
                "RTSP/1.0 200 OK", 3, null),
            new(9, 1571, MiPlayObservedPlaybackDirection.SourceToReceiver,
                "SET_PARAMETER rtsp://localhost/wfd1.0 RTSP/1.0", 4,
                "wfd_trigger_method: SETUP\r\n"),
            new(10, 1572, MiPlayObservedPlaybackDirection.ReceiverToSource,
                "RTSP/1.0 200 OK", 4, null),
            new(11, 1573, MiPlayObservedPlaybackDirection.ReceiverToSource,
                "SETUP rtsp://192.168.10.58/wfd1.0/streamid=0 RTSP/1.0", 2, null),
            new(12, 1574, MiPlayObservedPlaybackDirection.SourceToReceiver,
                "RTSP/1.0 200 OK", 2, null),
            new(13, 1575, MiPlayObservedPlaybackDirection.ReceiverToSource,
                "PLAY rtsp://192.168.10.58/wfd1.0/streamid=0 RTSP/1.0", 3, null),
            new(14, 1576, MiPlayObservedPlaybackDirection.SourceToReceiver,
                "RTSP/1.0 200 OK", 3, null),
            new(15, 1584, MiPlayObservedPlaybackDirection.SourceToReceiver,
                "TIME_OFFSET rtsp://localhost/wfd1.0 RTSP/1.0", 5, null),
            new(16, 1586, MiPlayObservedPlaybackDirection.ReceiverToSource,
                "RTSP/1.0 200 OK", 5, null),
        ];

    private static string Hash(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes));
}
