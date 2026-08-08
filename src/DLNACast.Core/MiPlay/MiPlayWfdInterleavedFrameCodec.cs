namespace DLNACast.Core.MiPlay;

/// <summary>
/// Codec for the separate TCP media framing used by the captured legacy
/// MiPlay/WFD audio source. Each RTP packet is prefixed by '$' and a 24-bit
/// big-endian payload length. This is not the four-byte RTSP channel/16-bit
/// interleaved header and must not be decoded as a TCP 8899 command frame.
/// </summary>
public static class MiPlayWfdInterleavedFrameCodec
{
    public const int HeaderLength = 4;
    public const int MaximumPayloadLength = 0x00ff_ffff;

    public static byte[] Encode(ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty || payload.Length > MaximumPayloadLength)
        {
            throw new ArgumentOutOfRangeException(nameof(payload));
        }

        var frame = new byte[HeaderLength + payload.Length];
        frame[0] = MiPlayProtocolConstants.CommandFrameMagic;
        frame[1] = (byte)(payload.Length >> 16);
        frame[2] = (byte)(payload.Length >> 8);
        frame[3] = (byte)payload.Length;
        payload.CopyTo(frame.AsSpan(HeaderLength));
        return frame;
    }

    public static bool TryDecode(
        ReadOnlySpan<byte> data,
        out byte[] payload,
        out int bytesConsumed)
    {
        payload = [];
        bytesConsumed = 0;
        if (data.Length < HeaderLength || data[0] != MiPlayProtocolConstants.CommandFrameMagic)
        {
            return false;
        }

        var payloadLength = (data[1] << 16) | (data[2] << 8) | data[3];
        if (payloadLength == 0 || data.Length - HeaderLength < payloadLength)
        {
            return false;
        }

        payload = data.Slice(HeaderLength, payloadLength).ToArray();
        bytesConsumed = HeaderLength + payloadLength;
        return true;
    }
}
