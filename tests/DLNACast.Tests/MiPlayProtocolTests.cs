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
        var wirePayload = payload + "\0";

        Assert.Equal(0x24, bytes[0]);
        Assert.Equal(0, BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(1, 2)));
        Assert.Equal(0x1234, BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(3, 2)));
        Assert.Equal((uint)wirePayload.Length, BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(5, 4)));
        Assert.Equal(wirePayload, Encoding.UTF8.GetString(bytes, 9, wirePayload.Length));
    }

    [Fact]
    public void PostAuthOpenDeviceFrameUsesEncryptedPayloadAndSessionCbcState()
    {
        var key = Encoding.ASCII.GetBytes("0123456789abcdef");
        var iv = Encoding.ASCII.GetBytes("fedcba9876543210");
        var sender = new MiPlaySafetyDataSessionCipher(key, iv);
        var previousSafetyData = sender.EncryptVersion1("previous-auth-frame"u8);
        var request = new MiPlayOpenDeviceRequest(IPAddress.Parse("192.168.31.8"), 7_236);
        var plaintext = request.ToPayloadBytes();

        var frame = request.ToSafetyDataCommandFrame(sequence: 4, sender);
        var rawFrame = request.ToCommandFrame(sequence: 4);

        Assert.NotEqual(rawFrame, frame);
        Assert.True(MiPlayCommandFrameCodec.TryDecode(frame, out var decodedFrame, out var bytesConsumed));
        Assert.NotNull(decodedFrame);
        Assert.Equal(frame.Length, bytesConsumed);
        Assert.Equal(MiPlayProtocolConstants.OpenDeviceCommand, decodedFrame.Command);
        Assert.Equal((ushort)4, decodedFrame.Sequence);
        Assert.NotEqual(plaintext, decodedFrame.Payload);
        Assert.NotEqual(MiPlaySafetyDataCodec.EncryptVersion1(plaintext, key, iv), decodedFrame.Payload);

        var receiver = new MiPlaySafetyDataSessionCipher(key, iv);
        Assert.True(receiver.TryDecryptVersion1(previousSafetyData, out var previousDecoded));
        Assert.NotNull(previousDecoded);
        Assert.Equal("previous-auth-frame"u8.ToArray(), previousDecoded.Plaintext);
        Assert.True(receiver.TryDecryptVersion1(decodedFrame.Payload, out var decodedSafetyData));
        Assert.NotNull(decodedSafetyData);
        Assert.Equal(plaintext, decodedSafetyData.Plaintext);
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
    public void DecodesObservedMiPlayNotifyModeAndStatePayloads()
    {
        var decodedMode = MiPlayNotifyPayloadCodec.TryDecode(
            Convert.FromHexString("046D6F64650302"),
            out var mode,
            out var modeBytesConsumed);
        var decodedState = MiPlayNotifyPayloadCodec.TryDecode(
            Convert.FromHexString("0573746174650303"),
            out var state,
            out var stateBytesConsumed);

        Assert.True(decodedMode);
        Assert.NotNull(mode);
        Assert.Equal("mode", mode.Label);
        Assert.Equal(MiPlayNotifyPayloadCodec.ByteValueType, mode.ValueType);
        Assert.Equal(2, mode.IntegerValue);
        Assert.Empty(mode.Fields);
        Assert.Equal(7, modeBytesConsumed);

        Assert.True(decodedState);
        Assert.NotNull(state);
        Assert.Equal("state", state.Label);
        Assert.Equal(MiPlayNotifyPayloadCodec.ByteValueType, state.ValueType);
        Assert.Equal(3, state.IntegerValue);
        Assert.Empty(state.Fields);
        Assert.Equal(8, stateBytesConsumed);
    }

    [Fact]
    public void DecodesObservedMiPlayNotifyMediaInfoExFields()
    {
        var payload = Convert.FromHexString(
            "0B6D65646961496E666F4578160000009C" +
            "026964140000000130" +
            "066D416C62756D1400000000" +
            "076D4172746973741400000000" +
            "086D417564696F4964140000000130" +
            "096D436F76657255726C1400000000" +
            "0C6D4465766963655374617465140000000133" +
            "096D4475726174696F6E140000000130" +
            "096D506F736974696F6E140000000130" +
            "066D5469746C651400000000" +
            "056D547970651400000005617564696F" +
            "06737461747573140000000133");

        var decoded = MiPlayNotifyPayloadCodec.TryDecode(payload, out var notify, out var bytesConsumed);

        Assert.True(decoded);
        Assert.NotNull(notify);
        Assert.Equal(payload.Length, bytesConsumed);
        Assert.Equal("mediaInfoEx", notify.Label);
        Assert.Equal(MiPlayNotifyPayloadCodec.ObjectValueType, notify.ValueType);
        Assert.Equal(156, notify.DeclaredPayloadLength);
        Assert.Equal(
            ["id", "mAlbum", "mArtist", "mAudioId", "mCoverUrl", "mDeviceState", "mDuration", "mPosition", "mTitle", "mType", "status"],
            notify.Fields.Select(field => field.Name));
        Assert.Equal("0", notify.Fields.Single(field => field.Name == "id").StringValue);
        Assert.Equal("3", notify.Fields.Single(field => field.Name == "mDeviceState").StringValue);
        Assert.Equal("audio", notify.Fields.Single(field => field.Name == "mType").StringValue);
        Assert.Equal("3", notify.Fields.Single(field => field.Name == "status").StringValue);
    }

    [Fact]
    public void DecodesCompoundFirstAudioPcmNotificationFromApplicationRun()
    {
        var payload = Convert.FromHexString(
            "0E66697273742D617564696F70636D0301" +
            "1A66697273742D617564696F70636D2D6275666665722D74696D650600000000");

        var decoded = MiPlayNotifyPayloadCodec.TryDecode(
            payload,
            out var notify,
            out var bytesConsumed);

        Assert.True(decoded);
        Assert.NotNull(notify);
        Assert.Equal(payload.Length, bytesConsumed);
        Assert.Equal("first-audiopcm", notify.Label);
        Assert.Equal(1, notify.IntegerValue);
        var bufferTime = Assert.Single(notify.Fields);
        Assert.Equal("first-audiopcm-buffer-time", bufferTime.Name);
        Assert.Equal(MiPlayNotifyPayloadCodec.UnsignedInt32ValueType, bufferTime.ValueType);
        Assert.Equal(0, bufferTime.IntegerValue);
        Assert.Null(bufferTime.StringValue);
    }

    [Fact]
    public void RejectsTruncatedMiPlayNotifyPayload()
    {
        var malformed = Convert.FromHexString("0B6D65646961496E666F4578160000009C02696414");

        var decoded = MiPlayNotifyPayloadCodec.TryDecode(malformed, out var notify, out var bytesConsumed);

        Assert.False(decoded);
        Assert.Null(notify);
        Assert.Equal(0, bytesConsumed);
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
            Assert.Equal('4', key[12]);
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
    public void AudioAccessUnitCipherEncryptsCompleteBlocksOnlyAndChainsIv()
    {
        var key = Enumerable.Range(0, 16).Select(value => (byte)value).ToArray();
        var iv = Enumerable.Range(16, 16).Select(value => (byte)value).ToArray();
        var accessUnit = Enumerable.Range(0, 37).Select(value => (byte)(0xA0 + value)).ToArray();
        var cipher = new MiPlayAudioAccessUnitCipher(key, iv);

        var encrypted = cipher.Encrypt(accessUnit);

        Assert.Equal(iv, encrypted.StartingIv);
        Assert.Equal(
            Convert.FromHexString("67896C75BA00597BAE4779270EF2B108DD49775189678FA9032C86208E7D974FC0C1C2C3C4"),
            encrypted.Payload);
        Assert.Equal(accessUnit[32..], encrypted.Payload[32..]);

        var secondAccessUnit = Enumerable.Range(0, 16).Select(value => (byte)(0x40 + value)).ToArray();
        var secondEncrypted = cipher.Encrypt(secondAccessUnit);

        Assert.Equal(Convert.FromHexString("DD49775189678FA9032C86208E7D974F"), secondEncrypted.StartingIv);
        Assert.Equal(Convert.FromHexString("068DC3752C49BB18029BA8A37FA4E17F"), secondEncrypted.Payload);

        var decryptor = new MiPlayAudioAccessUnitCipher(key, iv);
        Assert.Equal(accessUnit, decryptor.Decrypt(encrypted.Payload));
        Assert.Equal(secondAccessUnit, decryptor.Decrypt(secondEncrypted.Payload));
    }

    [Fact]
    public void AudioAccessUnitCipherLeavesSubBlockPayloadsClearAndDoesNotAdvanceIv()
    {
        var key = Enumerable.Range(0, 16).Select(value => (byte)value).ToArray();
        var iv = Enumerable.Range(16, 16).Select(value => (byte)value).ToArray();
        var shortAccessUnit = Enumerable.Range(0, 15).Select(value => (byte)(0x20 + value)).ToArray();
        var fullBlockAccessUnit = Enumerable.Range(0, 16).Select(value => (byte)(0x40 + value)).ToArray();
        var cipher = new MiPlayAudioAccessUnitCipher(key, iv);

        var shortEncrypted = cipher.Encrypt(shortAccessUnit);
        var afterShortEncrypted = cipher.Encrypt(fullBlockAccessUnit);
        var resetCipher = new MiPlayAudioAccessUnitCipher(key, iv);
        var resetEncrypted = resetCipher.Encrypt(fullBlockAccessUnit);

        Assert.Equal(iv, shortEncrypted.StartingIv);
        Assert.Equal(shortAccessUnit, shortEncrypted.Payload);
        Assert.Equal(resetEncrypted.StartingIv, afterShortEncrypted.StartingIv);
        Assert.Equal(resetEncrypted.Payload, afterShortEncrypted.Payload);

        var decryptor = new MiPlayAudioAccessUnitCipher(key, iv);
        Assert.Equal(shortAccessUnit, decryptor.Decrypt(shortEncrypted.Payload));
        Assert.Equal(fullBlockAccessUnit, decryptor.Decrypt(afterShortEncrypted.Payload));
    }

    [Fact]
    public void PlaybackDelayConstantsAreExpressedInMicroseconds()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(800), TimeSpan.FromMicroseconds(MiPlayProtocolConstants.FiveGigahertzPlaybackDelayMicroseconds));
        Assert.Equal(TimeSpan.FromSeconds(1), TimeSpan.FromMicroseconds(MiPlayProtocolConstants.OtherNetworkPlaybackDelayMicroseconds));
        Assert.Equal(TimeSpan.Zero, TimeSpan.FromMicroseconds(MiPlayProtocolConstants.SystemAudioPlaybackDelayMicroseconds));
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
    public void RtpPacketWrapsCompleteMpegTsPayloadWithPayloadTypeThirtyThree()
    {
        var payload = Enumerable.Range(0, MiPlayProtocolConstants.MpegTsPacketLength * 2)
            .Select(value => (byte)value)
            .ToArray();

        var packet = MiPlayRtpPacketCodec.EncodeMpegTsPayload(
            sequenceNumber: 0x1234,
            timestamp: 0x01020304,
            synchronizationSource: 0xA0B0C0D0,
            payload,
            marker: true);

        Assert.Equal(MiPlayProtocolConstants.RtpHeaderLength + payload.Length, packet.Length);
        Assert.Equal(0x80, packet[0]);
        Assert.Equal(0x80 | MiPlayProtocolConstants.MpegTsRtpPayloadType, packet[1]);
        Assert.Equal(0x1234, BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(2, 2)));
        Assert.Equal(0x01020304u, BinaryPrimitives.ReadUInt32BigEndian(packet.AsSpan(4, 4)));
        Assert.Equal(0xA0B0C0D0u, BinaryPrimitives.ReadUInt32BigEndian(packet.AsSpan(8, 4)));
        Assert.Equal(payload, packet[MiPlayProtocolConstants.RtpHeaderLength..]);
    }

    [Fact]
    public void RtpPacketRejectsPartialOrOversizedMpegTsPayloads()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MiPlayRtpPacketCodec.EncodeMpegTsPayload(
            sequenceNumber: 1,
            timestamp: 2,
            synchronizationSource: 3,
            [0]));
        Assert.Throws<ArgumentOutOfRangeException>(() => MiPlayRtpPacketCodec.EncodeMpegTsPayload(
            sequenceNumber: 1,
            timestamp: 2,
            synchronizationSource: 3,
            new byte[MiPlayRtpPacketCodec.MaximumMpegTsPayloadLength + MiPlayProtocolConstants.MpegTsPacketLength]));
    }

    [Fact]
    public void WfdMediaFrameUsesDollarAndTwentyFourBitBigEndianLength()
    {
        var rtp = Enumerable.Range(0, 1_328).Select(value => (byte)value).ToArray();

        var frame = MiPlayWfdInterleavedFrameCodec.Encode(rtp);

        Assert.Equal(1_332, frame.Length);
        Assert.Equal((byte)'$', frame[0]);
        Assert.Equal(0x00, frame[1]);
        Assert.Equal(0x05, frame[2]);
        Assert.Equal(0x30, frame[3]);
        Assert.True(MiPlayWfdInterleavedFrameCodec.TryDecode(frame, out var decoded, out var consumed));
        Assert.Equal(frame.Length, consumed);
        Assert.Equal(rtp, decoded);
    }

    [Fact]
    public void WfdTimerResponseMatchesTheFirstCapturedPhoneReply()
    {
        var request = Convert.FromHexString(
            "D6DC80A9000000000000000000000000000000000000000000000000000000000100000000000000");
        var expected = Convert.FromHexString(
            "D6DC80A9000000000000000000000000BDE71A3E02000000BDE71A3E020000000100000000000000");

        var decoded = MiPlayWfdTimerPacketCodec.Decode(request);
        var response = MiPlayWfdTimerPacketCodec.CreateResponse(
            request,
            sourceReceiveTimestamp: 9_631_885_245,
            sourceSendTimestamp: 9_631_885_245);

        Assert.Equal(2_843_794_646UL, decoded.RemoteTimestamp0);
        Assert.Equal(0UL, decoded.RemoteTimestamp1);
        Assert.Equal(1U, decoded.Sequence);
        Assert.Equal(0U, decoded.Reserved);
        Assert.Equal(expected, response);
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
