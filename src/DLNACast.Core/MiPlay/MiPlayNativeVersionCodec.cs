using System.Text;

namespace DLNACast.Core.MiPlay;

/// <summary>
/// Version exchange used by the native TCP command-session bootstrap.
/// The source version payload is NUL-terminated in the Xiaomi 18.0.0.3 sample.
/// </summary>
public static class MiPlayNativeVersionCodec
{
    public static byte[] EncodeSourceVersion(ushort sequence) => MiPlayCommandFrameCodec.Encode(
        MiPlayProtocolConstants.NativeSourceVersionCommand,
        sequence,
        Encoding.ASCII.GetBytes(MiPlayProtocolConstants.NativeSourceVersion18_0_0_3Payload));

    public static bool TryDecodeAcknowledgement(
        ReadOnlySpan<byte> frameData,
        out ushort sequence,
        out string? deviceVersion)
    {
        sequence = 0;
        deviceVersion = null;

        if (!MiPlayCommandFrameCodec.TryDecode(frameData, out var frame, out _) ||
            frame is null ||
            frame.Command != MiPlayProtocolConstants.NativeSourceVersionAcknowledgementCommand)
        {
            return false;
        }

        var payload = frame.Payload.AsSpan();
        if (payload.Length > 0 && payload[^1] == 0)
        {
            payload = payload[..^1];
        }

        if (payload.IsEmpty || payload.IndexOf((byte)0) >= 0)
        {
            return false;
        }

        foreach (var value in payload)
        {
            if (value is < 0x20 or > 0x7E)
            {
                return false;
            }
        }

        sequence = frame.Sequence;
        deviceVersion = Encoding.ASCII.GetString(payload);
        return true;
    }
}
