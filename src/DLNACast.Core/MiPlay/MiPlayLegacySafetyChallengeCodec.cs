using System.Security.Cryptography;
using System.Text;

namespace DLNACast.Core.MiPlay;

public sealed record MiPlayLegacySafetyAcknowledgement(ushort Sequence, string Response);

/// <summary>
/// Reproduces the offline computation for the legacy 0x0028 challenge and its 0x0029 response.
/// It only encodes bytes; it does not open a device connection or establish MiPlay trust.
/// </summary>
public static class MiPlayLegacySafetyChallengeCodec
{
    private const string NativeHmacSeed = "0.0.0.0";

    public static MiPlayLegacySafetyAcknowledgement CreateAcknowledgement(
        ushort sequence,
        ReadOnlySpan<byte> challenge)
    {
        var key = ToLowerHex(MD5.HashData(Encoding.UTF8.GetBytes(NativeHmacSeed)));
        var response = ToLowerHex(HMACSHA1.HashData(Encoding.UTF8.GetBytes(key), challenge));
        return new MiPlayLegacySafetyAcknowledgement(sequence, response);
    }

    public static byte[] EncodeAcknowledgement(MiPlayLegacySafetyAcknowledgement acknowledgement)
    {
        ArgumentNullException.ThrowIfNull(acknowledgement);
        ArgumentException.ThrowIfNullOrEmpty(acknowledgement.Response);

        return MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.LegacySafetyAcknowledgementCommand,
            acknowledgement.Sequence,
            Encoding.ASCII.GetBytes(acknowledgement.Response));
    }

    public static bool TryCreateAcknowledgement(
        ReadOnlySpan<byte> frameData,
        out MiPlayLegacySafetyAcknowledgement? acknowledgement,
        out int bytesConsumed)
    {
        acknowledgement = null;
        bytesConsumed = 0;

        if (!MiPlayCommandFrameCodec.TryDecode(frameData, out var frame, out var frameLength) ||
            frame is null ||
            frame.Command != MiPlayProtocolConstants.LegacySafetyChallengeCommand)
        {
            return false;
        }

        acknowledgement = CreateAcknowledgement(frame.Sequence, frame.Payload);
        bytesConsumed = frameLength;
        return true;
    }

    private static string ToLowerHex(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(bytes).ToLowerInvariant();
}
