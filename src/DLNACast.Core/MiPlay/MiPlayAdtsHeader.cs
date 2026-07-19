namespace DLNACast.Core.MiPlay;

/// <summary>
/// Writes the seven-byte MPEG-2 AAC-LC ADTS header produced by Xiaomi's
/// MicRecorder before each encoded audio access unit.
/// </summary>
public static class MiPlayAdtsHeader
{
    public const int Length = 7;

    public static void Write(Span<byte> destination, int completeFrameLength, int sampleRate = MiPlayProtocolConstants.SampleRate)
    {
        if (destination.Length < Length)
        {
            throw new ArgumentException($"The ADTS destination must be at least {Length} bytes.", nameof(destination));
        }
        if (completeFrameLength is < Length or > 0x1fff)
        {
            throw new ArgumentOutOfRangeException(nameof(completeFrameLength));
        }

        var frequencyIndex = sampleRate switch
        {
            96_000 => 0,
            48_000 => 3,
            44_100 => 4,
            32_000 => 5,
            24_000 => 6,
            22_050 => 7,
            16_000 => 8,
            11_025 => 10,
            8_000 => 11,
            _ => throw new ArgumentOutOfRangeException(nameof(sampleRate), "Unsupported MiPlay AAC sample rate.")
        };

        destination[0] = 0xff;
        destination[1] = 0xf9;
        destination[2] = (byte)(0x40 + (frequencyIndex << 2));
        destination[3] = (byte)(0x80 + (completeFrameLength >> 11));
        destination[4] = (byte)((completeFrameLength & 0x7ff) >> 3);
        destination[5] = (byte)(((completeFrameLength & 7) << 5) + 0x1f);
        destination[6] = 0xfc;
    }

    public static byte[] Prepend(ReadOnlySpan<byte> aacAccessUnit, int sampleRate = MiPlayProtocolConstants.SampleRate)
    {
        var packet = new byte[Length + aacAccessUnit.Length];
        Write(packet, packet.Length, sampleRate);
        aacAccessUnit.CopyTo(packet.AsSpan(Length));
        return packet;
    }
}
