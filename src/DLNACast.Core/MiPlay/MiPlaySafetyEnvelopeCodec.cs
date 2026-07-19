using System.Buffers.Binary;

namespace DLNACast.Core.MiPlay;

public sealed record MiPlaySafetyEnvelope(bool IsAcknowledgement, byte ValueType, byte[] Payload);

/// <summary>
/// Encodes the OPack subset used by the verified MiPlay safety commands:
/// tag-length(u8) + ASCII "cmd"/"ack" + value-type(u8) + payload-length(u32 BE) + payload.
/// </summary>
public static class MiPlaySafetyEnvelopeCodec
{
    private static ReadOnlySpan<byte> CommandTag => "cmd"u8;
    private static ReadOnlySpan<byte> AcknowledgementTag => "ack"u8;

    public static byte[] Encode(bool isAcknowledgement, byte valueType, ReadOnlySpan<byte> payload)
    {
        if (valueType != MiPlayProtocolConstants.SafetyValueType)
        {
            throw new ArgumentOutOfRangeException(
                nameof(valueType),
                valueType,
                $"The verified SafetyAuth OPack value type is {MiPlayProtocolConstants.SafetyValueType}.");
        }

        if (payload.Length > MiPlayCommandFrameCodec.MaximumPayloadLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(payload),
                $"MiPlay safety payload exceeds {MiPlayCommandFrameCodec.MaximumPayloadLength} bytes.");
        }

        var tag = isAcknowledgement ? AcknowledgementTag : CommandTag;
        var encoded = new byte[1 + tag.Length + 1 + sizeof(uint) + payload.Length];
        encoded[0] = checked((byte)tag.Length);
        tag.CopyTo(encoded.AsSpan(1));
        encoded[1 + tag.Length] = valueType;
        BinaryPrimitives.WriteUInt32BigEndian(encoded.AsSpan(2 + tag.Length, sizeof(uint)), (uint)payload.Length);
        payload.CopyTo(encoded.AsSpan(2 + tag.Length + sizeof(uint)));
        return encoded;
    }

    public static bool TryDecode(
        ReadOnlySpan<byte> data,
        out MiPlaySafetyEnvelope? envelope,
        out int bytesConsumed)
    {
        envelope = null;
        bytesConsumed = 0;

        if (data.IsEmpty)
        {
            return false;
        }

        var tagLength = data[0];
        var headerLength = 1 + tagLength + 1 + sizeof(uint);
        if (data.Length < headerLength)
        {
            return false;
        }

        var tag = data.Slice(1, tagLength);
        var isAcknowledgement = tag.SequenceEqual(AcknowledgementTag);
        if (!isAcknowledgement && !tag.SequenceEqual(CommandTag))
        {
            return false;
        }

        var valueTypeOffset = 1 + tagLength;
        var valueType = data[valueTypeOffset];
        if (valueType != MiPlayProtocolConstants.SafetyValueType)
        {
            return false;
        }

        var payloadLength = BinaryPrimitives.ReadUInt32BigEndian(
            data.Slice(valueTypeOffset + 1, sizeof(uint)));
        if (payloadLength > MiPlayCommandFrameCodec.MaximumPayloadLength)
        {
            return false;
        }

        var envelopeLength = headerLength + (int)payloadLength;
        if (data.Length < envelopeLength)
        {
            return false;
        }

        envelope = new MiPlaySafetyEnvelope(
            isAcknowledgement,
            valueType,
            data.Slice(headerLength, (int)payloadLength).ToArray());
        bytesConsumed = envelopeLength;
        return true;
    }
}

