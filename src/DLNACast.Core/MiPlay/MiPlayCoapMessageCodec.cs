namespace DLNACast.Core.MiPlay;

public enum MiPlayCoapMessageType
{
    Read = 0,
    Write = 1,
    Notify = 2,
}

public sealed record MiPlayCoapMessage(
    MiPlayCoapMessageType Type,
    int TargetId,
    byte[] Value,
    int Ip,
    int Port,
    byte[] IdHash)
{
    public int ApplicationId => TargetId >> 16;
    public int AttributeId => TargetId & ushort.MaxValue;

    public static int CreateTargetId(int applicationId, int attributeId)
    {
        if (applicationId is < 0 or > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(applicationId));
        }

        if (attributeId is < 0 or > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(attributeId));
        }

        return (applicationId << 16) | attributeId;
    }
}

public sealed record MiPlayCoapResponse(
    MiPlayCoapMessageType Type,
    int TargetId,
    bool Success,
    bool HasValue,
    byte[] Value);

/// <summary>
/// Minimal protobuf codec for Xiaomi's CoapMessages/CoapResponses mailbox.
/// Network transport is deliberately outside this type.
/// </summary>
public static class MiPlayCoapMessageCodec
{
    public const int DefaultPort = 56_666;
    public const string MailboxPath = "/32";

    private const int VarintWireType = 0;
    private const int LengthDelimitedWireType = 2;

    public static byte[] EncodeMessages(IEnumerable<MiPlayCoapMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        using var output = new MemoryStream();
        foreach (var message in messages)
        {
            ArgumentNullException.ThrowIfNull(message);
            var nested = EncodeMessage(message);
            WriteTag(output, 1, LengthDelimitedWireType);
            WriteLengthDelimited(output, nested);
        }

        return output.ToArray();
    }

    public static IReadOnlyList<MiPlayCoapMessage> DecodeMessages(ReadOnlySpan<byte> payload)
    {
        var messages = new List<MiPlayCoapMessage>();
        var offset = 0;
        while (offset < payload.Length)
        {
            var tag = ReadVarint(payload, ref offset);
            var fieldNumber = ReadFieldNumber(tag);
            var wireType = ReadWireType(tag);
            if (fieldNumber == 1 && wireType == LengthDelimitedWireType)
            {
                messages.Add(DecodeMessage(ReadLengthDelimited(payload, ref offset)));
            }
            else
            {
                SkipField(payload, ref offset, wireType);
            }
        }

        return messages;
    }

    public static byte[] EncodeResponses(IEnumerable<MiPlayCoapResponse> responses)
    {
        ArgumentNullException.ThrowIfNull(responses);
        using var output = new MemoryStream();
        foreach (var response in responses)
        {
            ArgumentNullException.ThrowIfNull(response);
            var nested = EncodeResponse(response);
            WriteTag(output, 1, LengthDelimitedWireType);
            WriteLengthDelimited(output, nested);
        }

        return output.ToArray();
    }

    public static IReadOnlyList<MiPlayCoapResponse> DecodeResponses(ReadOnlySpan<byte> payload)
    {
        var responses = new List<MiPlayCoapResponse>();
        var offset = 0;
        while (offset < payload.Length)
        {
            var tag = ReadVarint(payload, ref offset);
            var fieldNumber = ReadFieldNumber(tag);
            var wireType = ReadWireType(tag);
            if (fieldNumber == 1 && wireType == LengthDelimitedWireType)
            {
                responses.Add(DecodeResponse(ReadLengthDelimited(payload, ref offset)));
            }
            else
            {
                SkipField(payload, ref offset, wireType);
            }
        }

        return responses;
    }

    private static byte[] EncodeMessage(MiPlayCoapMessage message)
    {
        ValidateType(message.Type);
        ValidateNonNegative(message.TargetId, nameof(message.TargetId));
        ValidateNonNegative(message.Ip, nameof(message.Ip));
        ValidateNonNegative(message.Port, nameof(message.Port));

        using var output = new MemoryStream();
        WriteEnum(output, 1, message.Type);
        WriteInt32(output, 2, message.TargetId);
        WriteBytes(output, 3, message.Value);
        WriteInt32(output, 4, message.Ip);
        WriteInt32(output, 5, message.Port);
        WriteBytes(output, 6, message.IdHash);
        return output.ToArray();
    }

    private static MiPlayCoapMessage DecodeMessage(ReadOnlySpan<byte> payload)
    {
        var type = MiPlayCoapMessageType.Read;
        var targetId = 0;
        var value = Array.Empty<byte>();
        var ip = 0;
        var port = 0;
        var idHash = Array.Empty<byte>();
        var offset = 0;

        while (offset < payload.Length)
        {
            var tag = ReadVarint(payload, ref offset);
            var fieldNumber = ReadFieldNumber(tag);
            var wireType = ReadWireType(tag);
            switch (fieldNumber)
            {
                case 1 when wireType == VarintWireType:
                    type = ReadType(payload, ref offset);
                    break;
                case 2 when wireType == VarintWireType:
                    targetId = ReadInt32(payload, ref offset);
                    break;
                case 3 when wireType == LengthDelimitedWireType:
                    value = ReadLengthDelimited(payload, ref offset).ToArray();
                    break;
                case 4 when wireType == VarintWireType:
                    ip = ReadInt32(payload, ref offset);
                    break;
                case 5 when wireType == VarintWireType:
                    port = ReadInt32(payload, ref offset);
                    break;
                case 6 when wireType == LengthDelimitedWireType:
                    idHash = ReadLengthDelimited(payload, ref offset).ToArray();
                    break;
                default:
                    SkipField(payload, ref offset, wireType);
                    break;
            }
        }

        return new MiPlayCoapMessage(type, targetId, value, ip, port, idHash);
    }

    private static byte[] EncodeResponse(MiPlayCoapResponse response)
    {
        ValidateType(response.Type);
        ValidateNonNegative(response.TargetId, nameof(response.TargetId));

        using var output = new MemoryStream();
        WriteEnum(output, 1, response.Type);
        WriteInt32(output, 2, response.TargetId);
        WriteBool(output, 3, response.Success);
        WriteBool(output, 4, response.HasValue);
        WriteBytes(output, 5, response.Value);
        return output.ToArray();
    }

    private static MiPlayCoapResponse DecodeResponse(ReadOnlySpan<byte> payload)
    {
        var type = MiPlayCoapMessageType.Read;
        var targetId = 0;
        var success = false;
        var hasValue = false;
        var value = Array.Empty<byte>();
        var offset = 0;

        while (offset < payload.Length)
        {
            var tag = ReadVarint(payload, ref offset);
            var fieldNumber = ReadFieldNumber(tag);
            var wireType = ReadWireType(tag);
            switch (fieldNumber)
            {
                case 1 when wireType == VarintWireType:
                    type = ReadType(payload, ref offset);
                    break;
                case 2 when wireType == VarintWireType:
                    targetId = ReadInt32(payload, ref offset);
                    break;
                case 3 when wireType == VarintWireType:
                    success = ReadVarint(payload, ref offset) != 0;
                    break;
                case 4 when wireType == VarintWireType:
                    hasValue = ReadVarint(payload, ref offset) != 0;
                    break;
                case 5 when wireType == LengthDelimitedWireType:
                    value = ReadLengthDelimited(payload, ref offset).ToArray();
                    break;
                default:
                    SkipField(payload, ref offset, wireType);
                    break;
            }
        }

        return new MiPlayCoapResponse(type, targetId, success, hasValue, value);
    }

    private static void WriteEnum(Stream output, int fieldNumber, MiPlayCoapMessageType value)
    {
        if (value == MiPlayCoapMessageType.Read)
        {
            return;
        }

        WriteTag(output, fieldNumber, VarintWireType);
        WriteVarint(output, (ulong)value);
    }

    private static void WriteInt32(Stream output, int fieldNumber, int value)
    {
        if (value == 0)
        {
            return;
        }

        WriteTag(output, fieldNumber, VarintWireType);
        WriteVarint(output, checked((uint)value));
    }

    private static void WriteBool(Stream output, int fieldNumber, bool value)
    {
        if (!value)
        {
            return;
        }

        WriteTag(output, fieldNumber, VarintWireType);
        WriteVarint(output, 1);
    }

    private static void WriteBytes(Stream output, int fieldNumber, byte[]? value)
    {
        if (value is null || value.Length == 0)
        {
            return;
        }

        WriteTag(output, fieldNumber, LengthDelimitedWireType);
        WriteLengthDelimited(output, value);
    }

    private static void WriteTag(Stream output, int fieldNumber, int wireType) =>
        WriteVarint(output, checked((ulong)((fieldNumber << 3) | wireType)));

    private static void WriteLengthDelimited(Stream output, ReadOnlySpan<byte> value)
    {
        WriteVarint(output, checked((ulong)value.Length));
        output.Write(value);
    }

    private static void WriteVarint(Stream output, ulong value)
    {
        while (value >= 0x80)
        {
            output.WriteByte((byte)((value & 0x7f) | 0x80));
            value >>= 7;
        }

        output.WriteByte((byte)value);
    }

    private static int ReadInt32(ReadOnlySpan<byte> payload, ref int offset)
    {
        var value = ReadVarint(payload, ref offset);
        if (value > int.MaxValue)
        {
            throw new FormatException("MiPlay CoAP int32 field is out of range.");
        }

        return (int)value;
    }

    private static MiPlayCoapMessageType ReadType(ReadOnlySpan<byte> payload, ref int offset)
    {
        var value = ReadVarint(payload, ref offset);
        if (value > (ulong)MiPlayCoapMessageType.Notify)
        {
            throw new FormatException("Unknown MiPlay CoAP message type.");
        }

        return (MiPlayCoapMessageType)value;
    }

    private static ReadOnlySpan<byte> ReadLengthDelimited(ReadOnlySpan<byte> payload, ref int offset)
    {
        var length = ReadVarint(payload, ref offset);
        if (length > int.MaxValue || (int)length > payload.Length - offset)
        {
            throw new FormatException("Truncated MiPlay CoAP length-delimited field.");
        }

        var result = payload.Slice(offset, (int)length);
        offset += (int)length;
        return result;
    }

    private static ulong ReadVarint(ReadOnlySpan<byte> payload, ref int offset)
    {
        ulong result = 0;
        for (var shift = 0; shift < 64; shift += 7)
        {
            if (offset >= payload.Length)
            {
                throw new FormatException("Truncated MiPlay CoAP varint.");
            }

            var value = payload[offset++];
            result |= (ulong)(value & 0x7f) << shift;
            if ((value & 0x80) == 0)
            {
                return result;
            }
        }

        throw new FormatException("MiPlay CoAP varint is too long.");
    }

    private static int ReadFieldNumber(ulong tag)
    {
        var fieldNumber = tag >> 3;
        if (fieldNumber == 0 || fieldNumber > int.MaxValue)
        {
            throw new FormatException("Invalid MiPlay CoAP protobuf field number.");
        }

        return (int)fieldNumber;
    }

    private static int ReadWireType(ulong tag) => (int)(tag & 0x07);

    private static void SkipField(ReadOnlySpan<byte> payload, ref int offset, int wireType)
    {
        switch (wireType)
        {
            case VarintWireType:
                ReadVarint(payload, ref offset);
                return;
            case 1:
                EnsureAvailable(payload, offset, 8);
                offset += 8;
                return;
            case LengthDelimitedWireType:
                ReadLengthDelimited(payload, ref offset);
                return;
            case 5:
                EnsureAvailable(payload, offset, 4);
                offset += 4;
                return;
            default:
                throw new FormatException($"Unsupported MiPlay CoAP protobuf wire type {wireType}.");
        }
    }

    private static void EnsureAvailable(ReadOnlySpan<byte> payload, int offset, int count)
    {
        if (offset < 0 || count < 0 || offset > payload.Length - count)
        {
            throw new FormatException("Truncated MiPlay CoAP protobuf field.");
        }
    }

    private static void ValidateType(MiPlayCoapMessageType type)
    {
        if (type is < MiPlayCoapMessageType.Read or > MiPlayCoapMessageType.Notify)
        {
            throw new ArgumentOutOfRangeException(nameof(type));
        }
    }

    private static void ValidateNonNegative(int value, string parameterName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}
