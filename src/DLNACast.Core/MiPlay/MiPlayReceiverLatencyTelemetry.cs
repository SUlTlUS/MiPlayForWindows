using System.Globalization;

namespace DLNACast.Core.MiPlay;

internal sealed record MiPlayReceiverLatencyTelemetrySample(
    int LatencyMilliseconds,
    long RtpPacketCount,
    int Bitrate);

/// <summary>
/// Keeps the latest receiver VIDEO_LATENCY report available to the media
/// diagnostics loop without coupling the RTSP reader to media pacing.
/// </summary>
internal sealed class MiPlayReceiverLatencyTelemetry
{
    private MiPlayReceiverLatencyTelemetrySample? latest;

    public MiPlayReceiverLatencyTelemetrySample? Latest => Volatile.Read(ref latest);

    public bool TryObserve(MiPlayRtspWireMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (!TryParse(message, out var sample))
        {
            return false;
        }

        Volatile.Write(ref latest, sample);
        return true;
    }

    internal static bool TryParse(
        MiPlayRtspWireMessage message,
        out MiPlayReceiverLatencyTelemetrySample? sample)
    {
        ArgumentNullException.ThrowIfNull(message);
        sample = null;

        var startLineParts = message.StartLine.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (startLineParts.Length != 3 ||
            !startLineParts[0].Equals("VIDEO_LATENCY", StringComparison.Ordinal) ||
            !startLineParts[2].Equals("RTSP/1.0", StringComparison.OrdinalIgnoreCase) ||
            message.Body.Length != 0 ||
            !int.TryParse(
                message.GetHeader("latency"),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var latencyMilliseconds) ||
            latencyMilliseconds < 0 ||
            !long.TryParse(
                message.GetHeader("rtppacketnum"),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var rtpPacketCount) ||
            rtpPacketCount < 0 ||
            !int.TryParse(
                message.GetHeader("bitrate"),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var bitrate) ||
            bitrate < -1)
        {
            return false;
        }

        sample = new MiPlayReceiverLatencyTelemetrySample(
            latencyMilliseconds,
            rtpPacketCount,
            bitrate);
        return true;
    }
}
