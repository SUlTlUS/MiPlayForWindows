using System.Buffers.Binary;
using System.Text;
using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlaySafetyProtocolTests
{
    [Fact]
    public void SafetyCommandUsesVerifiedOpackByteOrder()
    {
        var json = "{}"u8.ToArray();

        var frame = MiPlaySafetyCommandCodec.Encode(
            MiPlayProtocolConstants.SafetyAuthCommand,
            0x1234,
            json);

        Assert.Equal(MiPlayProtocolConstants.CommandFrameMagic, frame[0]);
        Assert.Equal(
            MiPlayProtocolConstants.SafetyAuthCommand,
            BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(1, 2)));
        Assert.Equal((ushort)0x1234, BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(3, 2)));
        Assert.Equal(
            new byte[] { 3, (byte)'c', (byte)'m', (byte)'d', 30, 0, 0, 0, 2, (byte)'{', (byte)'}' },
            frame.AsSpan(MiPlayProtocolConstants.CommandHeaderLength).ToArray());

        var decoded = MiPlaySafetyCommandCodec.TryDecode(frame, out var command, out var bytesConsumed);

        Assert.True(decoded);
        Assert.NotNull(command);
        Assert.Equal(MiPlayProtocolConstants.SafetyAuthCommand, command.Command);
        Assert.Equal((ushort)0x1234, command.Sequence);
        Assert.False(command.IsAcknowledgement);
        Assert.Equal(json, command.JsonPayload);
        Assert.Equal(frame.Length, bytesConsumed);
    }

    [Fact]
    public void SafetyCommandRejectsAnUnexpectedAcknowledgementEnvelope()
    {
        var envelope = MiPlaySafetyEnvelopeCodec.Encode(
            isAcknowledgement: true,
            MiPlayProtocolConstants.SafetyValueType,
            "{}"u8);
        var frame = MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.SafetyAuthCommand,
            sequence: 9,
            envelope);

        var decoded = MiPlaySafetyCommandCodec.TryDecode(frame, out var command, out var bytesConsumed);

        Assert.False(decoded);
        Assert.Null(command);
        Assert.Equal(0, bytesConsumed);
    }

    [Fact]
    public void SafetyInfoUsesNativeStringFieldsAndValidatesSelectedTypes()
    {
        var offer = MiPlaySafetyInfoOffer.Native18_0_0_3;

        Assert.Equal(
            """{"authKeyTypes":"1","authAlgorithmTypes":"7","integrityTypes":"1","aesKeyTypes":"1","aesIvTypes":"3"}""",
            Encoding.UTF8.GetString(offer.ToJsonPayload()));

        var selection = new MiPlaySafetyInfoSelection(
            authKeyType: 1,
            authAlgorithmType: 4,
            integrityType: null,
            aesKeyType: 1,
            aesIvType: 2);
        var selectionPayload = selection.ToJsonPayload();

        Assert.Equal(
            """{"result":"0","authKeyType":"1","authAlgorithmType":"4","aesKeyType":"1","aesIvType":"2"}""",
            Encoding.UTF8.GetString(selectionPayload));
        Assert.True(MiPlaySafetyInfoCodec.TryDecodeSelection(selectionPayload, out var decoded));
        Assert.NotNull(decoded);
        Assert.Equal((uint)1, decoded.AuthKeyType);
        Assert.Equal((uint)4, decoded.AuthAlgorithmType);
        Assert.Null(decoded.IntegrityType);
        Assert.Equal((uint)1, decoded.AesKeyType);
        Assert.Equal((uint)2, decoded.AesIvType);
    }

    [Fact]
    public void SafetyInfoRejectsIncompleteOrNonPositiveSelections()
    {
        Assert.False(MiPlaySafetyInfoCodec.TryDecodeSelection(
            """{"result":"0","authKeyType":"1"}"""u8,
            out _));
        Assert.False(MiPlaySafetyInfoCodec.TryDecodeSelection(
            """{"result":"0","aesKeyType":"0","aesIvType":"2"}"""u8,
            out _));
        Assert.Throws<ArgumentException>(() => new MiPlaySafetyInfoSelection(
            authKeyType: null,
            authAlgorithmType: 1,
            integrityType: null,
            aesKeyType: null,
            aesIvType: null));
    }

    [Fact]
    public void SafetyInfoAcknowledgementAcceptsNativeResultZeroSelection()
    {
        var payload = """{"result":"0","authKeyType":"1","authAlgorithmType":"4","integrityType":"1","aesKeyType":"1","aesIvType":"2"}"""u8;

        Assert.True(MiPlaySafetyInfoCodec.TryDecodeAcknowledgement(payload, out var acknowledgement));
        Assert.NotNull(acknowledgement);
        Assert.Equal("0", acknowledgement.Result);
        Assert.Equal((uint)4, acknowledgement.Selection.AuthAlgorithmType);
        Assert.Equal((uint)2, acknowledgement.Selection.AesIvType);
        Assert.True(MiPlaySafetyInfoCodec.TryDecodeSelection(payload, out var selection));
        Assert.Equal(acknowledgement.Selection, selection);
        Assert.False(MiPlaySafetyInfoCodec.TryDecodeSelection(
            """{"result":"1","authKeyType":"1","authAlgorithmType":"4","integrityType":"1","aesKeyType":"1","aesIvType":"2"}"""u8,
            out _));
    }

    [Fact]
    public void LyraSecretKeyCommandRequiresTheFourNativeJsonStrings()
    {
        var command = new MiPlayLyraSecretKeyCommand(
            Wlan0Ip: "192.168.10.9",
            AuthKey: "auth-key-example",
            StreamKey: "stream-key-sample",
            StreamIv: "stream-iv-sample");

        var payload = command.ToJsonPayload();

        Assert.Equal(
            """{"wlan0ip":"192.168.10.9","authKey":"auth-key-example","streamKey":"stream-key-sample","streamIV":"stream-iv-sample"}""",
            Encoding.UTF8.GetString(payload));
        Assert.True(MiPlayLyraSecretKeyCodec.TryDecode(payload, out var decoded));
        Assert.Equal(command, decoded);
        Assert.False(MiPlayLyraSecretKeyCodec.TryDecode(
            """{"wlan0ip":"192.168.10.9","authKey":"a","streamKey":"b"}"""u8,
            out _));
    }

    [Fact]
    public void LocalDeviceInfoSourceNamePayloadMatchesNativeJsonAndMacHash()
    {
        var payload = MiPlayLocalDeviceInfoPayloadCodec.EncodeSourceName(
            sourceName: "小米手机",
            bluetoothMac: "AA:BB:CC:DD:EE:FF",
            canAlonePlayCtrl: "0");

        Assert.Equal(
            """{"sourceName":"小米手机","mSourceBtMac":"7D6D7EC9459BDD10988ABAF6BFA5232F","canAlonePlayCtrl":"0","canHeadsetCtrl":"1"}""",
            Encoding.UTF8.GetString(payload));
    }

    [Fact]
    public void LocalDeviceInfoSourceNamePayloadUsesEmptyHashWhenBluetoothMacIsMissing()
    {
        var payload = MiPlayLocalDeviceInfoPayloadCodec.EncodeSourceName(
            sourceName: "Windows",
            bluetoothMac: "",
            includeControlFields: false);

        Assert.Equal(
            """{"sourceName":"Windows","mSourceBtMac":""}""",
            Encoding.UTF8.GetString(payload));
        Assert.Throws<ArgumentException>(() =>
            MiPlayLocalDeviceInfoPayloadCodec.EncodeSourceName("", bluetoothMac: null));
    }

    [Fact]
    public void LocalDeviceInfoModelPayloadMatchesNativeSetLocalDeviceInfo2()
    {
        var payload = MiPlayLocalDeviceInfoPayloadCodec.EncodeLocalDeviceInfo(
            model: "Xiaomi 14",
            romVersion: "OS1.0.1",
            appVersion: 100000105);

        Assert.Equal(
            """{"model":"Xiaomi 14","romVersion":"OS1.0.1","appVersion":100000105}""",
            Encoding.UTF8.GetString(payload));
    }

    [Fact]
    public void PostAuthDeviceInfoAcknowledgementCommandsMatchNativeJumpTable()
    {
        Assert.Equal(0x001F, MiPlayProtocolConstants.GetDeviceInfoAcknowledgementCommand);
        Assert.Equal(
            checked((ushort)(MiPlayProtocolConstants.GetDeviceInfoCommand + 1)),
            MiPlayProtocolConstants.GetDeviceInfoAcknowledgementCommand);
        Assert.Equal(0x0059, MiPlayProtocolConstants.SetLocalDeviceInfoAcknowledgementCommand);
        Assert.Equal(
            checked((ushort)(MiPlayProtocolConstants.SetLocalDeviceInfoCommand + 1)),
            MiPlayProtocolConstants.SetLocalDeviceInfoAcknowledgementCommand);
    }

    [Fact]
    public void TcpSessionInfoDerivesType1KeyWithPeerBeforeLocalOrdering()
    {
        var session = new MiPlayTcpSessionInfo(
            localAddress: System.Net.IPAddress.Parse("192.168.10.9"),
            localPort: 54123,
            peerAddress: System.Net.IPAddress.Parse("192.168.10.4"),
            peerPort: 8899);

        Assert.Equal("538500d12a3719c98beabb013f679eb3", session.DeriveType1SafetyKey());
        Assert.Throws<ArgumentException>(() => new MiPlayTcpSessionInfo(
            localAddress: System.Net.IPAddress.IPv6Loopback,
            localPort: 1,
            peerAddress: System.Net.IPAddress.Loopback,
            peerPort: 8899));
    }

    [Fact]
    public void NativeSessionBootstrapUsesTheVerifiedVersionFrame()
    {
        var frame = MiPlayNativeVersionCodec.EncodeSourceVersion(sequence: 1);

        Assert.True(MiPlayCommandFrameCodec.TryDecode(frame, out var decoded, out var bytesConsumed));
        Assert.NotNull(decoded);
        Assert.Equal(MiPlayProtocolConstants.NativeSourceVersionCommand, decoded.Command);
        Assert.Equal((ushort)1, decoded.Sequence);
        Assert.Equal(MiPlayProtocolConstants.NativeSourceVersion18_0_0_3Payload, Encoding.ASCII.GetString(decoded.Payload));
        Assert.Equal(frame.Length, bytesConsumed);

        var acknowledgement = MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.NativeSourceVersionAcknowledgementCommand,
            sequence: 1,
            "2.1.5091615\0"u8);
        Assert.True(MiPlayNativeVersionCodec.TryDecodeAcknowledgement(
            acknowledgement,
            out var acknowledgementSequence,
            out var deviceVersion));
        Assert.Equal((ushort)1, acknowledgementSequence);
        Assert.Equal("2.1.5091615", deviceVersion);
    }

    [Fact]
    public void SafetyDataHeaderParsesTheObservedS12VersionOneContainerWithoutDecrypting()
    {
        var receivedSafetyData = Convert.FromHexString(
            "000701E00200ECAE89F6CB0DD35E2CB4FD408221777435A6E936DFDC3852CD3AA9757CFBE03675611671BF743FA3D6E0D9DBB0091E0A740C140D84A436B97DE4AA88A3252D54B6F1CF");

        Assert.True(MiPlaySafetyDataHeaderCodec.TryDecodeVersion1(receivedSafetyData, out var header));
        Assert.NotNull(header);
        Assert.Equal(9, header.HeaderLength);
        Assert.Equal((byte)0xE0, header.Flags);
        Assert.True(header.IsEncrypted);
        Assert.True(header.HasPaddingLengthField);
        Assert.Equal((byte)2, header.PaddingLength);
        Assert.True(header.HasIntegrityValue);
        Assert.Equal(0x00ECAE89u, header.IntegrityValue);
        Assert.Equal(9, header.PayloadOffset);
        Assert.Equal(receivedSafetyData.Length - 9, header.PayloadLength);

        Assert.False(MiPlaySafetyDataHeaderCodec.TryDecodeVersion1(
            receivedSafetyData.AsSpan(0, 8),
            out _));
    }

    [Fact]
    public void SafetyDataCodecMatchesTheObservedS12CiphertextCrc()
    {
        var receivedSafetyData = Convert.FromHexString(
            "000701E00200ECAE89F6CB0DD35E2CB4FD408221777435A6E936DFDC3852CD3AA9757CFBE03675611671BF743FA3D6E0D9DBB0091E0A740C140D84A436B97DE4AA88A3252D54B6F1CF");

        Assert.Equal(0x89AEEC00u, MiPlaySafetyDataCodec.ComputeCrc32Mpeg2(receivedSafetyData.AsSpan(9)));
        Assert.Equal(0x89AEEC00u, BinaryPrimitives.ReadUInt32LittleEndian(receivedSafetyData.AsSpan(5, sizeof(uint))));
    }

    [Fact]
    public void SafetyDataDiagnosticsDescribeHeaderCrcAndDecryptFailureClasses()
    {
        var key = Encoding.ASCII.GetBytes("0123456789abcdef");
        var iv = Encoding.ASCII.GetBytes("fedcba9876543210");
        var encoded = MiPlaySafetyDataCodec.EncryptVersion1("diagnostic"u8, key, iv);
        var invalidCrc = encoded.ToArray();
        invalidCrc[^1] ^= 0x01;

        var invalidHeader = MiPlaySafetyDataDiagnostics.DescribeVersion1DecodeFailure(encoded.AsSpan(0, 3));
        var crcMismatch = MiPlaySafetyDataDiagnostics.DescribeVersion1DecodeFailure(invalidCrc);
        var decryptOrPadding = MiPlaySafetyDataDiagnostics.DescribeVersion1DecodeFailure(encoded);

        Assert.Equal("header=invalid,length=3", invalidHeader);
        Assert.Contains("header=ok,headerLength=9,flags=0xE0", crcMismatch, StringComparison.Ordinal);
        Assert.Contains("failure=crc-mismatch", crcMismatch, StringComparison.Ordinal);
        Assert.Contains("storedCrc=", crcMismatch, StringComparison.Ordinal);
        Assert.Contains("computedCrc=", crcMismatch, StringComparison.Ordinal);
        Assert.Contains("header=ok,headerLength=9,flags=0xE0", decryptOrPadding, StringComparison.Ordinal);
        Assert.Contains("failure=decrypt-or-padding", decryptOrPadding, StringComparison.Ordinal);
    }

    [Fact]
    public void ObservedS12SafetyAuthChallengeDecryptsWithRecoveredTcpSessionMaterial()
    {
        var receivedSafetyData = Convert.FromHexString(
            "000701E00200ECAE89F6CB0DD35E2CB4FD408221777435A6E936DFDC3852CD3AA9757CFBE03675611671BF743FA3D6E0D9DBB0091E0A740C140D84A436B97DE4AA88A3252D54B6F1CF");
        var session = new MiPlayTcpSessionInfo(
            localAddress: System.Net.IPAddress.Parse("192.168.10.9"),
            localPort: 9970,
            peerAddress: System.Net.IPAddress.Parse("192.168.10.4"),
            peerPort: 8899);

        var authKey = session.DeriveType1SafetyKey();
        Assert.Equal("d24264ef9bb7ddf04a6062358fc7849e", authKey);
        var aesKey = Encoding.ASCII.GetBytes(MiPlaySafetyKeyDerivation.SelectDerivedAesMaterial(
            authKey,
            MiPlaySafetyKeyDerivation.FirstHalfMaterialType));
        var nativeAesIv = Encoding.ASCII.GetBytes(MiPlaySafetyKeyDerivation.SelectDerivedAesMaterial(
            authKey,
            MiPlaySafetyKeyDerivation.SecondHalfMaterialType));
        Assert.True(MiPlaySafetyDataCodec.TryDecryptVersion1(
            receivedSafetyData,
            aesKey,
            nativeAesIv,
            out var nativeIvSafetyData));
        Assert.NotNull(nativeIvSafetyData);
        Assert.False(
            MiPlaySafetyEnvelopeCodec.TryDecode(
                nativeIvSafetyData.Plaintext,
                out var nativeIvEnvelope,
                out var nativeIvBytesConsumed) &&
            nativeIvEnvelope is not null &&
            nativeIvBytesConsumed == nativeIvSafetyData.Plaintext.Length &&
            !nativeIvEnvelope.IsAcknowledgement &&
            MiPlaySafetyAuthCodec.TryDecodeChallenge(nativeIvEnvelope.Payload, out _));

        var observedInboundAesIv = Encoding.ASCII.GetBytes(MiPlaySafetyKeyDerivation.SelectObservedS12InboundSafetyIvMaterial(
            authKey,
            aesKeyType: 1,
            aesIvType: 2));
        Assert.True(MiPlaySafetyDataCodec.TryDecryptVersion1(
            receivedSafetyData,
            aesKey,
            observedInboundAesIv,
            out var safetyData));
        Assert.NotNull(safetyData);

        Assert.True(MiPlaySafetyEnvelopeCodec.TryDecode(
            safetyData.Plaintext,
            out var envelope,
            out var bytesConsumed));
        Assert.NotNull(envelope);
        Assert.Equal(safetyData.Plaintext.Length, bytesConsumed);
        Assert.False(envelope.IsAcknowledgement);
        Assert.Equal(MiPlayProtocolConstants.SafetyValueType, envelope.ValueType);
        Assert.True(MiPlaySafetyAuthCodec.TryDecodeChallenge(envelope.Payload, out var challenge));
        Assert.NotNull(challenge);
        Assert.Equal("c0e81b8e05502738463dbdb7ce8fdc4d", challenge.AuthMessage);

        var acknowledgement = MiPlaySafetyAuthCodec.CreateAcknowledgement(
            challenge.AuthMessage,
            authKey,
            MiPlaySafetyHashAlgorithm.Sha256);
        Assert.Equal(
            "3e5811cf3a0c2a9dabeab49f2ec145196b0a26ad091aed353ba56c6c8ede4a65",
            acknowledgement.AuthMessageAck);
    }

    [Fact]
    public void ObservedS12SafetyAuthAcknowledgementBuildsEncryptedCommandFrame()
    {
        var session = new MiPlayTcpSessionInfo(
            localAddress: System.Net.IPAddress.Parse("192.168.10.9"),
            localPort: 9970,
            peerAddress: System.Net.IPAddress.Parse("192.168.10.4"),
            peerPort: 8899);
        var authKey = session.DeriveType1SafetyKey();
        var aesKey = Encoding.ASCII.GetBytes(MiPlaySafetyKeyDerivation.SelectDerivedAesMaterial(
            authKey,
            MiPlaySafetyKeyDerivation.FirstHalfMaterialType));
        var observedInboundAesIv = Encoding.ASCII.GetBytes(MiPlaySafetyKeyDerivation.SelectObservedS12InboundSafetyIvMaterial(
            authKey,
            aesKeyType: 1,
            aesIvType: 2));
        var nativeAesIv = Encoding.ASCII.GetBytes(MiPlaySafetyKeyDerivation.SelectDerivedAesMaterial(
            authKey,
            MiPlaySafetyKeyDerivation.SecondHalfMaterialType));
        var challenge = new MiPlaySafetyAuthChallenge("c0e81b8e05502738463dbdb7ce8fdc4d");

        var acknowledgement = MiPlaySafetyAuthCodec.CreateAcknowledgement(
            challenge.AuthMessage,
            authKey,
            MiPlaySafetyHashAlgorithm.Sha256);
        Assert.Equal(
            "{\"result\":\"1\",\"authMsgAck\":\"3e5811cf3a0c2a9dabeab49f2ec145196b0a26ad091aed353ba56c6c8ede4a65\"}",
            Encoding.UTF8.GetString(acknowledgement.ToJsonPayload()));

        var acknowledgementPlaintext = MiPlaySafetyEnvelopeCodec.Encode(
            isAcknowledgement: true,
            MiPlayProtocolConstants.SafetyValueType,
            acknowledgement.ToJsonPayload());
        Assert.Equal(
            "0361636B1E0000005E7B22726573756C74223A2231222C22617574684D736741636B223A2233653538313163663361306332613964616265616234396632656331343531393662306132366164303931616564333533626135366336633865646534613635227D",
            Convert.ToHexString(acknowledgementPlaintext));

        var encryptedAcknowledgement = MiPlaySafetyDataCodec.EncryptVersion1(
            acknowledgementPlaintext,
            aesKey,
            observedInboundAesIv);
        Assert.Equal(
            "000701E009730D911C1B46DFBDFFE76CDD31B4C9AF04EAFD003A9465E7A84AAD47A5D095FE7A72AA3562015C44F932570DA61B0EE9AC6E9B4DC75C668A35FFC8DE17E99BD8DE5798E9CBD1F13CDEB919AAF0DE15340CB06F001C9FDB27F1C349DE305449C39AAFC3DE50961804903DEE088D906B40E2C503EB",
            Convert.ToHexString(encryptedAcknowledgement));

        var frame = MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.SafetyAuthAcknowledgementCommand,
            sequence: 0,
            encryptedAcknowledgement);
        Assert.Equal(
            "241403000000000079000701E009730D911C1B46DFBDFFE76CDD31B4C9AF04EAFD003A9465E7A84AAD47A5D095FE7A72AA3562015C44F932570DA61B0EE9AC6E9B4DC75C668A35FFC8DE17E99BD8DE5798E9CBD1F13CDEB919AAF0DE15340CB06F001C9FDB27F1C349DE305449C39AAFC3DE50961804903DEE088D906B40E2C503EB",
            Convert.ToHexString(frame));

        Assert.True(MiPlayCommandFrameCodec.TryDecode(frame, out var decodedFrame, out var frameBytesConsumed));
        Assert.NotNull(decodedFrame);
        Assert.Equal(frame.Length, frameBytesConsumed);
        Assert.Equal(MiPlayProtocolConstants.SafetyAuthAcknowledgementCommand, decodedFrame.Command);
        Assert.Equal((ushort)0, decodedFrame.Sequence);
        Assert.Equal(encryptedAcknowledgement, decodedFrame.Payload);
        Assert.False(MiPlaySafetyCommandCodec.TryDecode(frame, out _, out _));

        Assert.True(MiPlaySafetyDataCodec.TryDecryptVersion1(
            decodedFrame.Payload,
            aesKey,
            observedInboundAesIv,
            out var decryptedAcknowledgement));
        Assert.NotNull(decryptedAcknowledgement);
        Assert.True(MiPlaySafetyEnvelopeCodec.TryDecode(
            decryptedAcknowledgement.Plaintext,
            out var envelope,
            out var envelopeBytesConsumed));
        Assert.NotNull(envelope);
        Assert.Equal(decryptedAcknowledgement.Plaintext.Length, envelopeBytesConsumed);
        Assert.True(envelope.IsAcknowledgement);
        Assert.True(MiPlaySafetyAuthCodec.TryDecodeAcknowledgement(envelope.Payload, out var decodedAcknowledgement));
        Assert.Equal(acknowledgement, decodedAcknowledgement);

        var nativeIvDecodedCompleteAck = MiPlaySafetyDataCodec.TryDecryptVersion1(
                decodedFrame.Payload,
                aesKey,
                nativeAesIv,
                out var nativeIvSafetyData) &&
            nativeIvSafetyData is not null &&
            MiPlaySafetyEnvelopeCodec.TryDecode(
                nativeIvSafetyData.Plaintext,
                out var nativeIvEnvelope,
                out var nativeIvBytesConsumed) &&
            nativeIvEnvelope is not null &&
            nativeIvBytesConsumed == nativeIvSafetyData.Plaintext.Length &&
            nativeIvEnvelope.IsAcknowledgement &&
            MiPlaySafetyAuthCodec.TryDecodeAcknowledgement(nativeIvEnvelope.Payload, out _);
        Assert.False(nativeIvDecodedCompleteAck);
    }

    [Fact]
    public void ObservedS12MutualProbePeerAcknowledgementUsesResultZeroAndChainedInboundCbc()
    {
        var session = new MiPlayTcpSessionInfo(
            localAddress: System.Net.IPAddress.Parse("192.168.10.9"),
            localPort: 13634,
            peerAddress: System.Net.IPAddress.Parse("192.168.10.4"),
            peerPort: 8899);
        var authKey = session.DeriveType1SafetyKey();
        var aesKey = Encoding.ASCII.GetBytes(MiPlaySafetyKeyDerivation.SelectDerivedAesMaterial(
            authKey,
            MiPlaySafetyKeyDerivation.FirstHalfMaterialType));
        var observedInboundAesIv = Encoding.ASCII.GetBytes(MiPlaySafetyKeyDerivation.SelectObservedS12InboundSafetyIvMaterial(
            authKey,
            aesKeyType: 1,
            aesIvType: 2));
        var cipher = new MiPlaySafetyDataSessionCipher(aesKey, observedInboundAesIv);
        var peerChallengeData = Convert.FromHexString(
            "000701E002DDAFF090EE84246399428CF794F0743713900F8A504B157F64D7CE488AC47128C45E60D627EEEEFC14065F1F7EC94D43B47851F0A3A92C68993346AA3FD268EFDB76F62D");
        var peerAcknowledgementData = Convert.FromHexString(
            "000701E00F6E4F725613312EF24159C3610572ED3C4198007E7F4E347B5C5AA0F91EE6606E68B3B84813D62DFF13D040B09611811B90F9C4519E92646010404E757DC63B518F2CFAC21CEEEF2A9676C783F20F2063DE89A5954B62E40165F25BDD2A3FF446EE5C976E7E38FBF4F456F76B5A8AD01AB781C08C55F5012EE7B4D03D13C1C50AE5453781");

        Assert.True(cipher.TryDecryptVersion1(peerChallengeData, out var peerChallengeSafetyData));
        Assert.NotNull(peerChallengeSafetyData);
        Assert.True(MiPlaySafetyEnvelopeCodec.TryDecode(
            peerChallengeSafetyData.Plaintext,
            out var peerChallengeEnvelope,
            out var peerChallengeBytesConsumed));
        Assert.NotNull(peerChallengeEnvelope);
        Assert.Equal(peerChallengeSafetyData.Plaintext.Length, peerChallengeBytesConsumed);
        Assert.False(peerChallengeEnvelope.IsAcknowledgement);
        Assert.True(MiPlaySafetyAuthCodec.TryDecodeChallenge(peerChallengeEnvelope.Payload, out var peerChallenge));
        Assert.NotNull(peerChallenge);
        Assert.Equal("8c6695041d51b9474715a24eaf786a02", peerChallenge.AuthMessage);

        Assert.True(cipher.TryDecryptVersion1(peerAcknowledgementData, out var peerAcknowledgementSafetyData));
        Assert.NotNull(peerAcknowledgementSafetyData);
        Assert.True(MiPlaySafetyEnvelopeCodec.TryDecode(
            peerAcknowledgementSafetyData.Plaintext,
            out var peerAcknowledgementEnvelope,
            out var peerAcknowledgementBytesConsumed));
        Assert.NotNull(peerAcknowledgementEnvelope);
        Assert.Equal(peerAcknowledgementSafetyData.Plaintext.Length, peerAcknowledgementBytesConsumed);
        Assert.True(peerAcknowledgementEnvelope.IsAcknowledgement);
        Assert.True(MiPlaySafetyAuthCodec.TryDecodeAcknowledgement(
            peerAcknowledgementEnvelope.Payload,
            out var peerAcknowledgement));
        Assert.NotNull(peerAcknowledgement);
        Assert.Equal("0", peerAcknowledgement.Result);
        Assert.Equal(
            "e2a006bcbe1f70f556d60ad2d75d415df6abc39168e84d6c2b746d94f1bb5c00",
            peerAcknowledgement.AuthMessageAck);
    }

    [Fact]
    public void ObservedS12MutualSafetyAuthEncryptsLocalChallengeBeforePeerAcknowledgement()
    {
        var session = new MiPlayTcpSessionInfo(
            localAddress: System.Net.IPAddress.Parse("192.168.10.9"),
            localPort: 9970,
            peerAddress: System.Net.IPAddress.Parse("192.168.10.4"),
            peerPort: 8899);
        var authKey = session.DeriveType1SafetyKey();
        var aesKey = Encoding.ASCII.GetBytes(MiPlaySafetyKeyDerivation.SelectDerivedAesMaterial(
            authKey,
            MiPlaySafetyKeyDerivation.FirstHalfMaterialType));
        var observedInboundAesIv = Encoding.ASCII.GetBytes(MiPlaySafetyKeyDerivation.SelectObservedS12InboundSafetyIvMaterial(
            authKey,
            aesKeyType: 1,
            aesIvType: 2));
        var sender = new MiPlaySafetyDataSessionCipher(aesKey, observedInboundAesIv);

        var localChallenge = MiPlaySafetyAuthCodec.CreateChallenge(123_456_789);
        Assert.Equal("25f9e794323b453885f5181f1b624d0b", localChallenge.AuthMessage);
        var localChallengePlaintext = MiPlaySafetyEnvelopeCodec.Encode(
            isAcknowledgement: false,
            MiPlayProtocolConstants.SafetyValueType,
            localChallenge.ToJsonPayload());
        var encryptedLocalChallenge = sender.EncryptVersion1(localChallengePlaintext);
        Assert.Equal(
            MiPlaySafetyDataCodec.EncryptVersion1(localChallengePlaintext, aesKey, observedInboundAesIv),
            encryptedLocalChallenge);

        var localChallengeFrame = MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.SafetyAuthCommand,
            sequence: 3,
            encryptedLocalChallenge);
        Assert.True(MiPlayCommandFrameCodec.TryDecode(localChallengeFrame, out var decodedLocalChallengeFrame, out _));
        Assert.NotNull(decodedLocalChallengeFrame);
        Assert.Equal(MiPlayProtocolConstants.SafetyAuthCommand, decodedLocalChallengeFrame.Command);
        Assert.Equal((ushort)3, decodedLocalChallengeFrame.Sequence);
        Assert.False(MiPlaySafetyCommandCodec.TryDecode(localChallengeFrame, out _, out _));

        var peerChallenge = new MiPlaySafetyAuthChallenge("c0e81b8e05502738463dbdb7ce8fdc4d");
        var localAcknowledgement = MiPlaySafetyAuthCodec.CreateAcknowledgement(
            peerChallenge.AuthMessage,
            authKey,
            MiPlaySafetyHashAlgorithm.Sha256);
        var localAcknowledgementPlaintext = MiPlaySafetyEnvelopeCodec.Encode(
            isAcknowledgement: true,
            MiPlayProtocolConstants.SafetyValueType,
            localAcknowledgement.ToJsonPayload());
        var encryptedAcknowledgementAfterLocalChallenge = sender.EncryptVersion1(localAcknowledgementPlaintext);
        Assert.NotEqual(
            MiPlaySafetyDataCodec.EncryptVersion1(localAcknowledgementPlaintext, aesKey, observedInboundAesIv),
            encryptedAcknowledgementAfterLocalChallenge);

        var receiver = new MiPlaySafetyDataSessionCipher(aesKey, observedInboundAesIv);
        Assert.True(receiver.TryDecryptVersion1(encryptedLocalChallenge, out var decodedLocalChallenge));
        Assert.NotNull(decodedLocalChallenge);
        Assert.True(MiPlaySafetyEnvelopeCodec.TryDecode(
            decodedLocalChallenge.Plaintext,
            out var localChallengeEnvelope,
            out var localChallengeBytesConsumed));
        Assert.NotNull(localChallengeEnvelope);
        Assert.Equal(decodedLocalChallenge.Plaintext.Length, localChallengeBytesConsumed);
        Assert.False(localChallengeEnvelope.IsAcknowledgement);
        Assert.True(MiPlaySafetyAuthCodec.TryDecodeChallenge(localChallengeEnvelope.Payload, out var decodedLocalChallengePayload));
        Assert.Equal(localChallenge, decodedLocalChallengePayload);

        Assert.True(receiver.TryDecryptVersion1(encryptedAcknowledgementAfterLocalChallenge, out var decodedAcknowledgement));
        Assert.NotNull(decodedAcknowledgement);
        Assert.True(MiPlaySafetyEnvelopeCodec.TryDecode(
            decodedAcknowledgement.Plaintext,
            out var acknowledgementEnvelope,
            out var acknowledgementBytesConsumed));
        Assert.NotNull(acknowledgementEnvelope);
        Assert.Equal(decodedAcknowledgement.Plaintext.Length, acknowledgementBytesConsumed);
        Assert.True(acknowledgementEnvelope.IsAcknowledgement);
        Assert.True(MiPlaySafetyAuthCodec.TryDecodeAcknowledgement(
            acknowledgementEnvelope.Payload,
            out var decodedLocalAcknowledgement));
        Assert.Equal(localAcknowledgement, decodedLocalAcknowledgement);
    }

    [Fact]
    public void SafetyDataSessionCipherMatchesOneShotFirstFrameAndAdvancesOutboundCbcState()
    {
        var key = Encoding.ASCII.GetBytes("0123456789abcdef");
        var iv = Encoding.ASCII.GetBytes("fedcba9876543210");
        var firstPlaintext = MiPlaySafetyEnvelopeCodec.Encode(
            isAcknowledgement: false,
            MiPlayProtocolConstants.SafetyValueType,
            MiPlaySafetyAuthCodec.CreateChallenge(123_456_789).ToJsonPayload());
        var secondPlaintext = MiPlaySafetyEnvelopeCodec.Encode(
            isAcknowledgement: true,
            MiPlayProtocolConstants.SafetyValueType,
            new MiPlaySafetyAuthAcknowledgement("0123456789abcdef0123456789abcdef").ToJsonPayload());
        var sender = new MiPlaySafetyDataSessionCipher(key, iv);

        var firstFrameData = sender.EncryptVersion1(firstPlaintext);
        var secondFrameData = sender.EncryptVersion1(secondPlaintext);

        Assert.Equal(MiPlaySafetyDataCodec.EncryptVersion1(firstPlaintext, key, iv), firstFrameData);
        Assert.NotEqual(MiPlaySafetyDataCodec.EncryptVersion1(secondPlaintext, key, iv), secondFrameData);

        var receiver = new MiPlaySafetyDataSessionCipher(key, iv);
        Assert.True(receiver.TryDecryptVersion1(firstFrameData, out var firstDecoded));
        Assert.NotNull(firstDecoded);
        Assert.Equal(firstPlaintext, firstDecoded.Plaintext);
        Assert.True(receiver.TryDecryptVersion1(secondFrameData, out var secondDecoded));
        Assert.NotNull(secondDecoded);
        Assert.Equal(secondPlaintext, secondDecoded.Plaintext);

        Assert.True(MiPlaySafetyDataCodec.TryDecryptVersion1(secondFrameData, key, iv, out var resetIvDecoded));
        Assert.NotNull(resetIvDecoded);
        Assert.NotEqual(secondPlaintext, resetIvDecoded.Plaintext);
    }

    [Fact]
    public void SafetyDataSessionCipherDoesNotAdvanceInboundCbcStateAfterFailedDecrypt()
    {
        var key = Encoding.ASCII.GetBytes("0123456789abcdef");
        var iv = Encoding.ASCII.GetBytes("fedcba9876543210");
        var firstPlaintext = MiPlaySafetyEnvelopeCodec.Encode(
            isAcknowledgement: false,
            MiPlayProtocolConstants.SafetyValueType,
            MiPlaySafetyAuthCodec.CreateChallenge(123_456_789).ToJsonPayload());
        var secondPlaintext = MiPlaySafetyEnvelopeCodec.Encode(
            isAcknowledgement: true,
            MiPlayProtocolConstants.SafetyValueType,
            new MiPlaySafetyAuthAcknowledgement("0123456789abcdef0123456789abcdef").ToJsonPayload());
        var sender = new MiPlaySafetyDataSessionCipher(key, iv);
        var firstFrameData = sender.EncryptVersion1(firstPlaintext);
        var secondFrameData = sender.EncryptVersion1(secondPlaintext);
        var corruptedSecondFrameData = secondFrameData.ToArray();
        corruptedSecondFrameData[^1] ^= 0x80;
        var corruptedCiphertext = corruptedSecondFrameData.AsSpan(9);
        BinaryPrimitives.WriteUInt32LittleEndian(
            corruptedSecondFrameData.AsSpan(5, sizeof(uint)),
            MiPlaySafetyDataCodec.ComputeCrc32Mpeg2(corruptedCiphertext));

        var receiver = new MiPlaySafetyDataSessionCipher(key, iv);

        Assert.True(receiver.TryDecryptVersion1(firstFrameData, out var firstDecoded));
        Assert.NotNull(firstDecoded);
        Assert.Equal(firstPlaintext, firstDecoded.Plaintext);
        Assert.False(receiver.TryDecryptVersion1(corruptedSecondFrameData, out var corruptedDecoded));
        Assert.Null(corruptedDecoded);
        Assert.True(receiver.TryDecryptVersion1(secondFrameData, out var secondDecoded));
        Assert.NotNull(secondDecoded);
        Assert.Equal(secondPlaintext, secondDecoded.Plaintext);
    }
    [Fact]
    public void SafetyAuthMutualExchangeRequiresLocalChallengeAcknowledgementToo()
    {
        var authKey = "d24264ef9bb7ddf04a6062358fc7849e";
        var localChallenge = MiPlaySafetyAuthCodec.CreateChallenge(123_456_789);
        var peerAcknowledgement = MiPlaySafetyAuthCodec.CreateAcknowledgement(
            localChallenge.AuthMessage,
            authKey,
            MiPlaySafetyHashAlgorithm.Sha256);

        Assert.True(MiPlaySafetyAuthCodec.VerifyAcknowledgement(
            localChallenge.AuthMessage,
            authKey,
            MiPlaySafetyHashAlgorithm.Sha256,
            peerAcknowledgement));
        var mutatedPeerAcknowledgement = peerAcknowledgement.AuthMessageAck[^1] == '0'
            ? peerAcknowledgement.AuthMessageAck[..^1] + "1"
            : peerAcknowledgement.AuthMessageAck[..^1] + "0";
        Assert.False(MiPlaySafetyAuthCodec.VerifyAcknowledgement(
            localChallenge.AuthMessage,
            authKey,
            MiPlaySafetyHashAlgorithm.Sha256,
            new MiPlaySafetyAuthAcknowledgement(mutatedPeerAcknowledgement)));

        var peerChallenge = new MiPlaySafetyAuthChallenge("c0e81b8e05502738463dbdb7ce8fdc4d");
        var localAcknowledgement = MiPlaySafetyAuthCodec.CreateAcknowledgement(
            peerChallenge.AuthMessage,
            authKey,
            MiPlaySafetyHashAlgorithm.Sha256);

        Assert.Equal(
            "3e5811cf3a0c2a9dabeab49f2ec145196b0a26ad091aed353ba56c6c8ede4a65",
            localAcknowledgement.AuthMessageAck);
        Assert.NotEqual(peerAcknowledgement.AuthMessageAck, localAcknowledgement.AuthMessageAck);
    }

    [Fact]
    public void SafetyDataCodecRoundTripsValidatedEnvelopeAndRejectsMalformedContainers()
    {
        var key = Encoding.ASCII.GetBytes("0123456789abcdef");
        var iv = Encoding.ASCII.GetBytes("fedcba9876543210");
        var plaintext = MiPlaySafetyEnvelopeCodec.Encode(
            true,
            MiPlayProtocolConstants.SafetyValueType,
            """{"result":"1","authMsgAck":"0123456789abcdef"}"""u8);
        var encoded = MiPlaySafetyDataCodec.EncryptVersion1(plaintext, key, iv);

        Assert.Equal(new byte[] { 0, 7, 1, 0xE0 }, encoded.AsSpan(0, 4).ToArray());
        Assert.True(MiPlaySafetyDataHeaderCodec.TryDecodeVersion1(encoded, out var header));
        Assert.NotNull(header);
        Assert.Equal((byte)9, header.PaddingLength);
        Assert.Equal(
            MiPlaySafetyDataCodec.ComputeCrc32Mpeg2(encoded.AsSpan(header.PayloadOffset)),
            BinaryPrimitives.ReadUInt32LittleEndian(encoded.AsSpan(5, sizeof(uint))));
        Assert.True(MiPlaySafetyDataCodec.TryDecryptVersion1(encoded, key, iv, out var decoded));
        Assert.NotNull(decoded);
        Assert.Equal(plaintext, decoded.Plaintext);

        var invalidCrc = encoded.ToArray();
        invalidCrc[5] ^= 0x01;
        Assert.False(MiPlaySafetyDataCodec.TryDecryptVersion1(invalidCrc, key, iv, out _));
        var invalidFlags = encoded.ToArray();
        invalidFlags[3] = 0xC0;
        Assert.False(MiPlaySafetyDataCodec.TryDecryptVersion1(invalidFlags, key, iv, out _));
        var invalidPadding = encoded.ToArray();
        invalidPadding[4] = 0;
        Assert.False(MiPlaySafetyDataCodec.TryDecryptVersion1(invalidPadding, key, iv, out _));
        Assert.False(MiPlaySafetyDataCodec.TryDecryptVersion1(encoded[..^1], key, iv, out _));
    }

    [Fact]
    public void SafetyDataCodecAddsAWholeZeroBlockForAlignedPlaintext()
    {
        var key = Encoding.ASCII.GetBytes("0123456789abcdef");
        var iv = Encoding.ASCII.GetBytes("fedcba9876543210");
        var encoded = MiPlaySafetyDataCodec.EncryptVersion1(new byte[16], key, iv);

        Assert.Equal((byte)16, encoded[4]);
        Assert.Equal(9 + 32, encoded.Length);
        Assert.True(MiPlaySafetyDataCodec.TryDecryptVersion1(encoded, key, iv, out var decoded));
        Assert.NotNull(decoded);
        Assert.Equal(new byte[16], decoded.Plaintext);
    }
    [Fact]
    public void SafetyAuthUsesLowercaseHexAndVerifiesAcknowledgements()
    {
        var challenge = MiPlaySafetyAuthCodec.CreateChallenge(123_456_789);

        Assert.Equal("25f9e794323b453885f5181f1b624d0b", challenge.AuthMessage);
        Assert.Equal(
            """{"authMsg":"25f9e794323b453885f5181f1b624d0b"}""",
            Encoding.UTF8.GetString(challenge.ToJsonPayload()));

        var acknowledgement = MiPlaySafetyAuthCodec.CreateAcknowledgement(
            "The quick brown fox jumps over the lazy dog",
            "key",
            MiPlaySafetyHashAlgorithm.Sha256);

        Assert.Equal(
            "f7bc83f430538424b13298e6aa6fb143ef4d59a14946175997479dbc2d1a3cd8",
            acknowledgement.AuthMessageAck);
        Assert.True(MiPlaySafetyAuthCodec.VerifyAcknowledgement(
            "The quick brown fox jumps over the lazy dog",
            "key",
            MiPlaySafetyHashAlgorithm.Sha256,
            acknowledgement));
        Assert.True(MiPlaySafetyAuthCodec.TryDecodeAcknowledgement(
            acknowledgement.ToJsonPayload(),
            out var decoded));
        Assert.Equal(acknowledgement, decoded);
        Assert.True(MiPlaySafetyAuthCodec.TryDecodeAcknowledgement(
            """{"result":"0","authMsgAck":"0123456789abcdef"}"""u8,
            out var resultZeroAcknowledgement));
        Assert.NotNull(resultZeroAcknowledgement);
        Assert.Equal("0", resultZeroAcknowledgement.Result);
        Assert.Equal("0123456789abcdef", resultZeroAcknowledgement.AuthMessageAck);
        Assert.False(MiPlaySafetyAuthCodec.TryDecodeAcknowledgement(
            """{"result":"2","authMsgAck":"0123456789abcdef"}"""u8,
            out _));
    }

    [Fact]
    public void TypeOneSafetyKeyDerivationRewritesOnlyAsciiDigits()
    {
        var key = MiPlaySafetyKeyDerivation.DeriveType1("a1", 2, "b3", 4);

        Assert.Equal("e86f41d69a8701c417b0827439fe2388", key);
    }

    [Fact]
    public void DerivedAesMaterialUsesTheSelectedAuthKeyHalf()
    {
        const string authKey = "ABCDEFGHIJKLMNOPQRSTUVWX01234567";

        Assert.Equal(
            "ABCDEFGHIJKLMNOP",
            MiPlaySafetyKeyDerivation.SelectDerivedAesMaterial(
                authKey,
                MiPlaySafetyKeyDerivation.FirstHalfMaterialType));
        Assert.Equal(
            "QRSTUVWX01234567",
            MiPlaySafetyKeyDerivation.SelectDerivedAesMaterial(
                authKey,
                MiPlaySafetyKeyDerivation.SecondHalfMaterialType));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MiPlaySafetyKeyDerivation.SelectDerivedAesMaterial(authKey, materialType: 4));
        Assert.Equal(
            "ABCDEFGHIJKLMNOP",
            MiPlaySafetyKeyDerivation.SelectObservedS12InboundSafetyIvMaterial(
                authKey,
                aesKeyType: 1,
                aesIvType: 2));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MiPlaySafetyKeyDerivation.SelectObservedS12InboundSafetyIvMaterial(
                authKey,
                aesKeyType: 1,
                aesIvType: 1));
    }

    [Fact]
    public void PostAuthHeartbeatUsesEncryptedEmptyPayloadAndSessionCbcState()
    {
        var key = Encoding.ASCII.GetBytes("0123456789abcdef");
        var iv = Encoding.ASCII.GetBytes("fedcba9876543210");
        var sender = new MiPlaySafetyDataSessionCipher(key, iv);

        var previousSafetyData = sender.EncryptVersion1("previous-auth-frame"u8);
        var heartbeatSafetyData = sender.EncryptVersion1(ReadOnlySpan<byte>.Empty);

        Assert.Equal(25, heartbeatSafetyData.Length);
        Assert.Equal((byte)16, heartbeatSafetyData[4]);
        Assert.NotEqual(
            MiPlaySafetyDataCodec.EncryptVersion1(ReadOnlySpan<byte>.Empty, key, iv),
            heartbeatSafetyData);

        var heartbeatFrame = MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.HeartbeatCommand,
            sequence: 4,
            heartbeatSafetyData);
        Assert.True(MiPlayCommandFrameCodec.TryDecode(heartbeatFrame, out var decodedFrame, out var bytesConsumed));
        Assert.NotNull(decodedFrame);
        Assert.Equal(heartbeatFrame.Length, bytesConsumed);
        Assert.Equal(MiPlayProtocolConstants.HeartbeatCommand, decodedFrame.Command);
        Assert.Equal((ushort)4, decodedFrame.Sequence);
        Assert.Equal(heartbeatSafetyData, decodedFrame.Payload);

        var receiver = new MiPlaySafetyDataSessionCipher(key, iv);
        Assert.True(receiver.TryDecryptVersion1(previousSafetyData, out var previousDecoded));
        Assert.NotNull(previousDecoded);
        Assert.Equal("previous-auth-frame"u8.ToArray(), previousDecoded.Plaintext);
        Assert.True(receiver.TryDecryptVersion1(heartbeatSafetyData, out var heartbeatDecoded));
        Assert.NotNull(heartbeatDecoded);
        Assert.Empty(heartbeatDecoded.Plaintext);
    }

    [Fact]
    public void PostAuthGetDeviceInfoUsesEncryptedEmptyPayloadAndSessionCbcState()
    {
        var key = Encoding.ASCII.GetBytes("0123456789abcdef");
        var iv = Encoding.ASCII.GetBytes("fedcba9876543210");
        var sender = new MiPlaySafetyDataSessionCipher(key, iv);

        var previousSafetyData = sender.EncryptVersion1("previous-auth-frame"u8);
        var getDeviceInfoSafetyData = sender.EncryptVersion1(ReadOnlySpan<byte>.Empty);

        Assert.Equal(25, getDeviceInfoSafetyData.Length);
        Assert.Equal((byte)16, getDeviceInfoSafetyData[4]);
        Assert.NotEqual(
            MiPlaySafetyDataCodec.EncryptVersion1(ReadOnlySpan<byte>.Empty, key, iv),
            getDeviceInfoSafetyData);

        var getDeviceInfoFrame = MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.GetDeviceInfoCommand,
            sequence: 4,
            getDeviceInfoSafetyData);
        Assert.True(MiPlayCommandFrameCodec.TryDecode(getDeviceInfoFrame, out var decodedFrame, out var bytesConsumed));
        Assert.NotNull(decodedFrame);
        Assert.Equal(getDeviceInfoFrame.Length, bytesConsumed);
        Assert.Equal(MiPlayProtocolConstants.GetDeviceInfoCommand, decodedFrame.Command);
        Assert.Equal((ushort)4, decodedFrame.Sequence);
        Assert.Equal(getDeviceInfoSafetyData, decodedFrame.Payload);

        var receiver = new MiPlaySafetyDataSessionCipher(key, iv);
        Assert.True(receiver.TryDecryptVersion1(previousSafetyData, out var previousDecoded));
        Assert.NotNull(previousDecoded);
        Assert.Equal("previous-auth-frame"u8.ToArray(), previousDecoded.Plaintext);
        Assert.True(receiver.TryDecryptVersion1(getDeviceInfoSafetyData, out var getDeviceInfoDecoded));
        Assert.NotNull(getDeviceInfoDecoded);
        Assert.Empty(getDeviceInfoDecoded.Plaintext);
    }

    [Fact]
    public void PostAuthSetLocalDeviceInfoUsesEncryptedJsonPayloadAndSessionCbcState()
    {
        var key = Encoding.ASCII.GetBytes("0123456789abcdef");
        var iv = Encoding.ASCII.GetBytes("fedcba9876543210");
        var sender = new MiPlaySafetyDataSessionCipher(key, iv);

        var previousSafetyData = sender.EncryptVersion1("previous-auth-frame"u8);
        var getDeviceInfoSafetyData = sender.EncryptVersion1(ReadOnlySpan<byte>.Empty);
        var localDeviceInfoPayload = MiPlayLocalDeviceInfoPayloadCodec.EncodeSourceName(
            sourceName: "Windows",
            bluetoothMac: "",
            canAlonePlayCtrl: "0");
        var setLocalDeviceInfoSafetyData = sender.EncryptVersion1(localDeviceInfoPayload);

        Assert.True(setLocalDeviceInfoSafetyData.Length > localDeviceInfoPayload.Length);
        Assert.NotEqual(localDeviceInfoPayload, setLocalDeviceInfoSafetyData);
        Assert.NotEqual(
            MiPlaySafetyDataCodec.EncryptVersion1(localDeviceInfoPayload, key, iv),
            setLocalDeviceInfoSafetyData);

        var setLocalDeviceInfoFrame = MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.SetLocalDeviceInfoCommand,
            sequence: 5,
            setLocalDeviceInfoSafetyData);
        Assert.True(MiPlayCommandFrameCodec.TryDecode(setLocalDeviceInfoFrame, out var decodedFrame, out var bytesConsumed));
        Assert.NotNull(decodedFrame);
        Assert.Equal(setLocalDeviceInfoFrame.Length, bytesConsumed);
        Assert.Equal(MiPlayProtocolConstants.SetLocalDeviceInfoCommand, decodedFrame.Command);
        Assert.Equal((ushort)5, decodedFrame.Sequence);
        Assert.Equal(setLocalDeviceInfoSafetyData, decodedFrame.Payload);

        var receiver = new MiPlaySafetyDataSessionCipher(key, iv);
        Assert.True(receiver.TryDecryptVersion1(previousSafetyData, out var previousDecoded));
        Assert.NotNull(previousDecoded);
        Assert.Equal("previous-auth-frame"u8.ToArray(), previousDecoded.Plaintext);
        Assert.True(receiver.TryDecryptVersion1(getDeviceInfoSafetyData, out var getDeviceInfoDecoded));
        Assert.NotNull(getDeviceInfoDecoded);
        Assert.Empty(getDeviceInfoDecoded.Plaintext);
        Assert.True(receiver.TryDecryptVersion1(setLocalDeviceInfoSafetyData, out var setLocalDeviceInfoDecoded));
        Assert.NotNull(setLocalDeviceInfoDecoded);
        Assert.Equal(localDeviceInfoPayload, setLocalDeviceInfoDecoded.Plaintext);
    }

    [Fact]
    public void PostAuthLocalDeviceInfoSequenceUsesGetDeviceInfoThenTwoEncryptedSetLocalDeviceInfoFrames()
    {
        var key = Encoding.ASCII.GetBytes("0123456789abcdef");
        var iv = Encoding.ASCII.GetBytes("fedcba9876543210");
        var sender = new MiPlaySafetyDataSessionCipher(key, iv);

        var previousSafetyData = sender.EncryptVersion1("previous-auth-frame"u8);
        var getDeviceInfoSafetyData = sender.EncryptVersion1(ReadOnlySpan<byte>.Empty);
        var sourceNamePayload = MiPlayLocalDeviceInfoPayloadCodec.EncodeSourceName(
            sourceName: "DLNACast Windows",
            bluetoothMac: null,
            canAlonePlayCtrl: "0");
        var sourceNameSafetyData = sender.EncryptVersion1(sourceNamePayload);
        var localDeviceInfoPayload = MiPlayLocalDeviceInfoPayloadCodec.EncodeLocalDeviceInfo(
            model: "Windows",
            romVersion: "Windows 11",
            appVersion: 1);
        var localDeviceInfoSafetyData = sender.EncryptVersion1(localDeviceInfoPayload);

        Assert.NotEqual(sourceNamePayload, sourceNameSafetyData);
        Assert.NotEqual(localDeviceInfoPayload, localDeviceInfoSafetyData);
        Assert.NotEqual(
            MiPlaySafetyDataCodec.EncryptVersion1(localDeviceInfoPayload, key, iv),
            localDeviceInfoSafetyData);

        var getDeviceInfoFrame = MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.GetDeviceInfoCommand,
            sequence: 4,
            getDeviceInfoSafetyData);
        var sourceNameFrame = MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.SetLocalDeviceInfoCommand,
            sequence: 5,
            sourceNameSafetyData);
        var localDeviceInfoFrame = MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.SetLocalDeviceInfoCommand,
            sequence: 6,
            localDeviceInfoSafetyData);

        Assert.True(MiPlayCommandFrameCodec.TryDecode(getDeviceInfoFrame, out var decodedGetDeviceInfoFrame, out var getDeviceInfoBytesConsumed));
        Assert.NotNull(decodedGetDeviceInfoFrame);
        Assert.Equal(getDeviceInfoFrame.Length, getDeviceInfoBytesConsumed);
        Assert.Equal(MiPlayProtocolConstants.GetDeviceInfoCommand, decodedGetDeviceInfoFrame.Command);
        Assert.Equal((ushort)4, decodedGetDeviceInfoFrame.Sequence);
        Assert.Equal(getDeviceInfoSafetyData, decodedGetDeviceInfoFrame.Payload);

        Assert.True(MiPlayCommandFrameCodec.TryDecode(sourceNameFrame, out var decodedSourceNameFrame, out var sourceNameBytesConsumed));
        Assert.NotNull(decodedSourceNameFrame);
        Assert.Equal(sourceNameFrame.Length, sourceNameBytesConsumed);
        Assert.Equal(MiPlayProtocolConstants.SetLocalDeviceInfoCommand, decodedSourceNameFrame.Command);
        Assert.Equal((ushort)5, decodedSourceNameFrame.Sequence);
        Assert.Equal(sourceNameSafetyData, decodedSourceNameFrame.Payload);

        Assert.True(MiPlayCommandFrameCodec.TryDecode(localDeviceInfoFrame, out var decodedLocalDeviceInfoFrame, out var localDeviceInfoBytesConsumed));
        Assert.NotNull(decodedLocalDeviceInfoFrame);
        Assert.Equal(localDeviceInfoFrame.Length, localDeviceInfoBytesConsumed);
        Assert.Equal(MiPlayProtocolConstants.SetLocalDeviceInfoCommand, decodedLocalDeviceInfoFrame.Command);
        Assert.Equal((ushort)6, decodedLocalDeviceInfoFrame.Sequence);
        Assert.Equal(localDeviceInfoSafetyData, decodedLocalDeviceInfoFrame.Payload);

        var receiver = new MiPlaySafetyDataSessionCipher(key, iv);
        Assert.True(receiver.TryDecryptVersion1(previousSafetyData, out var previousDecoded));
        Assert.NotNull(previousDecoded);
        Assert.Equal("previous-auth-frame"u8.ToArray(), previousDecoded.Plaintext);
        Assert.True(receiver.TryDecryptVersion1(getDeviceInfoSafetyData, out var getDeviceInfoDecoded));
        Assert.NotNull(getDeviceInfoDecoded);
        Assert.Empty(getDeviceInfoDecoded.Plaintext);
        Assert.True(receiver.TryDecryptVersion1(sourceNameSafetyData, out var sourceNameDecoded));
        Assert.NotNull(sourceNameDecoded);
        Assert.Equal(sourceNamePayload, sourceNameDecoded.Plaintext);
        Assert.True(receiver.TryDecryptVersion1(localDeviceInfoSafetyData, out var localDeviceInfoDecoded));
        Assert.NotNull(localDeviceInfoDecoded);
        Assert.Equal(localDeviceInfoPayload, localDeviceInfoDecoded.Plaintext);
    }

    [Fact]
    public void PostAuthLocalDeviceInfoPolicyRequiresMatchingMinimumLengthGetDeviceInfoAck()
    {
        var accepted = MiPlayPostAuthProbePolicy.EvaluateStagedLocalDeviceInfoGate(
            awaitingGetDeviceInfoAcknowledgement: true,
            hasLocalDeviceInfoPayloads: true,
            alreadySentLocalDeviceInfo: false,
            observedCommand: MiPlayProtocolConstants.GetDeviceInfoAcknowledgementCommand,
            observedSequence: 4,
            expectedGetDeviceInfoSequence: 4,
            decryptedPayloadLength: MiPlayPostAuthProbePolicy.MinimumDeviceInfoAcknowledgementPayloadLength);

        Assert.True(accepted.CanSend);

        var wrongSequence = MiPlayPostAuthProbePolicy.EvaluateStagedLocalDeviceInfoGate(
            awaitingGetDeviceInfoAcknowledgement: true,
            hasLocalDeviceInfoPayloads: true,
            alreadySentLocalDeviceInfo: false,
            observedCommand: MiPlayProtocolConstants.GetDeviceInfoAcknowledgementCommand,
            observedSequence: 5,
            expectedGetDeviceInfoSequence: 4,
            decryptedPayloadLength: MiPlayPostAuthProbePolicy.MinimumDeviceInfoAcknowledgementPayloadLength);
        var shortPayload = MiPlayPostAuthProbePolicy.EvaluateStagedLocalDeviceInfoGate(
            awaitingGetDeviceInfoAcknowledgement: true,
            hasLocalDeviceInfoPayloads: true,
            alreadySentLocalDeviceInfo: false,
            observedCommand: MiPlayProtocolConstants.GetDeviceInfoAcknowledgementCommand,
            observedSequence: 4,
            expectedGetDeviceInfoSequence: 4,
            decryptedPayloadLength: MiPlayPostAuthProbePolicy.MinimumDeviceInfoAcknowledgementPayloadLength - 1);
        var duplicate = MiPlayPostAuthProbePolicy.EvaluateStagedLocalDeviceInfoGate(
            awaitingGetDeviceInfoAcknowledgement: true,
            hasLocalDeviceInfoPayloads: true,
            alreadySentLocalDeviceInfo: true,
            observedCommand: MiPlayProtocolConstants.GetDeviceInfoAcknowledgementCommand,
            observedSequence: 4,
            expectedGetDeviceInfoSequence: 4,
            decryptedPayloadLength: MiPlayPostAuthProbePolicy.MinimumDeviceInfoAcknowledgementPayloadLength);
        var wrongCommand = MiPlayPostAuthProbePolicy.EvaluateStagedLocalDeviceInfoGate(
            awaitingGetDeviceInfoAcknowledgement: true,
            hasLocalDeviceInfoPayloads: true,
            alreadySentLocalDeviceInfo: false,
            observedCommand: MiPlayProtocolConstants.SetLocalDeviceInfoAcknowledgementCommand,
            observedSequence: 4,
            expectedGetDeviceInfoSequence: 4,
            decryptedPayloadLength: MiPlayPostAuthProbePolicy.MinimumDeviceInfoAcknowledgementPayloadLength);

        Assert.False(wrongSequence.CanSend);
        Assert.False(shortPayload.CanSend);
        Assert.False(duplicate.CanSend);
        Assert.False(wrongCommand.CanSend);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MiPlayPostAuthProbePolicy.EvaluateStagedLocalDeviceInfoGate(
                awaitingGetDeviceInfoAcknowledgement: true,
                hasLocalDeviceInfoPayloads: true,
                alreadySentLocalDeviceInfo: false,
                observedCommand: MiPlayProtocolConstants.GetDeviceInfoAcknowledgementCommand,
                observedSequence: 4,
                expectedGetDeviceInfoSequence: 4,
                decryptedPayloadLength: -1));
    }

    [Fact]
    public void LegacySafetyChallengeProducesTheVerifiedResponseFrame()
    {
        var challengeFrame = MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.LegacySafetyChallengeCommand,
            sequence: 0x9912,
            "legacy-challenge"u8);

        var created = MiPlayLegacySafetyChallengeCodec.TryCreateAcknowledgement(
            challengeFrame,
            out var acknowledgement,
            out var bytesConsumed);

        Assert.True(created);
        Assert.NotNull(acknowledgement);
        Assert.Equal((ushort)0x9912, acknowledgement.Sequence);
        Assert.Equal("1bfbbecf1244c16add4362959aa0ccc7b6e8a0c4", acknowledgement.Response);
        Assert.Equal(challengeFrame.Length, bytesConsumed);

        var responseFrame = MiPlayLegacySafetyChallengeCodec.EncodeAcknowledgement(acknowledgement);
        Assert.Equal(
            MiPlayProtocolConstants.LegacySafetyAcknowledgementCommand,
            BinaryPrimitives.ReadUInt16BigEndian(responseFrame.AsSpan(1, 2)));
        Assert.Equal((ushort)0x9912, BinaryPrimitives.ReadUInt16BigEndian(responseFrame.AsSpan(3, 2)));
        Assert.Equal(acknowledgement.Response, Encoding.ASCII.GetString(responseFrame.AsSpan(9)));
    }
}
