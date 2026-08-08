using System.Text;

namespace DLNACast.Core.MiPlay;

/// <summary>
/// Decoder for the legacy clear-text Cmd_GetDeviceInfo acknowledgement payload
/// observed on LX06/S12 command 0x001f. This payload is an OPack-like string map
/// with a 24-bit body length followed by repeated key/string-value pairs:
/// keyLen(1), ASCII key, valueType(0x0c), valueLen(2 big-endian), UTF-8 value.
/// </summary>
public static class MiPlayLegacyDeviceInfoPayloadCodec
{
    public const int HeaderLength = 3;
    public const byte StringValueType = 0x0c;
    public const int MaximumBodyLength = 0x00ff_ffff;

    public static byte[] Encode(IEnumerable<KeyValuePair<string, string>> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);

        using var body = new MemoryStream();
        var names = new HashSet<string>(StringComparer.Ordinal);
        var fieldCount = 0;
        foreach (var field in fields)
        {
            var name = field.Key ?? throw new ArgumentException("Device-info field names cannot be null.", nameof(fields));
            var value = field.Value ?? throw new ArgumentException($"Device-info field '{name}' has a null value.", nameof(fields));
            var nameBytes = Encoding.ASCII.GetBytes(name);
            if (nameBytes.Length is 0 or > byte.MaxValue ||
                name.Any(character => character is < '!' or > '~'))
            {
                throw new ArgumentException(
                    $"Device-info field name '{name}' must contain 1..255 printable ASCII bytes.",
                    nameof(fields));
            }

            if (!names.Add(name))
            {
                throw new ArgumentException($"Duplicate device-info field '{name}'.", nameof(fields));
            }

            var valueBytes = Encoding.UTF8.GetBytes(value);
            if (valueBytes.Length > ushort.MaxValue)
            {
                throw new ArgumentException(
                    $"Device-info field '{name}' exceeds the 16-bit UTF-8 value length.",
                    nameof(fields));
            }

            body.WriteByte(checked((byte)nameBytes.Length));
            body.Write(nameBytes);
            body.WriteByte(StringValueType);
            body.WriteByte((byte)(valueBytes.Length >> 8));
            body.WriteByte((byte)valueBytes.Length);
            body.Write(valueBytes);
            fieldCount++;
        }

        if (fieldCount == 0)
        {
            throw new ArgumentException("At least one device-info field is required.", nameof(fields));
        }

        if (body.Length > MaximumBodyLength)
        {
            throw new ArgumentException("The encoded device-info body exceeds the 24-bit length field.", nameof(fields));
        }

        var bodyBytes = body.ToArray();
        return
        [
            (byte)(bodyBytes.Length >> 16),
            (byte)(bodyBytes.Length >> 8),
            (byte)bodyBytes.Length,
            .. bodyBytes,
        ];
    }

    public static bool TryDecode(
        ReadOnlySpan<byte> payload,
        out MiPlayLegacyDeviceInfoPayload? deviceInfo,
        out int bytesConsumed)
    {
        deviceInfo = null;
        bytesConsumed = 0;

        if (payload.Length < HeaderLength)
        {
            return false;
        }

        var declaredBodyLength = (payload[0] << 16) | (payload[1] << 8) | payload[2];
        if (declaredBodyLength < 0 || payload.Length < HeaderLength + declaredBodyLength)
        {
            return false;
        }

        var fields = new List<MiPlayLegacyDeviceInfoField>();
        var body = payload.Slice(HeaderLength, declaredBodyLength);
        var offset = 0;
        while (offset < body.Length)
        {
            if (!TryReadField(body[offset..], out var field, out var fieldBytesConsumed) ||
                fieldBytesConsumed <= 0)
            {
                return false;
            }

            fields.Add(field);
            offset += fieldBytesConsumed;
        }

        deviceInfo = new MiPlayLegacyDeviceInfoPayload(declaredBodyLength, fields);
        bytesConsumed = HeaderLength + declaredBodyLength;
        return true;
    }

    public static string DescribeRedacted(MiPlayLegacyDeviceInfoPayload deviceInfo)
    {
        ArgumentNullException.ThrowIfNull(deviceInfo);

        return string.Join(
            ", ",
            deviceInfo.Fields.Select(field => IsSensitiveField(field.Name)
                ? $"{field.Name}=<redacted:{field.Value.Length}>"
                : $"{field.Name}={field.Value}"));
    }

    public static bool IsSensitiveField(string fieldName) =>
        fieldName is "accountId" or "bluetoothMac" or "deviceId" or "house_Id" or
            "miotDid" or "roomName" or "room_Id" or "sn";

    private static bool TryReadField(
        ReadOnlySpan<byte> payload,
        out MiPlayLegacyDeviceInfoField field,
        out int bytesConsumed)
    {
        field = default;
        bytesConsumed = 0;

        if (payload.IsEmpty)
        {
            return false;
        }

        var keyLength = payload[0];
        var offset = 1;
        if (keyLength == 0 || payload.Length < offset + keyLength + 1 + sizeof(ushort))
        {
            return false;
        }

        var name = Encoding.ASCII.GetString(payload.Slice(offset, keyLength));
        offset += keyLength;

        var valueType = payload[offset++];
        if (valueType != StringValueType)
        {
            return false;
        }

        var valueLength = (payload[offset] << 8) | payload[offset + 1];
        offset += sizeof(ushort);
        if (payload.Length < offset + valueLength)
        {
            return false;
        }

        var value = Encoding.UTF8.GetString(payload.Slice(offset, valueLength));
        offset += valueLength;

        field = new MiPlayLegacyDeviceInfoField(name, valueType, value, valueLength);
        bytesConsumed = offset;
        return true;
    }
}

public sealed record MiPlayLegacyDeviceInfoPayload(
    int DeclaredBodyLength,
    IReadOnlyList<MiPlayLegacyDeviceInfoField> Fields)
{
    public string? GetValue(string name) =>
        Fields.FirstOrDefault(field => string.Equals(field.Name, name, StringComparison.Ordinal)).Value;
}

public readonly record struct MiPlayLegacyDeviceInfoField(
    string Name,
    byte ValueType,
    string Value,
    int DeclaredValueLength);
