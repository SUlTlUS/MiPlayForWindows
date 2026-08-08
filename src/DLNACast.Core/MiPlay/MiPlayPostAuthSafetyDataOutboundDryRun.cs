using System.Security.Cryptography;

namespace DLNACast.Core.MiPlay;

public sealed record MiPlayPostAuthSafetyDataOutboundDryRunFrame(
    string ProfileLabel,
    string ExpectedVectorLabel,
    ushort Command,
    ushort Sequence,
    int PlaintextPayloadLength,
    int SafetyDataPayloadLength,
    int CommandFrameLength,
    string CommandFrameSha256,
    bool SafeForNetworkUse);

public sealed record MiPlayPostAuthSafetyDataOutboundDryRunComparison(
    MiPlayPostAuthSafetyDataOutboundDryRunFrame NativeNoReset,
    MiPlayPostAuthSafetyDataOutboundDryRunFrame ObservedInboundPromotedNegativeControl,
    bool FramesDiffer,
    string Boundary);

/// <summary>
/// Offline/dry-run helpers for comparing post-auth outbound SafetyData command
/// bytes without exposing keys or sending frames.
/// </summary>
public static class MiPlayPostAuthSafetyDataOutboundDryRun
{
    public static MiPlayPostAuthSafetyDataOutboundDryRunComparison CompareOfficialSetPlaySourceProfiles(
        string authKey,
        ReadOnlySpan<byte> localSafetyAuthPlaintext,
        ReadOnlySpan<byte> safetyAuthAcknowledgementPlaintext,
        ushort sequence)
    {
        ArgumentException.ThrowIfNullOrEmpty(authKey);

        if (localSafetyAuthPlaintext.IsEmpty)
        {
            throw new ArgumentException("The local 0x1402 SafetyAuth plaintext is required for post-auth outbound dry-run state.", nameof(localSafetyAuthPlaintext));
        }

        if (safetyAuthAcknowledgementPlaintext.IsEmpty)
        {
            throw new ArgumentException("The local 0x1403 SafetyAuth acknowledgement plaintext is required for post-auth outbound dry-run state.", nameof(safetyAuthAcknowledgementPlaintext));
        }

        var outboundSafetyAuthPlaintexts = new[]
        {
            localSafetyAuthPlaintext.ToArray(),
            safetyAuthAcknowledgementPlaintext.ToArray(),
        };
        var nativeFrame = BuildOfficialSetPlaySourceFrame(
            authKey,
            MiPlayPostAuthSafetyDataCipherProfile.CreateNativeNoResetOutboundProfile(),
            outboundSafetyAuthPlaintexts,
            sequence);
        var negativeControlFrame = BuildOfficialSetPlaySourceFrame(
            authKey,
            MiPlayPostAuthSafetyDataCipherProfile.CreateObservedInboundPromotedOutboundProfile(),
            outboundSafetyAuthPlaintexts,
            sequence);

        return new MiPlayPostAuthSafetyDataOutboundDryRunComparison(
            NativeNoReset: nativeFrame,
            ObservedInboundPromotedNegativeControl: negativeControlFrame,
            FramesDiffer: !string.Equals(nativeFrame.CommandFrameSha256, negativeControlFrame.CommandFrameSha256, StringComparison.Ordinal),
            Boundary: "Dry-run only: compares the native no-reset post-auth outbound profile with the old observed-inbound-promoted negative-control profile. It does not prove inbound response decrypt state and does not authorize or send any post-auth business frame.");
    }

    private static MiPlayPostAuthSafetyDataOutboundDryRunFrame BuildOfficialSetPlaySourceFrame(
        string authKey,
        MiPlayPostAuthSafetyDataOutboundCipherProfile profile,
        IReadOnlyList<byte[]> outboundSafetyAuthPlaintexts,
        ushort sequence)
    {
        var cipher = MiPlayPostAuthSafetyDataCipherProfile.CreateOutboundCommandCipher(
            authKey,
            profile,
            outboundSafetyAuthPlaintexts);
        var plaintext = MiPlaySetPlaySourceOneFrameProbe.BuildMinimalOfficialPayload();
        var commandFrame = MiPlaySetPlaySourceOneFrameProbe.ToSafetyDataCommandFrame(sequence, cipher);

        if (!MiPlayCommandFrameCodec.TryDecode(commandFrame, out var decodedFrame, out var bytesConsumed) ||
            decodedFrame is null ||
            bytesConsumed != commandFrame.Length)
        {
            throw new InvalidOperationException("The generated MiPlay post-auth dry-run command frame did not round-trip through the command decoder.");
        }

        return new MiPlayPostAuthSafetyDataOutboundDryRunFrame(
            ProfileLabel: profile.Label,
            ExpectedVectorLabel: profile.ExpectedSendOnlyVectorLabel,
            Command: decodedFrame.Command,
            Sequence: decodedFrame.Sequence,
            PlaintextPayloadLength: plaintext.Length,
            SafetyDataPayloadLength: decodedFrame.Payload.Length,
            CommandFrameLength: commandFrame.Length,
            CommandFrameSha256: Sha256Hex(commandFrame),
            SafeForNetworkUse: false);
    }

    private static string Sha256Hex(ReadOnlySpan<byte> data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
}