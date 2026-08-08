using System.Security.Cryptography;
using System.Text;

namespace DLNACast.Core.MiPlay;

public sealed record MiPlayPassiveSenderMutualAuthInboundResult(
    bool Accepted,
    string Phase,
    bool MutualSafetyAuthComplete,
    byte[]? ResponseFrame,
    ushort? CapturedCommand,
    ushort? CapturedSequence,
    byte[]? CapturedPlaintext,
    string Boundary);

/// <summary>
/// Pure protocol state for a bounded test receiver that completes mutual
/// SafetyAuth with an official phone source, then decrypts exactly one fresh
/// post-auth frame. The class performs no network I/O and never builds a
/// business-command response.
/// </summary>
public sealed class MiPlayPassiveSenderMutualAuthCaptureSession
{
    public const ushort ReceiverChallengeSequence = 0;
    public const string Boundary =
        "authentication-only receiver: permit 0x1401, one local 0x1402, and one 0x1403 response; after mutual auth decrypt exactly one phone-originated post-auth frame and send no business ACK, control, RTSP, media, playback, or audio data";

    public static MiPlaySafetyInfoSelection ReceiverSelection { get; } = new(
        authKeyType: MiPlaySafetyKeyDerivation.FirstHalfMaterialType,
        authAlgorithmType: (uint)MiPlaySafetyHashAlgorithm.Sha256,
        integrityType: 1,
        aesKeyType: MiPlaySafetyKeyDerivation.FirstHalfMaterialType,
        aesIvType: MiPlaySafetyKeyDerivation.SecondHalfMaterialType);

    private readonly string authKey;
    private readonly MiPlaySafetyDataSessionCipher cipher;
    private MiPlaySafetyAuthChallenge? localChallenge;
    private bool localChallengeAcknowledged;
    private bool peerChallengeAcknowledged;
    private bool firstPostAuthFrameCaptured;

    private MiPlayPassiveSenderMutualAuthCaptureSession(string authKey)
    {
        this.authKey = authKey;
        var aesKey = Encoding.ASCII.GetBytes(MiPlaySafetyKeyDerivation.SelectDerivedAesMaterial(
            authKey,
            ReceiverSelection.AesKeyType!.Value));
        var aesIv = Encoding.ASCII.GetBytes(MiPlaySafetyKeyDerivation.SelectDerivedAesMaterial(
            authKey,
            ReceiverSelection.AesIvType!.Value));
        cipher = new MiPlaySafetyDataSessionCipher(aesKey, aesIv);
    }

    public bool MutualSafetyAuthComplete =>
        localChallengeAcknowledged && peerChallengeAcknowledged;

    public bool FirstPostAuthFrameCaptured => firstPostAuthFrameCaptured;

    public static bool IsPermittedOutboundCommand(ushort command) =>
        command is MiPlayProtocolConstants.LegacySafetyChallengeCommand or
            MiPlayProtocolConstants.SafetyInfoAcknowledgementCommand or
            MiPlayProtocolConstants.SafetyAuthCommand or
            MiPlayProtocolConstants.SafetyAuthAcknowledgementCommand;

    public static bool TryCreate(
        MiPlayTcpSessionInfo receiverSession,
        ReadOnlySpan<byte> safetyInfoFrame,
        out MiPlayPassiveSenderMutualAuthCaptureSession? session,
        out byte[]? safetyInfoAcknowledgementFrame,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(receiverSession);
        session = null;
        safetyInfoAcknowledgementFrame = null;
        error = string.Empty;

        if (!MiPlaySafetyCommandCodec.TryDecode(
                safetyInfoFrame,
                out var safetyCommand,
                out var bytesConsumed) ||
            safetyCommand is null ||
            bytesConsumed != safetyInfoFrame.Length ||
            safetyCommand.Command != MiPlayProtocolConstants.SafetyInfoCommand ||
            safetyCommand.IsAcknowledgement ||
            !MiPlaySafetyInfoCodec.TryDecodeOffer(safetyCommand.JsonPayload, out var offer) ||
            offer is null)
        {
            error = "The source SafetyInfo frame is not a complete 0x1400 offer.";
            return false;
        }

        if (!OfferSupportsSelection(offer, ReceiverSelection))
        {
            error = "The source SafetyInfo offer does not support the bounded receiver selection (1,4,1,1,2).";
            return false;
        }

        var authKey = receiverSession.DeriveType1SafetyKeyForPeerSourceRole();
        session = new MiPlayPassiveSenderMutualAuthCaptureSession(authKey);
        safetyInfoAcknowledgementFrame = MiPlaySafetyCommandCodec.Encode(
            MiPlayProtocolConstants.SafetyInfoAcknowledgementCommand,
            safetyCommand.Sequence,
            ReceiverSelection.ToJsonPayload());
        return true;
    }

    public byte[] BuildLocalChallengeFrame(long timestampMicroseconds)
    {
        if (localChallenge is not null)
        {
            throw new InvalidOperationException("The bounded receiver permits exactly one local SafetyAuth challenge.");
        }

        localChallenge = MiPlaySafetyAuthCodec.CreateChallenge(timestampMicroseconds);
        var plaintext = MiPlaySafetyEnvelopeCodec.Encode(
            isAcknowledgement: false,
            MiPlayProtocolConstants.SafetyValueType,
            localChallenge.ToJsonPayload());
        var safetyData = cipher.EncryptVersion1(plaintext);
        return MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.SafetyAuthCommand,
            ReceiverChallengeSequence,
            safetyData);
    }

    public MiPlayPassiveSenderMutualAuthInboundResult ProcessInboundFrame(
        ReadOnlySpan<byte> frameBytes)
    {
        if (!MiPlayCommandFrameCodec.TryDecode(
                frameBytes,
                out var frame,
                out var bytesConsumed) ||
            frame is null ||
            bytesConsumed != frameBytes.Length)
        {
            return Reject("invalid-command-frame", "The inbound bytes are not one complete MiPlay command frame.");
        }

        if (frame.Command == MiPlayProtocolConstants.SafetyAuthCommand)
        {
            return ProcessPeerChallenge(frame);
        }

        if (frame.Command == MiPlayProtocolConstants.SafetyAuthAcknowledgementCommand)
        {
            return ProcessPeerAcknowledgement(frame);
        }

        if (!MutualSafetyAuthComplete)
        {
            return Reject(
                "pre-mutual-unexpected-command",
                $"Command 0x{frame.Command:X4} arrived before both SafetyAuth directions were verified.");
        }

        if (firstPostAuthFrameCaptured)
        {
            return Reject(
                "post-auth-capture-complete",
                "The bounded receiver already captured its single permitted post-auth frame.");
        }

        if (!cipher.TryDecryptVersion1(frame.Payload, out var decoded) || decoded is null)
        {
            return Reject(
                "post-auth-decrypt-failed",
                $"The first post-auth command 0x{frame.Command:X4} did not decrypt with the continued type-2 inbound CBC state.");
        }

        firstPostAuthFrameCaptured = true;
        return new MiPlayPassiveSenderMutualAuthInboundResult(
            Accepted: true,
            Phase: "first-post-auth-frame",
            MutualSafetyAuthComplete: true,
            ResponseFrame: null,
            CapturedCommand: frame.Command,
            CapturedSequence: frame.Sequence,
            CapturedPlaintext: decoded.Plaintext,
            Boundary: Boundary);
    }

    private MiPlayPassiveSenderMutualAuthInboundResult ProcessPeerChallenge(MiPlayCommandFrame frame)
    {
        if (peerChallengeAcknowledged)
        {
            return Reject("duplicate-peer-challenge", "The bounded receiver permits exactly one peer 0x1402 challenge.");
        }

        if (!TryDecryptSafetyEnvelope(
                frame.Payload,
                expectedAcknowledgement: false,
                out var payload) ||
            !MiPlaySafetyAuthCodec.TryDecodeChallenge(payload, out var challenge) ||
            challenge is null)
        {
            return Reject("peer-challenge-decode-failed", "The peer 0x1402 did not decode as a complete cmd/authMsg challenge.");
        }

        var acknowledgement = MiPlaySafetyAuthCodec.CreateAcknowledgement(
            challenge.AuthMessage,
            authKey,
            MiPlaySafetyHashAlgorithm.Sha256);
        var acknowledgementPlaintext = MiPlaySafetyEnvelopeCodec.Encode(
            isAcknowledgement: true,
            MiPlayProtocolConstants.SafetyValueType,
            acknowledgement.ToJsonPayload());
        var acknowledgementSafetyData = cipher.EncryptVersion1(acknowledgementPlaintext);
        var acknowledgementFrame = MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.SafetyAuthAcknowledgementCommand,
            frame.Sequence,
            acknowledgementSafetyData);
        peerChallengeAcknowledged = true;

        return new MiPlayPassiveSenderMutualAuthInboundResult(
            Accepted: true,
            Phase: "peer-challenge-acknowledged",
            MutualSafetyAuthComplete: MutualSafetyAuthComplete,
            ResponseFrame: acknowledgementFrame,
            CapturedCommand: null,
            CapturedSequence: null,
            CapturedPlaintext: null,
            Boundary: Boundary);
    }

    private MiPlayPassiveSenderMutualAuthInboundResult ProcessPeerAcknowledgement(MiPlayCommandFrame frame)
    {
        if (localChallenge is null)
        {
            return Reject("unsolicited-peer-acknowledgement", "No local 0x1402 challenge is pending.");
        }

        if (localChallengeAcknowledged)
        {
            return Reject("duplicate-peer-acknowledgement", "The bounded receiver already verified its one peer 0x1403 acknowledgement.");
        }

        if (!TryDecryptSafetyEnvelope(
                frame.Payload,
                expectedAcknowledgement: true,
                out var payload) ||
            !MiPlaySafetyAuthCodec.TryDecodeAcknowledgement(payload, out var acknowledgement) ||
            acknowledgement is null ||
            !MiPlaySafetyAuthCodec.VerifyAcknowledgement(
                localChallenge.AuthMessage,
                authKey,
                MiPlaySafetyHashAlgorithm.Sha256,
                acknowledgement))
        {
            return Reject("peer-acknowledgement-verification-failed", "The peer 0x1403 did not verify against the local challenge.");
        }

        localChallengeAcknowledged = true;
        return new MiPlayPassiveSenderMutualAuthInboundResult(
            Accepted: true,
            Phase: "local-challenge-verified",
            MutualSafetyAuthComplete: MutualSafetyAuthComplete,
            ResponseFrame: null,
            CapturedCommand: null,
            CapturedSequence: null,
            CapturedPlaintext: null,
            Boundary: Boundary);
    }

    private bool TryDecryptSafetyEnvelope(
        ReadOnlySpan<byte> safetyData,
        bool expectedAcknowledgement,
        out byte[] payload)
    {
        payload = [];
        if (!cipher.TryDecryptVersion1(safetyData, out var decoded) ||
            decoded is null ||
            !MiPlaySafetyEnvelopeCodec.TryDecode(
                decoded.Plaintext,
                out var envelope,
                out var bytesConsumed) ||
            envelope is null ||
            bytesConsumed != decoded.Plaintext.Length ||
            envelope.IsAcknowledgement != expectedAcknowledgement)
        {
            return false;
        }

        payload = envelope.Payload;
        return true;
    }

    private MiPlayPassiveSenderMutualAuthInboundResult Reject(string phase, string reason) =>
        new(
            Accepted: false,
            Phase: phase,
            MutualSafetyAuthComplete: MutualSafetyAuthComplete,
            ResponseFrame: null,
            CapturedCommand: null,
            CapturedSequence: null,
            CapturedPlaintext: null,
            Boundary: $"{reason} {Boundary}");

    private static bool OfferSupportsSelection(
        MiPlaySafetyInfoOffer offer,
        MiPlaySafetyInfoSelection selection) =>
        Supports(offer.AuthKeyTypes, selection.AuthKeyType) &&
        Supports(offer.AuthAlgorithmTypes, selection.AuthAlgorithmType) &&
        Supports(offer.IntegrityTypes, selection.IntegrityType) &&
        Supports(offer.AesKeyTypes, selection.AesKeyType) &&
        Supports(offer.AesIvTypes, selection.AesIvType);

    private static bool Supports(uint offeredMask, uint? selectedType) =>
        selectedType is { } value && (offeredMask & value) == value;
}
