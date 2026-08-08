using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Text;

namespace DLNACast.Core.MiPlay;

/// <summary>
/// Pure byte factories for the source half of the rooted-phone WFD/RTSP
/// handshake. They open no listener and send no control or media traffic.
/// </summary>
public static class MiPlayWfdSourceRtspMessages
{
    public const string WfdTarget = "rtsp://localhost/wfd1.0";
    public const string StreamTarget = "rtsp://192.168.10.58/wfd1.0/streamid=0";
    public const string SourcePublicMethods =
        "org.wfa.wfd1.0, SETUP, TEARDOWN, PLAY, PAUSE, GET_PARAMETER, SET_PARAMETER";

    public const string CapabilityQueryBody =
        "wfd_video_formats\r\n" +
        "wfd_audio_codecs\r\n" +
        "wfd_client_rtp_ports\r\n" +
        "wfd_tcp_enable\r\n" +
        "wfd_tcp_multi_session_enable\r\n" +
        "wfd_image_enable\r\n" +
        "wfd_dynamic_video_enable\r\n" +
        "wfd_standby_resume_capability\r\n" +
        "wfd_video_bitrate\r\n" +
        "wfd_current_video_info\r\n";

    public static byte[] EncodeOptions(
        DateTimeOffset timestamp,
        IPAddress sourceAddress,
        int timerPort)
    {
        ArgumentNullException.ThrowIfNull(sourceAddress);
        var addressBytes = sourceAddress.GetAddressBytes();
        if (addressBytes.Length != 4)
        {
            throw new NotSupportedException("The captured WFD timer header supports IPv4 only.");
        }
        if (timerPort is < IPEndPoint.MinPort or > IPEndPoint.MaxPort)
        {
            throw new ArgumentOutOfRangeException(nameof(timerPort));
        }

        var addressValue = BinaryPrimitives.ReadUInt32BigEndian(addressBytes);
        return MiPlayRtspWireMessageCodec.Encode(
            "OPTIONS * RTSP/1.0",
            [
                Header("Date", FormatDate(timestamp)),
                Header("Server", ""),
                Header("CSeq", "1"),
                Header("Require", "org.wfa.wfd1.0"),
                new MiPlayRtspWireHeader(
                    "wfd_timer_server_port",
                    $"{addressValue.ToString(CultureInfo.InvariantCulture)}:{timerPort.ToString(CultureInfo.InvariantCulture)}",
                    SpaceAfterColon: false),
            ],
            []);
    }

    public static byte[] EncodeOptionsResponse(DateTimeOffset timestamp) =>
        MiPlayRtspWireMessageCodec.Encode(
            "RTSP/1.0 200 OK",
            [
                Header("Date", FormatDate(timestamp)),
                Header("Server", ""),
                Header("CSeq", "1"),
                Header("Public", SourcePublicMethods),
            ],
            []);

    public static byte[] EncodeCapabilityQuery(DateTimeOffset timestamp)
    {
        var body = Encoding.ASCII.GetBytes(CapabilityQueryBody);
        return EncodeParametersRequest("GET_PARAMETER", 2, timestamp, body);
    }

    public static byte[] EncodeSelectedParameters(
        DateTimeOffset timestamp,
        IPAddress sourceAddress)
    {
        ArgumentNullException.ThrowIfNull(sourceAddress);
        if (sourceAddress.GetAddressBytes().Length != 4)
        {
            throw new NotSupportedException("The captured WFD presentation URL supports IPv4 only.");
        }

        var body = Encoding.ASCII.GetBytes(
            "wfd_audio_codecs: AAC 00000001 00\r\n" +
            "wfd_client_rtp_ports: RTP/AVP/TCP;interleaved mode=play\r\n" +
            $"wfd_presentation_URL: rtsp://{sourceAddress}/wfd1.0/streamid=0 none\r\n");
        return EncodeParametersRequest("SET_PARAMETER", 3, timestamp, body);
    }

    public static byte[] EncodeSetupTrigger(DateTimeOffset timestamp)
    {
        var body = "wfd_trigger_method: SETUP\r\n"u8;
        return EncodeParametersRequest("SET_PARAMETER", 4, timestamp, body);
    }

    public static byte[] EncodeSetupResponse(
        DateTimeOffset timestamp,
        int cseq,
        string sessionId,
        string transport)
    {
        ValidateSession(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(transport);
        return MiPlayRtspWireMessageCodec.Encode(
            "RTSP/1.0 200 OK",
            [
                Header("Date", FormatDate(timestamp)),
                Header("Server", ""),
                Header("CSeq", cseq.ToString(CultureInfo.InvariantCulture)),
                Header("Session", $"{sessionId};timeout=60"),
                Header("Transport", transport.EndsWith(';') ? transport : transport + ";"),
            ],
            []);
    }

    public static byte[] EncodePlayResponse(
        DateTimeOffset timestamp,
        int cseq,
        string sessionId)
    {
        ValidateSession(sessionId);
        return MiPlayRtspWireMessageCodec.Encode(
            "RTSP/1.0 200 OK",
            [
                Header("Date", FormatDate(timestamp)),
                Header("Server", ""),
                Header("CSeq", cseq.ToString(CultureInfo.InvariantCulture)),
                Header("Session", $"{sessionId};timeout=60"),
                Header("Range", "npt=now-"),
            ],
            []);
    }

    public static byte[] EncodeTimeOffset(
        DateTimeOffset timestamp,
        ulong monotonicMicroseconds)
    {
        return MiPlayRtspWireMessageCodec.Encode(
            $"TIME_OFFSET {WfdTarget} RTSP/1.0",
            [
                Header("Date", FormatDate(timestamp)),
                Header("Server", ""),
                Header("CSeq", "5"),
                Header("Content-Type", "text/parameters"),
                new MiPlayRtspWireHeader(
                    "TimeOffset",
                    monotonicMicroseconds.ToString(CultureInfo.InvariantCulture),
                    SpaceAfterColon: false),
                Header("Content-Length", "0"),
            ],
            []);
    }

    public static byte[] EncodeReceiverRequestResponse(
        DateTimeOffset timestamp,
        int cseq,
        string? sessionId = null)
    {
        if (cseq < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cseq));
        }
        if (sessionId is not null)
        {
            ValidateSession(sessionId);
        }

        var headers = new List<MiPlayRtspWireHeader>
        {
            Header("Date", FormatDate(timestamp)),
            Header("Server", ""),
            Header("CSeq", cseq.ToString(CultureInfo.InvariantCulture)),
        };
        if (sessionId is not null)
        {
            headers.Add(Header("Session", $"{sessionId};timeout=60"));
        }
        headers.Add(Header("Content-Length", "0"));

        return MiPlayRtspWireMessageCodec.Encode("RTSP/1.0 200 OK", headers, []);
    }

    public static string FormatDate(DateTimeOffset timestamp) =>
        timestamp.ToUniversalTime().ToString(
            "ddd, dd MMM yyyy HH:mm:ss '+0000'",
            CultureInfo.InvariantCulture);

    private static byte[] EncodeParametersRequest(
        string method,
        int cseq,
        DateTimeOffset timestamp,
        ReadOnlySpan<byte> body) =>
        MiPlayRtspWireMessageCodec.Encode(
            $"{method} {WfdTarget} RTSP/1.0",
            [
                Header("Date", FormatDate(timestamp)),
                Header("Server", ""),
                Header("CSeq", cseq.ToString(CultureInfo.InvariantCulture)),
                Header("Content-Type", "text/parameters"),
                Header("Content-Length", body.Length.ToString(CultureInfo.InvariantCulture)),
            ],
            body);

    private static MiPlayRtspWireHeader Header(string name, string value) => new(name, value);

    private static void ValidateSession(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        if (sessionId.Contains('\r', StringComparison.Ordinal) ||
            sessionId.Contains('\n', StringComparison.Ordinal) ||
            sessionId.Contains(';', StringComparison.Ordinal))
        {
            throw new ArgumentException("The RTSP session id must be one token.", nameof(sessionId));
        }
    }
}
