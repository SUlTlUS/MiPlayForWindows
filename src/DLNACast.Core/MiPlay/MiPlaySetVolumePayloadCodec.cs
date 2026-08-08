using System.Buffers.Binary;

namespace DLNACast.Core.MiPlay;

/// <summary>
/// Raw SetVolume/SetVolume_Ack payload used by the legacy 8899 command path.
/// Official CmdSource code byte-swaps the integer before sending four bytes;
/// the LX06 receiver rejects lengths other than four.
/// </summary>
public static class MiPlaySetVolumePayloadCodec
{
    public const int PayloadLength = 4;
    public const uint MaximumVolume = 100;

    public static byte[] Encode(uint volume)
    {
        if (volume > MaximumVolume)
        {
            throw new ArgumentOutOfRangeException(nameof(volume));
        }

        var payload = new byte[PayloadLength];
        BinaryPrimitives.WriteUInt32BigEndian(payload, volume);
        return payload;
    }

    public static bool TryDecode(ReadOnlySpan<byte> payload, out uint volume)
    {
        volume = 0;
        if (payload.Length != PayloadLength)
        {
            return false;
        }

        volume = BinaryPrimitives.ReadUInt32BigEndian(payload);
        return volume <= MaximumVolume;
    }
}
