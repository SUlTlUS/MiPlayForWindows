using System.Buffers.Binary;
using System.Net;
using System.Text;
using System.Text.Json;
using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayProtocolTests
{
    [Fact]
    public void ParsesLegacyControlPortAndLyraCapability()
    {
        var appData = new byte[25];
        BinaryPrimitives.WriteUInt16BigEndian(appData, 12_345);
        appData[24] = 1;

        var parsed = MiPlayLegacyAppData.Parse(appData);

        Assert.Equal(12_345, parsed.ControlPort);
        Assert.True(parsed.HasAdvertisedControlPort);
        Assert.True(parsed.SupportsLyra);
    }

    [Fact]
    public void MissingLegacyAppDataUsesObservedFallbackPort()
    {
        var parsed = MiPlayLegacyAppData.Parse(null);

        Assert.Equal(8_899, parsed.ControlPort);
        Assert.False(parsed.HasAdvertisedControlPort);
        Assert.False(parsed.SupportsLyra);
    }

    [Fact]
    public void OpenDeviceFrameUsesNineByteBigEndianHeader()
    {
        var request = new MiPlayOpenDeviceRequest(IPAddress.Parse("192.168.31.8"), 7_236);

        var bytes = request.ToCommandFrame(sequence: 0x1234);
        var payload = "wfd://192.168.31.8:7236?mirrorMode=1";

        Assert.Equal(0x24, bytes[0]);
        Assert.Equal(0, BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(1, 2)));
        Assert.Equal(0x1234, BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(3, 2)));
        Assert.Equal((uint)payload.Length, BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(5, 4)));
        Assert.Equal(payload, Encoding.UTF8.GetString(bytes, 9, payload.Length));
    }

    [Fact]
    public void CommandFrameDecoderConsumesOneFrameAndLeavesFollowingData()
    {
        var encoded = MiPlayCommandFrameCodec.Encode(17, 9, [1, 2, 3]);
        var data = encoded.Concat(new byte[] { 0xaa, 0xbb }).ToArray();

        var decoded = MiPlayCommandFrameCodec.TryDecode(data, out var frame, out var consumed);

        Assert.True(decoded);
        Assert.NotNull(frame);
        Assert.Equal((ushort)17, frame.Command);
        Assert.Equal((ushort)9, frame.Sequence);
        Assert.Equal(new byte[] { 1, 2, 3 }, frame.Payload);
        Assert.Equal(encoded.Length, consumed);
    }

    [Fact]
    public void IncompleteCommandFrameIsNotConsumed()
    {
        var encoded = MiPlayCommandFrameCodec.Encode(0, 1, [1, 2, 3]);

        var decoded = MiPlayCommandFrameCodec.TryDecode(encoded.AsSpan(0, encoded.Length - 1), out var frame, out var consumed);

        Assert.False(decoded);
        Assert.Null(frame);
        Assert.Equal(0, consumed);
    }

    [Fact]
    public void SessionKeyJsonMatchesMiPlayFieldNamesAndOrder()
    {
        var keys = MiPlaySessionKeys.Create(
            IPAddress.Parse("192.168.31.8"),
            "0123456789abcdef",
            "fedcba9876543210",
            "0011223344556677");

        var json = keys.ToJson();

        Assert.Equal(
            "{\"wlan0ip\":\"192.168.31.8\",\"authKey\":\"0123456789abcdef\",\"streamKey\":\"fedcba9876543210\",\"streamIV\":\"0011223344556677\"}",
            json);
        using var document = JsonDocument.Parse(json);
        Assert.Equal("0011223344556677", document.RootElement.GetProperty("streamIV").GetString());
    }

    [Fact]
    public void GeneratedSessionKeysAreSixteenAsciiBytes()
    {
        var keys = MiPlaySessionKeys.Generate(IPAddress.Loopback);

        Assert.All([keys.AuthKey, keys.StreamKey, keys.StreamIv], key =>
        {
            Assert.Equal(16, Encoding.UTF8.GetByteCount(key));
            Assert.Matches("^[0-9a-f]{16}$", key);
        });
    }

    [Fact]
    public void WritesTheObservedFortyEightKilohertzStereoAdtsHeader()
    {
        var accessUnit = Enumerable.Range(0, 100).Select(value => (byte)value).ToArray();

        var packet = MiPlayAdtsHeader.Prepend(accessUnit);

        Assert.Equal(new byte[] { 0xff, 0xf9, 0x4c, 0x80, 0x0d, 0x7f, 0xfc }, packet[..7]);
        Assert.Equal(accessUnit, packet[7..]);
    }

    [Fact]
    public void PlaybackDelayConstantsAreExpressedInMicroseconds()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(800), TimeSpan.FromMicroseconds(MiPlayProtocolConstants.FiveGigahertzPlaybackDelayMicroseconds));
        Assert.Equal(TimeSpan.FromSeconds(1), TimeSpan.FromMicroseconds(MiPlayProtocolConstants.OtherNetworkPlaybackDelayMicroseconds));
    }

    [Theory]
    [InlineData("RTP/AVP/UDP;unicast;client_port=19000-19001", MiPlayTransportMode.Udp, 19000, 19001, null)]
    [InlineData("RTP/AVP/TCP;unicast;interleaved=0-1", MiPlayTransportMode.TcpInterleaved, null, null, null)]
    [InlineData("RTP/AVP/MPT;unicast;client_port=7236;userid=42", MiPlayTransportMode.MptKcp, 7236, null, 42)]
    [InlineData("RTP/AVP;unicast", MiPlayTransportMode.Udp, 19000, null, null)]
    public void ParsesObservedRtspTransportModes(
        string value,
        MiPlayTransportMode mode,
        int? rtpPort,
        int? rtcpPort,
        int? userId)
    {
        var parsed = MiPlayRtspTransport.TryParse(value, out var transport);

        Assert.True(parsed);
        Assert.NotNull(transport);
        Assert.Equal(mode, transport.Mode);
        Assert.Equal(rtpPort, transport.ClientRtpPort);
        Assert.Equal(rtcpPort, transport.ClientRtcpPort);
        Assert.Equal(userId, transport.UserId);
    }

    [Theory]
    [InlineData("RTP/AVP/MPT;unicast;client_port=0;userid=1")]
    [InlineData("RTP/AVP/MPT;unicast;client_port=7236;userid=-1")]
    [InlineData("RTP/AVP/QUIC;unicast;client_port=7236")]
    public void RejectsInvalidOrUnknownRtspTransport(string value)
    {
        Assert.False(MiPlayRtspTransport.TryParse(value, out var transport));
        Assert.Null(transport);
    }

    [Fact]
    public void MptProfileMatchesTheNativeKcpConfiguration()
    {
        Assert.Equal(0x1234u, MiPlayKcpProfile.ConversationId);
        Assert.Equal(1_400, MiPlayKcpProfile.MaximumTransmissionUnit);
        Assert.Equal(256, MiPlayKcpProfile.SendWindow);
        Assert.Equal(256, MiPlayKcpProfile.ReceiveWindow);
        Assert.Equal(10, MiPlayKcpProfile.UpdateIntervalMilliseconds);
        Assert.Equal(1, MiPlayKcpProfile.FastResend);
        Assert.Equal(100, MiPlayKcpProfile.MinimumRetransmissionTimeoutMilliseconds);
        Assert.False(MiPlayKcpProfile.NoDelay);
        Assert.True(MiPlayKcpProfile.DisableCongestionWindow);
    }

    [Fact]
    public void AudioUsesSevenMpegTsPacketsInEachRtpPacket()
    {
        Assert.Equal(33, MiPlayProtocolConstants.MpegTsRtpPayloadType);
        Assert.Equal(7, MiPlayProtocolConstants.MpegTsPacketsPerRtpPacket);
        Assert.True(
            MiPlayProtocolConstants.RtpHeaderLength +
            (MiPlayProtocolConstants.MpegTsPacketsPerRtpPacket * MiPlayProtocolConstants.MpegTsPacketLength) <=
            MiPlayProtocolConstants.MaximumRtpPacketLength);
    }

    [Fact]
    public void EncodesKnownMiPlayAudioReadTargetAsProtobuf()
    {
        var targetId = MiPlayCoapMessage.CreateTargetId(
            MiPlayMdnsCapabilities.MiPlayAudioApplicationId,
            attributeId: 1);
        var message = new MiPlayCoapMessage(
            MiPlayCoapMessageType.Read,
            targetId,
            [],
            Ip: 0,
            Port: 0,
            IdHash: []);

        var encoded = MiPlayCoapMessageCodec.EncodeMessages([message]);

        Assert.Equal(new byte[] { 0x0a, 0x04, 0x10, 0x81, 0x80, 0x14 }, encoded);
        var decoded = Assert.Single(MiPlayCoapMessageCodec.DecodeMessages(encoded));
        Assert.Equal(MiPlayCoapMessageType.Read, decoded.Type);
        Assert.Equal(5, decoded.ApplicationId);
        Assert.Equal(1, decoded.AttributeId);
        Assert.Empty(decoded.Value);
    }

    [Fact]
    public void RoundTripsMiPlayCoapWriteAndResponseFields()
    {
        var targetId = MiPlayCoapMessage.CreateTargetId(5, 0x1234);
        var message = new MiPlayCoapMessage(
            MiPlayCoapMessageType.Write,
            targetId,
            [1, 2, 3],
            Ip: 0x01020304,
            Port: MiPlayCoapMessageCodec.DefaultPort,
            IdHash: Encoding.ASCII.GetBytes("00e"));

        var decodedMessage = Assert.Single(MiPlayCoapMessageCodec.DecodeMessages(
            MiPlayCoapMessageCodec.EncodeMessages([message])));

        Assert.Equal(message.Type, decodedMessage.Type);
        Assert.Equal(message.TargetId, decodedMessage.TargetId);
        Assert.Equal(message.Value, decodedMessage.Value);
        Assert.Equal(message.Ip, decodedMessage.Ip);
        Assert.Equal(message.Port, decodedMessage.Port);
        Assert.Equal(message.IdHash, decodedMessage.IdHash);
        Assert.Equal(5, decodedMessage.ApplicationId);
        Assert.Equal(0x1234, decodedMessage.AttributeId);
        Assert.Equal("/32", MiPlayCoapMessageCodec.MailboxPath);

        var response = new MiPlayCoapResponse(
            MiPlayCoapMessageType.Write,
            targetId,
            Success: true,
            HasValue: true,
            Value: [4, 5, 6]);
        var decodedResponse = Assert.Single(MiPlayCoapMessageCodec.DecodeResponses(
            MiPlayCoapMessageCodec.EncodeResponses([response])));

        Assert.Equal(response.Type, decodedResponse.Type);
        Assert.Equal(response.TargetId, decodedResponse.TargetId);
        Assert.Equal(response.Success, decodedResponse.Success);
        Assert.Equal(response.HasValue, decodedResponse.HasValue);
        Assert.Equal(response.Value, decodedResponse.Value);
    }

    [Fact]
    public void RejectsTruncatedMiPlayCoapPayload()
    {
        var malformed = new byte[] { 0x0a, 0x04, 0x10, 0x81 };

        Assert.Throws<FormatException>(() => MiPlayCoapMessageCodec.DecodeMessages(malformed));
    }
}
