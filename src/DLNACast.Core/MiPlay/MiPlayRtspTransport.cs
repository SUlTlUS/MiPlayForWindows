using System.Globalization;

namespace DLNACast.Core.MiPlay;

/// <summary>
/// The subset of an RTSP SETUP Transport header consumed by Xiaomi's
/// WifiDisplaySource. MPT is Xiaomi's KCP-backed transport in version 18.0.0.3.
/// </summary>
public sealed record MiPlayRtspTransport(
    MiPlayTransportMode Mode,
    int? ClientRtpPort,
    int? ClientRtcpPort,
    int? UserId,
    string RawValue)
{
    public const int LegacyDefaultClientPort = 19_000;

    public static bool TryParse(string? value, out MiPlayRtspTransport? transport)
    {
        transport = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var segments = value.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return false;
        }

        var protocol = segments[0];
        MiPlayTransportMode mode;
        int? defaultClientPort = null;

        if (protocol.Equals("RTP/AVP/TCP", StringComparison.OrdinalIgnoreCase))
        {
            mode = MiPlayTransportMode.TcpInterleaved;
        }
        else if (protocol.Equals("RTP/AVP/UDP", StringComparison.OrdinalIgnoreCase))
        {
            mode = MiPlayTransportMode.Udp;
        }
        else if (protocol.Equals("RTP/AVP/MPT", StringComparison.OrdinalIgnoreCase))
        {
            mode = MiPlayTransportMode.MptKcp;
        }
        else if (protocol.Equals("RTP/AVP", StringComparison.OrdinalIgnoreCase))
        {
            mode = MiPlayTransportMode.Udp;
            defaultClientPort = LegacyDefaultClientPort;
        }
        else
        {
            return false;
        }

        int? clientRtpPort = null;
        int? clientRtcpPort = null;
        int? userId = null;

        foreach (var segment in segments.Skip(1))
        {
            if (TryReadParameter(segment, "client_port", out var ports))
            {
                if (!TryParsePorts(ports, out clientRtpPort, out clientRtcpPort))
                {
                    return false;
                }
            }
            else if (TryReadParameter(segment, "userid", out var userIdValue))
            {
                if (!int.TryParse(userIdValue, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedUserId) ||
                    parsedUserId < 0)
                {
                    return false;
                }

                userId = parsedUserId;
            }
        }

        clientRtpPort ??= defaultClientPort;
        transport = new MiPlayRtspTransport(mode, clientRtpPort, clientRtcpPort, userId, value);
        return true;
    }

    private static bool TryReadParameter(string segment, string name, out string value)
    {
        var separator = segment.IndexOf('=');
        if (separator <= 0 || !segment.AsSpan(0, separator).Equals(name, StringComparison.OrdinalIgnoreCase))
        {
            value = string.Empty;
            return false;
        }

        value = segment[(separator + 1)..].Trim();
        return true;
    }

    private static bool TryParsePorts(string value, out int? rtpPort, out int? rtcpPort)
    {
        rtpPort = null;
        rtcpPort = null;

        var separator = value.IndexOf('-');
        var rtpText = separator < 0 ? value : value[..separator];
        if (!TryParsePort(rtpText, out var parsedRtpPort))
        {
            return false;
        }

        rtpPort = parsedRtpPort;
        if (separator < 0)
        {
            return true;
        }

        if (separator == value.Length - 1 || !TryParsePort(value[(separator + 1)..], out var parsedRtcpPort))
        {
            return false;
        }

        rtcpPort = parsedRtcpPort;
        return true;
    }

    private static bool TryParsePort(string value, out int port) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out port) &&
        port is > 0 and <= ushort.MaxValue;
}
