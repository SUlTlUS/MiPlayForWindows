using System.Buffers.Binary;

namespace DLNACast.Core.MiPlay;

/// <summary>
/// Five-byte scalar status shape observed in legacy-clear 0x000f, 0x001d,
/// and 0x0035 responses: zero tag followed by an unsigned 32-bit big-endian value.
/// </summary>
public static class MiPlayLegacyStatusScalarCodec
{
    public const int PayloadLength = 5;
    public const byte Unsigned32Tag = 0;

    public static byte[] Encode(uint value)
    {
        var payload = new byte[PayloadLength];
        payload[0] = Unsigned32Tag;
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(1), value);
        return payload;
    }

    public static bool TryDecode(ReadOnlySpan<byte> payload, out uint value)
    {
        value = 0;
        if (payload.Length != PayloadLength || payload[0] != Unsigned32Tag)
        {
            return false;
        }

        value = BinaryPrimitives.ReadUInt32BigEndian(payload[1..]);
        return true;
    }
}
