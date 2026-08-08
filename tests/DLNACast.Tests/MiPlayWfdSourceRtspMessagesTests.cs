using System.Net;
using System.Security.Cryptography;
using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayWfdSourceRtspMessagesTests
{
    private static readonly DateTimeOffset CapturedSecond43 =
        new(2026, 8, 7, 6, 12, 43, TimeSpan.Zero);

    private static readonly DateTimeOffset CapturedSecond44 =
        CapturedSecond43.AddSeconds(1);

    [Fact]
    public void RebuildsAllCapturedSourceHandshakeMessagesByteForByte()
    {
        var sourceAddress = IPAddress.Parse("192.168.10.58");
        var messages = new[]
        {
            MiPlayWfdSourceRtspMessages.EncodeOptions(CapturedSecond43, sourceAddress, 36_524),
            MiPlayWfdSourceRtspMessages.EncodeOptionsResponse(CapturedSecond44),
            MiPlayWfdSourceRtspMessages.EncodeCapabilityQuery(CapturedSecond44),
            MiPlayWfdSourceRtspMessages.EncodeSelectedParameters(CapturedSecond44, sourceAddress),
            MiPlayWfdSourceRtspMessages.EncodeSetupTrigger(CapturedSecond44),
            MiPlayWfdSourceRtspMessages.EncodeSetupResponse(
                CapturedSecond44,
                cseq: 2,
                sessionId: "588290182",
                transport: "RTP/AVP/TCP;interleaved=0-1"),
            MiPlayWfdSourceRtspMessages.EncodePlayResponse(
                CapturedSecond44,
                cseq: 3,
                sessionId: "588290182"),
            MiPlayWfdSourceRtspMessages.EncodeTimeOffset(
                CapturedSecond44,
                monotonicMicroseconds: 9_633_364_443),
        };

        Assert.Equal([145, 161, 383, 319, 186, 149, 125, 179], messages.Select(message => message.Length));
        Assert.Equal(
            [
                "2267F3241E03DB32D0AC89A2F3DFFDD2E6F7C685562677EDB21FFDEB61371749",
                "E50C6B31A3CB83EEC7E9FE80B16978268F3E61A60AFE978E07317A15420BB004",
                "143274797AE02D907243A4B8313191C9741A97AB941A864CAE7A7D07FCA4F48B",
                "083C3B99EEAB800AFC7BE01980804AC8B4F56EF667A5B8888419D6A062E28E16",
                "A300B2C10458DDED329697AEF7046C168FF16A6D725D3B2ED59758EA0FE9B63D",
                "5911B64278E1B409599962B929E39F2B68001ED99D94F11E9245516163D06815",
                "B0E11D4FA020823F5E710462F1E753821932828AAEE4C3C0FC3A7F355E481933",
                "265A36D5E5C75B73E02C7695956A8F1089C9CC0943E90E95DA212C3227961454",
            ],
            messages.Select(Hash));
    }

    [Fact]
    public void WireCodecConsumesCoalescedResponseAndIdrRequestSeparately()
    {
        var first = MiPlayRtspWireMessageCodec.Encode(
            "RTSP/1.0 200 OK",
            [
                new("CSeq", "6"),
                new("Content-Length", "0"),
            ],
            []);
        var idrBody = "wfd_idr_request\r\n"u8.ToArray();
        var second = MiPlayRtspWireMessageCodec.Encode(
            "SET_PARAMETER rtsp://localhost/wfd1.0/streamid=0 RTSP/1.0",
            [
                new("CSeq", "68"),
                new("Content-Type", "text/parameters"),
                new("Content-Length", idrBody.Length.ToString()),
            ],
            idrBody);
        var coalesced = first.Concat(second).ToArray();

        Assert.True(MiPlayRtspWireMessageCodec.TryDecode(coalesced, out var firstDecoded, out var firstConsumed));
        Assert.NotNull(firstDecoded);
        Assert.Equal("RTSP/1.0 200 OK", firstDecoded.StartLine);
        Assert.Equal("6", firstDecoded.GetHeader("CSeq"));

        Assert.True(MiPlayRtspWireMessageCodec.TryDecode(coalesced.AsSpan(firstConsumed), out var secondDecoded, out var secondConsumed));
        Assert.NotNull(secondDecoded);
        Assert.Equal("SET_PARAMETER rtsp://localhost/wfd1.0/streamid=0 RTSP/1.0", secondDecoded.StartLine);
        Assert.Equal("68", secondDecoded.GetHeader("CSeq"));
        Assert.Equal(idrBody, secondDecoded.Body);
        Assert.Equal(coalesced.Length, firstConsumed + secondConsumed);
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
