using System.Text;
using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayReceiverLatencyTelemetryTests
{
    [Fact]
    public void ParsesTheCapturedPerReceiverVideoLatencyHeaders()
    {
        var telemetry = new MiPlayReceiverLatencyTelemetry();
        var message = Decode(
            "VIDEO_LATENCY rtsp://localhost/wfd1.0 RTSP/1.0\r\n" +
            "CSeq: 9\r\n" +
            "Session: 588290182\r\n" +
            "latency: 469\r\n" +
            "rtppacketnum: 98\r\n" +
            "bitrate: -1\r\n\r\n");

        Assert.True(telemetry.TryObserve(message));
        Assert.Equal(
            new MiPlayReceiverLatencyTelemetrySample(469, 98, -1),
            telemetry.Latest);
    }

    [Theory]
    [InlineData("GET_PARAMETER rtsp://localhost/wfd1.0 RTSP/1.0", "469", "98", "-1")]
    [InlineData("VIDEO_LATENCY rtsp://localhost/wfd1.0 RTSP/1.0", "-1", "98", "-1")]
    [InlineData("VIDEO_LATENCY rtsp://localhost/wfd1.0 RTSP/1.0", "469", "invalid", "-1")]
    [InlineData("VIDEO_LATENCY rtsp://localhost/wfd1.0 RTSP/1.0", "469", "98", "-2")]
    public void IgnoresNonTelemetryAndInvalidReceiverValues(
        string startLine,
        string latency,
        string rtpPacketCount,
        string bitrate)
    {
        var telemetry = new MiPlayReceiverLatencyTelemetry();
        var message = new MiPlayRtspWireMessage(
            startLine,
            [
                new("latency", latency),
                new("rtppacketnum", rtpPacketCount),
                new("bitrate", bitrate),
            ],
            []);

        Assert.False(telemetry.TryObserve(message));
        Assert.Null(telemetry.Latest);
    }

    [Fact]
    public void InvalidFollowUpDoesNotEraseTheLastValidReceiverReport()
    {
        var telemetry = new MiPlayReceiverLatencyTelemetry();
        var valid = new MiPlayRtspWireMessage(
            "VIDEO_LATENCY rtsp://localhost/wfd1.0 RTSP/1.0",
            [new("latency", "298"), new("rtppacketnum", "74"), new("bitrate", "-1")],
            []);
        var malformed = valid with
        {
            Headers = [new("latency", "bad"), new("rtppacketnum", "75"), new("bitrate", "-1")],
        };

        Assert.True(telemetry.TryObserve(valid));
        Assert.False(telemetry.TryObserve(malformed));
        Assert.Equal(
            new MiPlayReceiverLatencyTelemetrySample(298, 74, -1),
            telemetry.Latest);
    }

    private static MiPlayRtspWireMessage Decode(string wireText)
    {
        var bytes = Encoding.ASCII.GetBytes(wireText);
        Assert.True(MiPlayRtspWireMessageCodec.TryDecode(bytes, out var message, out var consumed));
        Assert.Equal(bytes.Length, consumed);
        return Assert.IsType<MiPlayRtspWireMessage>(message);
    }
}
