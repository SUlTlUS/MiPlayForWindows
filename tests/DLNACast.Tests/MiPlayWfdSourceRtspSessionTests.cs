using System.Net;
using System.Security.Cryptography;
using System.Text;
using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayWfdSourceRtspSessionTests
{
    private static readonly DateTimeOffset Second43 =
        new(2026, 8, 7, 6, 12, 43, TimeSpan.Zero);
    private static readonly DateTimeOffset Second44 = Second43.AddSeconds(1);

    [Fact]
    public void ReplaysCapturedSixteenStepHandshakeToReadyWithoutSockets()
    {
        var address = IPAddress.Parse("192.168.10.58");
        var session = new MiPlayWfdSourceRtspSession(address, 36_524, "588290182");

        var start = session.Start(Second43);
        Assert.True(start.Accepted);
        Assert.Single(start.OutboundMessages);
        Assert.Equal(
            "2267F3241E03DB32D0AC89A2F3DFFDD2E6F7C685562677EDB21FFDEB61371749",
            Hash(start.OutboundMessages[0]));

        var optionsAck = session.ProcessInbound(
            Response(1), Second44, 9_633_364_443);
        Assert.True(optionsAck.Accepted);
        Assert.Empty(optionsAck.OutboundMessages);

        var receiverOptions = Request(
            "OPTIONS * RTSP/1.0",
            [
                new("CSeq", "1"),
                new("Require", "org.wfa.wfd1.0"),
                new("lib_version", "audio-speaker-mico-cloud 2.1.4052010"),
            ]);
        var initialComplete = session.ProcessInbound(receiverOptions, Second44, 9_633_364_443);
        Assert.True(initialComplete.Accepted);
        Assert.Equal(MiPlayWfdSourceRtspPhase.AwaitingCapabilities, initialComplete.Phase);
        Assert.Equal(2, initialComplete.OutboundMessages.Count);
        Assert.Equal(
            [
                "E50C6B31A3CB83EEC7E9FE80B16978268F3E61A60AFE978E07317A15420BB004",
                "143274797AE02D907243A4B8313191C9741A97AB941A864CAE7A7D07FCA4F48B",
            ],
            initialComplete.OutboundMessages.Select(Hash));

        var capabilities = session.ProcessInbound(
            Response(2, Encoding.ASCII.GetBytes(MiPlayRealLegacyPlaybackSessionEvidence.ReceiverAudioCapabilities)),
            Second44,
            9_633_364_443);
        Assert.Equal(MiPlayWfdSourceRtspPhase.AwaitingSelectedParametersAcknowledgement, capabilities.Phase);
        Assert.Equal(
            "083C3B99EEAB800AFC7BE01980804AC8B4F56EF667A5B8888419D6A062E28E16",
            Hash(Assert.Single(capabilities.OutboundMessages)));

        var selectedAck = session.ProcessInbound(Response(3), Second44, 9_633_364_443);
        Assert.Equal(MiPlayWfdSourceRtspPhase.AwaitingSetupTriggerAcknowledgement, selectedAck.Phase);
        Assert.Equal(
            "A300B2C10458DDED329697AEF7046C168FF16A6D725D3B2ED59758EA0FE9B63D",
            Hash(Assert.Single(selectedAck.OutboundMessages)));

        var triggerAck = session.ProcessInbound(Response(4), Second44, 9_633_364_443);
        Assert.Equal(MiPlayWfdSourceRtspPhase.AwaitingSetup, triggerAck.Phase);
        Assert.Empty(triggerAck.OutboundMessages);

        var setup = Request(
            "SETUP rtsp://192.168.10.58/wfd1.0/streamid=0 RTSP/1.0",
            [
                new("CSeq", "2"),
                new("Transport", "RTP/AVP/TCP;interleaved=0-1"),
            ]);
        var setupResponse = session.ProcessInbound(setup, Second44, 9_633_364_443);
        Assert.Equal(MiPlayWfdSourceRtspPhase.AwaitingPlay, setupResponse.Phase);
        Assert.Equal(
            "5911B64278E1B409599962B929E39F2B68001ED99D94F11E9245516163D06815",
            Hash(Assert.Single(setupResponse.OutboundMessages)));

        var play = Request(
            "PLAY rtsp://192.168.10.58/wfd1.0/streamid=0 RTSP/1.0",
            [
                new("CSeq", "3"),
                new("Session", "588290182"),
            ]);
        var playResponse = session.ProcessInbound(play, Second44, 9_633_364_443);
        Assert.Equal(MiPlayWfdSourceRtspPhase.AwaitingTimeOffsetAcknowledgement, playResponse.Phase);
        Assert.Equal(9_633_364_443UL, session.TimeOffsetMicroseconds);
        Assert.Equal(
            [
                "B0E11D4FA020823F5E710462F1E753821932828AAEE4C3C0FC3A7F355E481933",
                "265A36D5E5C75B73E02C7695956A8F1089C9CC0943E90E95DA212C3227961454",
            ],
            playResponse.OutboundMessages.Select(Hash));

        var ready = session.ProcessInbound(Response(5), Second44, 9_633_364_443);
        Assert.True(ready.Accepted);
        Assert.True(ready.Ready);
        Assert.False(ready.SafeForNetworkUse);
        Assert.Equal(MiPlayWfdSourceRtspPhase.Ready, ready.Phase);
        Assert.Empty(ready.OutboundMessages);

        var latency = session.ProcessInbound(
            Request(
                "VIDEO_LATENCY rtsp://localhost/wfd1.0 RTSP/1.0",
                [
                    new("CSeq", "4"),
                    new("Content-Type", "text/parameters"),
                    new("latency", "0"),
                    new("bitrate", "-1"),
                    new("rtpPacketNum", "1"),
                    new("Content-Length", "0"),
                ]),
            Second44,
            9_633_364_443);
        Assert.True(latency.Accepted);
        Assert.True(latency.Ready);
        Assert.Empty(latency.OutboundMessages);
        Assert.Contains("without replying", latency.Boundary, StringComparison.Ordinal);
    }

    [Fact]
    public void InitialOptionsCanArriveBeforeSourceAcknowledgement()
    {
        var session = new MiPlayWfdSourceRtspSession(
            IPAddress.Parse("192.168.10.58"), 36_524, "588290182");
        session.Start(Second43);

        var receiverOptions = session.ProcessInbound(
            Request(
                "OPTIONS * RTSP/1.0",
                [
                    new("CSeq", "1"),
                    new("Require", "org.wfa.wfd1.0"),
                    new("lib_version", "audio-speaker-mico-cloud 2.1.4052010"),
                ]),
            Second44,
            1);
        Assert.Single(receiverOptions.OutboundMessages);
        Assert.Equal(MiPlayWfdSourceRtspPhase.AwaitingInitialOptionsExchange, receiverOptions.Phase);

        var acknowledgement = session.ProcessInbound(Response(1), Second44, 1);
        Assert.Single(acknowledgement.OutboundMessages);
        Assert.Equal(MiPlayWfdSourceRtspPhase.AwaitingCapabilities, acknowledgement.Phase);
    }

    [Fact]
    public void CapabilityMismatchStopsBeforeSelectingParameters()
    {
        var session = new MiPlayWfdSourceRtspSession(
            IPAddress.Parse("192.168.10.58"), 36_524, "588290182");
        session.Start(Second43);
        session.ProcessInbound(Response(1), Second44, 1);
        session.ProcessInbound(
            Request(
                "OPTIONS * RTSP/1.0",
                [
                    new("CSeq", "1"),
                    new("Require", "org.wfa.wfd1.0"),
                    new("lib_version", "receiver"),
                ]),
            Second44,
            1);

        var result = session.ProcessInbound(
            Response(2, "wfd_audio_codecs: LPCM 00000001 00\r\n"u8.ToArray()),
            Second44,
            1);

        Assert.False(result.Accepted);
        Assert.Equal(MiPlayWfdSourceRtspPhase.Stopped, result.Phase);
        Assert.Empty(result.OutboundMessages);
    }

    private static byte[] Request(
        string startLine,
        IReadOnlyList<MiPlayRtspWireHeader> headers) =>
        MiPlayRtspWireMessageCodec.Encode(startLine, headers, []);

    private static byte[] Response(int cseq, byte[]? body = null)
    {
        body ??= [];
        var headers = new List<MiPlayRtspWireHeader> { new("CSeq", cseq.ToString()) };
        if (body.Length != 0)
        {
            headers.Add(new("Content-Type", "text/parameters"));
            headers.Add(new("Content-Length", body.Length.ToString()));
        }
        return MiPlayRtspWireMessageCodec.Encode("RTSP/1.0 200 OK", headers, body);
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
