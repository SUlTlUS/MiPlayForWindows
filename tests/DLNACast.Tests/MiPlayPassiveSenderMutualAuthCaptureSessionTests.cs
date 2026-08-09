using System.Net;
using System.Text;
using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayPassiveSenderMutualAuthCaptureSessionTests
{
    [Fact]
    public void PeerSourceRoleDerivationMatchesCapturedOfficialPhoneSessionKey()
    {
        var receiverView = new MiPlayTcpSessionInfo(
            IPAddress.Parse("192.168.10.7"),
            8899,
            IPAddress.Parse("192.168.10.20"),
            43720);

        Assert.Equal(
            "a565e5251cce7d9995e34b18bb656c33",
            receiverView.DeriveType1SafetyKeyForPeerSourceRole());
        Assert.NotEqual(
            receiverView.DeriveType1SafetyKey(),
            receiverView.DeriveType1SafetyKeyForPeerSourceRole());
    }

    [Fact]
    public void CapturedPhoneOfferProducesSameSequenceBoundedSafetyInfoAcknowledgement()
    {
        var receiverSession = new MiPlayTcpSessionInfo(
            IPAddress.Parse("192.168.10.9"),
            8899,
            IPAddress.Parse("192.168.10.20"),
            49432);
        var capturedOffer = Convert.FromBase64String(
            MiPlayPassiveSenderBootstrapCaptureEvidence.SafetyInfoOfferFrameBase64);

        Assert.True(MiPlayPassiveSenderMutualAuthCaptureSession.TryCreate(
            receiverSession,
            capturedOffer,
            out var session,
            out var acknowledgementFrame,
            out var error), error);
        Assert.NotNull(session);
        Assert.NotNull(acknowledgementFrame);
        Assert.True(MiPlaySafetyCommandCodec.TryDecode(
            acknowledgementFrame,
            out var acknowledgement,
            out var bytesConsumed));
        Assert.NotNull(acknowledgement);
        Assert.Equal(acknowledgementFrame.Length, bytesConsumed);
        Assert.Equal(MiPlayProtocolConstants.SafetyInfoAcknowledgementCommand, acknowledgement.Command);
        Assert.Equal((ushort)1, acknowledgement.Sequence);
        Assert.True(acknowledgement.IsAcknowledgement);
        Assert.True(MiPlaySafetyInfoCodec.TryDecodeSelection(
            acknowledgement.JsonPayload,
            out var selection));
        Assert.Equal(MiPlayPassiveSenderMutualAuthCaptureSession.ReceiverSelection, selection);
        Assert.False(session.MutualSafetyAuthComplete);
        Assert.False(session.FirstPostAuthFrameCaptured);
    }

    [Fact]
    public void RejectsOfferWithoutSelectedType2AesIvCapability()
    {
        var receiverSession = CreateReceiverSession();
        var offer = new MiPlaySafetyInfoOffer(
            AuthKeyTypes: 1,
            AuthAlgorithmTypes: 7,
            IntegrityTypes: 1,
            AesKeyTypes: 3,
            AesIvTypes: 1);
        var offerFrame = MiPlaySafetyCommandCodec.Encode(
            MiPlayProtocolConstants.SafetyInfoCommand,
            sequence: 1,
            offer.ToJsonPayload());

        Assert.False(MiPlayPassiveSenderMutualAuthCaptureSession.TryCreate(
            receiverSession,
            offerFrame,
            out var session,
            out var acknowledgementFrame,
            out var error));
        Assert.Null(session);
        Assert.Null(acknowledgementFrame);
        Assert.Contains("(1,4,1,1,2)", error, StringComparison.Ordinal);
    }

    [Fact]
    public void OutboundPolicyPermitsAuthenticationOnly()
    {
        Assert.True(MiPlayPassiveSenderMutualAuthCaptureSession.IsPermittedOutboundCommand(
            MiPlayProtocolConstants.LegacySafetyChallengeCommand));
        Assert.True(MiPlayPassiveSenderMutualAuthCaptureSession.IsPermittedOutboundCommand(
            MiPlayProtocolConstants.SafetyInfoAcknowledgementCommand));
        Assert.True(MiPlayPassiveSenderMutualAuthCaptureSession.IsPermittedOutboundCommand(
            MiPlayProtocolConstants.SafetyAuthCommand));
        Assert.True(MiPlayPassiveSenderMutualAuthCaptureSession.IsPermittedOutboundCommand(
            MiPlayProtocolConstants.SafetyAuthAcknowledgementCommand));

        Assert.False(MiPlayPassiveSenderMutualAuthCaptureSession.IsPermittedOutboundCommand(
            MiPlayProtocolConstants.GetDeviceInfoCommand));
        Assert.False(MiPlayPassiveSenderMutualAuthCaptureSession.IsPermittedOutboundCommand(
            MiPlayProtocolConstants.SetLocalDeviceInfoCommand));
        Assert.False(MiPlayPassiveSenderMutualAuthCaptureSession.IsPermittedOutboundCommand(
            MiPlayProtocolConstants.SetPlaySourceCommand));
        Assert.False(MiPlayPassiveSenderMutualAuthCaptureSession.IsPermittedOutboundCommand(
            MiPlayProtocolConstants.OpenDeviceCommand));
    }

    [Fact]
    public void FullOfflineMutualAuthContinuesPhoneOutboundCbcIntoExactlyOnePostAuthFrame()
    {
        var receiverSession = CreateReceiverSession();
        var offer = new MiPlaySafetyInfoOffer(
            AuthKeyTypes: 1,
            AuthAlgorithmTypes: 7,
            IntegrityTypes: 1,
            AesKeyTypes: 3,
            AesIvTypes: 3);
        var offerFrame = MiPlaySafetyCommandCodec.Encode(
            MiPlayProtocolConstants.SafetyInfoCommand,
            sequence: 1,
            offer.ToJsonPayload());

        Assert.True(MiPlayPassiveSenderMutualAuthCaptureSession.TryCreate(
            receiverSession,
            offerFrame,
            out var receiver,
            out _,
            out var error), error);
        Assert.NotNull(receiver);

        var authKey = receiverSession.DeriveType1SafetyKeyForPeerSourceRole();
        var aesKey = Encoding.ASCII.GetBytes(MiPlaySafetyKeyDerivation.SelectDerivedAesMaterial(
            authKey,
            MiPlaySafetyKeyDerivation.FirstHalfMaterialType));
        var aesIv = Encoding.ASCII.GetBytes(MiPlaySafetyKeyDerivation.SelectDerivedAesMaterial(
            authKey,
            MiPlaySafetyKeyDerivation.SecondHalfMaterialType));
        var phoneCipher = new MiPlaySafetyDataSessionCipher(aesKey, aesIv);

        var receiverChallengeFrame = receiver.BuildLocalChallengeFrame(1_725_000_000_000_000);
        var receiverChallenge = DecodeChallengeForPeer(receiverChallengeFrame, phoneCipher);

        var phoneChallenge = MiPlaySafetyAuthCodec.CreateChallenge(1_725_000_000_000_001);
        var phoneChallengeFrame = EncodeSafetyAuthFrame(
            MiPlayProtocolConstants.SafetyAuthCommand,
            sequence: 0x0042,
            isAcknowledgement: false,
            phoneChallenge.ToJsonPayload(),
            phoneCipher);
        var peerChallengeResult = receiver.ProcessInboundFrame(phoneChallengeFrame);

        Assert.True(peerChallengeResult.Accepted);
        Assert.Equal("peer-challenge-acknowledged", peerChallengeResult.Phase);
        Assert.NotNull(peerChallengeResult.ResponseFrame);
        Assert.False(peerChallengeResult.MutualSafetyAuthComplete);

        var phoneAcknowledgement = MiPlaySafetyAuthCodec.CreateAcknowledgement(
            receiverChallenge.AuthMessage,
            authKey,
            MiPlaySafetyHashAlgorithm.Sha256);
        var phoneAcknowledgementFrame = EncodeSafetyAuthFrame(
            MiPlayProtocolConstants.SafetyAuthAcknowledgementCommand,
            MiPlayPassiveSenderMutualAuthCaptureSession.ReceiverChallengeSequence,
            isAcknowledgement: true,
            phoneAcknowledgement.ToJsonPayload(),
            phoneCipher);
        var peerAcknowledgementResult = receiver.ProcessInboundFrame(phoneAcknowledgementFrame);

        Assert.True(peerAcknowledgementResult.Accepted);
        Assert.Equal("local-challenge-verified", peerAcknowledgementResult.Phase);
        Assert.True(peerAcknowledgementResult.MutualSafetyAuthComplete);
        Assert.True(receiver.MutualSafetyAuthComplete);

        var receiverAcknowledgement = DecodeAcknowledgementForPeer(
            peerChallengeResult.ResponseFrame,
            phoneCipher);
        Assert.True(MiPlaySafetyAuthCodec.VerifyAcknowledgement(
            phoneChallenge.AuthMessage,
            authKey,
            MiPlaySafetyHashAlgorithm.Sha256,
            receiverAcknowledgement));

        var firstPostAuthFrame = MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.GetDeviceInfoCommand,
            sequence: 3,
            phoneCipher.EncryptVersion1([]));
        var capture = receiver.ProcessInboundFrame(firstPostAuthFrame);

        Assert.True(capture.Accepted);
        Assert.Equal("first-post-auth-frame", capture.Phase);
        Assert.True(capture.MutualSafetyAuthComplete);
        Assert.Null(capture.ResponseFrame);
        Assert.Equal(MiPlayProtocolConstants.GetDeviceInfoCommand, capture.CapturedCommand);
        Assert.Equal((ushort)3, capture.CapturedSequence);
        Assert.NotNull(capture.CapturedPlaintext);
        Assert.Empty(capture.CapturedPlaintext);
        Assert.True(receiver.FirstPostAuthFrameCaptured);

        var duplicate = receiver.ProcessInboundFrame(firstPostAuthFrame);
        Assert.False(duplicate.Accepted);
        Assert.Equal("post-auth-capture-complete", duplicate.Phase);
        Assert.Null(duplicate.ResponseFrame);
    }

    private static MiPlayTcpSessionInfo CreateReceiverSession() =>
        new(
            IPAddress.Parse("192.168.10.9"),
            8899,
            IPAddress.Parse("192.168.10.20"),
            42509);

    private static byte[] EncodeSafetyAuthFrame(
        ushort command,
        ushort sequence,
        bool isAcknowledgement,
        ReadOnlySpan<byte> jsonPayload,
        MiPlaySafetyDataSessionCipher cipher)
    {
        var plaintext = MiPlaySafetyEnvelopeCodec.Encode(
            isAcknowledgement,
            MiPlayProtocolConstants.SafetyValueType,
            jsonPayload);
        return MiPlayCommandFrameCodec.Encode(
            command,
            sequence,
            cipher.EncryptVersion1(plaintext));
    }

    private static MiPlaySafetyAuthChallenge DecodeChallengeForPeer(
        ReadOnlySpan<byte> frameBytes,
        MiPlaySafetyDataSessionCipher cipher)
    {
        var payload = DecodeSafetyAuthPayloadForPeer(
            frameBytes,
            expectedCommand: MiPlayProtocolConstants.SafetyAuthCommand,
            expectedAcknowledgement: false,
            cipher);
        Assert.True(MiPlaySafetyAuthCodec.TryDecodeChallenge(payload, out var challenge));
        return Assert.IsType<MiPlaySafetyAuthChallenge>(challenge);
    }

    private static MiPlaySafetyAuthAcknowledgement DecodeAcknowledgementForPeer(
        ReadOnlySpan<byte> frameBytes,
        MiPlaySafetyDataSessionCipher cipher)
    {
        var payload = DecodeSafetyAuthPayloadForPeer(
            frameBytes,
            expectedCommand: MiPlayProtocolConstants.SafetyAuthAcknowledgementCommand,
            expectedAcknowledgement: true,
            cipher);
        Assert.True(MiPlaySafetyAuthCodec.TryDecodeAcknowledgement(payload, out var acknowledgement));
        return Assert.IsType<MiPlaySafetyAuthAcknowledgement>(acknowledgement);
    }

    private static byte[] DecodeSafetyAuthPayloadForPeer(
        ReadOnlySpan<byte> frameBytes,
        ushort expectedCommand,
        bool expectedAcknowledgement,
        MiPlaySafetyDataSessionCipher cipher)
    {
        Assert.True(MiPlayCommandFrameCodec.TryDecode(frameBytes, out var frame, out var frameBytesConsumed));
        Assert.NotNull(frame);
        Assert.Equal(frameBytes.Length, frameBytesConsumed);
        Assert.Equal(expectedCommand, frame.Command);
        Assert.True(cipher.TryDecryptVersion1(frame.Payload, out var decoded));
        Assert.NotNull(decoded);
        Assert.True(MiPlaySafetyEnvelopeCodec.TryDecode(
            decoded.Plaintext,
            out var envelope,
            out var envelopeBytesConsumed));
        Assert.NotNull(envelope);
        Assert.Equal(decoded.Plaintext.Length, envelopeBytesConsumed);
        Assert.Equal(expectedAcknowledgement, envelope.IsAcknowledgement);
        return envelope.Payload;
    }
}
