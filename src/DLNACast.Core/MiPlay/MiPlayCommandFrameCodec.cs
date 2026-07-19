using System.Buffers.Binary;

namespace DLNACast.Core.MiPlay;

public sealed record MiPlayCommandFrame(ushort Command, ushort Sequence, byte[] Payload);

/// <summary>
/// Encodes and decodes the legacy MiPlay command-channel frame:
/// '$' + command(u16 BE) + sequence(u16 BE) + payload length(u32 BE) + payload.
/// </summary>
public static class MiPlayCommandFrameCodec
{
    public const int MaximumPayloadLength = 4 * 1024 * 1024;

    public static byte[] Encode(ushort command, ushort sequence, ReadOnlySpan<byte> payload)
    {
        if (payload.Length > MaximumPayloadLength)
        {
            throw new ArgumentOutOfRangeException(nameof(payload), $"MiPlay command payload exceeds {MaximumPayloadLength} bytes.");
        }

        var frame = new byte[MiPlayProtocolConstants.CommandHeaderLength + payload.Length];
        frame[0] = MiPlayProtocolConstants.CommandFrameMagic;
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(1, 2), command);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(3, 2), sequence);
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(5, 4), (uint)payload.Length);
        payload.CopyTo(frame.AsSpan(MiPlayProtocolConstants.CommandHeaderLength));
        return frame;
    }

    public static bool TryDecode(
        ReadOnlySpan<byte> data,
        out MiPlayCommandFrame? frame,
        out int bytesConsumed)
    {
        frame = null;
        bytesConsumed = 0;

        if (data.Length < MiPlayProtocolConstants.CommandHeaderLength ||
            data[0] != MiPlayProtocolConstants.CommandFrameMagic)
        {
            return false;
        }

        var payloadLength = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(5, 4));
        if (payloadLength > MaximumPayloadLength)
        {
            return false;
        }

        var frameLength = MiPlayProtocolConstants.CommandHeaderLength + (int)payloadLength;
        if (data.Length < frameLength)
        {
            return false;
        }

        frame = new MiPlayCommandFrame(
            BinaryPrimitives.ReadUInt16BigEndian(data.Slice(1, 2)),
            BinaryPrimitives.ReadUInt16BigEndian(data.Slice(3, 2)),
            data.Slice(MiPlayProtocolConstants.CommandHeaderLength, (int)payloadLength).ToArray());
        bytesConsumed = frameLength;
        return true;
    }
}
