using System.Buffers.Binary;
using System.Text;

namespace DLNACast.Core.MiPlay;

/// <summary>
/// Narrow decoder for the OPack-like notify payloads observed on command 0x0022.
/// It is diagnostic only and does not imply that a notify frame should be
/// acknowledged by the source.
/// </summary>
public static class MiPlayNotifyPayloadCodec
{
    public const byte ByteValueType = 0x03;
    public const byte UnsignedInt32ValueType = 0x06;
    public const byte StringValueType = 0x14;
    public const byte ObjectValueType = 0x16;

    public static bool TryDecode(
        ReadOnlySpan<byte> payload,
        out MiPlayNotifyPayload? notify,
        out int bytesConsumed)
    {
        notify = null;
        bytesConsumed = 0;

        if (!TryReadLabel(payload, out var label, out var offset) ||
            payload.Length <= offset)
        {
            return false;
        }

        var valueType = payload[offset++];
        if (valueType == ByteValueType)
        {
            if (payload.Length <= offset)
            {
                return false;
            }

            var integerValue = payload[offset++];
            var scalarFields = new List<MiPlayNotifyField>();
            while (offset < payload.Length)
            {
                if (!TryReadField(payload[offset..], out var field, out var fieldBytesConsumed) ||
                    fieldBytesConsumed <= 0)
                {
                    return false;
                }
                scalarFields.Add(field);
                offset += fieldBytesConsumed;
            }

            notify = new MiPlayNotifyPayload(
                label,
                valueType,
                IntegerValue: integerValue,
                DeclaredPayloadLength: null,
                Fields: scalarFields);
            bytesConsumed = offset;
            return true;
        }

        if (valueType != ObjectValueType || payload.Length < offset + sizeof(uint))
        {
            return false;
        }

        var payloadLengthValue = BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(offset, sizeof(uint)));
        if (payloadLengthValue > int.MaxValue)
        {
            return false;
        }

        var payloadLength = (int)payloadLengthValue;
        offset += sizeof(uint);
        if (payload.Length < offset + payloadLength)
        {
            return false;
        }

        var fields = new List<MiPlayNotifyField>();
        var fieldsPayload = payload.Slice(offset, payloadLength);
        var fieldOffset = 0;
        while (fieldOffset < fieldsPayload.Length)
        {
            if (!TryReadField(fieldsPayload[fieldOffset..], out var field, out var fieldBytesConsumed) ||
                fieldBytesConsumed <= 0)
            {
                return false;
            }

            fields.Add(field);
            fieldOffset += fieldBytesConsumed;
        }

        notify = new MiPlayNotifyPayload(
            label,
            valueType,
            IntegerValue: null,
            DeclaredPayloadLength: payloadLength,
            Fields: fields);
        bytesConsumed = offset + payloadLength;
        return true;
    }

    private static bool TryReadField(
        ReadOnlySpan<byte> payload,
        out MiPlayNotifyField field,
        out int bytesConsumed)
    {
        field = default;
        bytesConsumed = 0;

        if (!TryReadLabel(payload, out var name, out var offset) ||
            payload.Length <= offset)
        {
            return false;
        }

        var valueType = payload[offset++];
        if (valueType == ByteValueType)
        {
            if (payload.Length <= offset)
            {
                return false;
            }

            field = new MiPlayNotifyField(
                name,
                valueType,
                StringValue: null,
                IntegerValue: payload[offset],
                DeclaredPayloadLength: null);
            bytesConsumed = offset + 1;
            return true;
        }

        if (valueType == UnsignedInt32ValueType)
        {
            if (payload.Length < offset + sizeof(uint))
            {
                return false;
            }
            var uint32Value = BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(offset, sizeof(uint)));
            if (uint32Value > int.MaxValue)
            {
                return false;
            }
            field = new MiPlayNotifyField(
                name,
                valueType,
                StringValue: null,
                IntegerValue: (int)uint32Value,
                DeclaredPayloadLength: sizeof(uint));
            bytesConsumed = offset + sizeof(uint);
            return true;
        }

        if (valueType != StringValueType || payload.Length < offset + sizeof(uint))
        {
            return false;
        }

        var valueLengthValue = BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(offset, sizeof(uint)));
        if (valueLengthValue > int.MaxValue)
        {
            return false;
        }

        var valueLength = (int)valueLengthValue;
        offset += sizeof(uint);
        if (payload.Length < offset + valueLength)
        {
            return false;
        }

        var value = Encoding.UTF8.GetString(payload.Slice(offset, valueLength));
        field = new MiPlayNotifyField(
            name,
            valueType,
            value,
            IntegerValue: null,
            DeclaredPayloadLength: valueLength);
        bytesConsumed = offset + valueLength;
        return true;
    }

    private static bool TryReadLabel(
        ReadOnlySpan<byte> payload,
        out string label,
        out int bytesConsumed)
    {
        label = string.Empty;
        bytesConsumed = 0;
        if (payload.IsEmpty)
        {
            return false;
        }

        var labelLength = payload[0];
        if (labelLength == 0 || payload.Length < 1 + labelLength)
        {
            return false;
        }

        label = Encoding.ASCII.GetString(payload.Slice(1, labelLength));
        bytesConsumed = 1 + labelLength;
        return true;
    }
}

public sealed record MiPlayNotifyPayload(
    string Label,
    byte ValueType,
    int? IntegerValue,
    int? DeclaredPayloadLength,
    IReadOnlyList<MiPlayNotifyField> Fields);

public readonly record struct MiPlayNotifyField(
    string Name,
    byte ValueType,
    string? StringValue,
    int? IntegerValue,
    int? DeclaredPayloadLength);
