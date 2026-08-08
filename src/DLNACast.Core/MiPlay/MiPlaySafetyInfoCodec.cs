using System.Globalization;
using System.Text.Json;

namespace DLNACast.Core.MiPlay;

public sealed record MiPlaySafetyInfoOffer(
    uint AuthKeyTypes,
    uint AuthAlgorithmTypes,
    uint IntegrityTypes,
    uint AesKeyTypes,
    uint AesIvTypes)
{
    /// <summary>
    /// Capability values initialized by CmdSource in Xiaomi Interconnectivity Services 18.0.0.3.
    /// This is version-specific protocol evidence, not a claim about every MiPlay device.
    /// </summary>
    public static MiPlaySafetyInfoOffer Native18_0_0_3 { get; } = new(
        AuthKeyTypes: 1,
        AuthAlgorithmTypes: 7,
        IntegrityTypes: 1,
        AesKeyTypes: 1,
        AesIvTypes: 3);

    public byte[] ToJsonPayload() => MiPlaySafetyInfoCodec.EncodeOffer(this);
}

public sealed record MiPlaySafetyInfoSelection
{
    public MiPlaySafetyInfoSelection(
        uint? authKeyType,
        uint? authAlgorithmType,
        uint? integrityType,
        uint? aesKeyType,
        uint? aesIvType)
    {
        AuthKeyType = authKeyType;
        AuthAlgorithmType = authAlgorithmType;
        IntegrityType = integrityType;
        AesKeyType = aesKeyType;
        AesIvType = aesIvType;
        Validate();
    }

    public uint? AuthKeyType { get; }
    public uint? AuthAlgorithmType { get; }
    public uint? IntegrityType { get; }
    public uint? AesKeyType { get; }
    public uint? AesIvType { get; }

    public byte[] ToJsonPayload() => MiPlaySafetyInfoCodec.EncodeSelection(this);

    private void Validate()
    {
        if (AuthKeyType.HasValue != AuthAlgorithmType.HasValue)
        {
            throw new ArgumentException("authKeyType and authAlgorithmType must be supplied together.");
        }

        if (AuthKeyType is 0 || AuthAlgorithmType is 0 || IntegrityType is 0)
        {
            throw new ArgumentOutOfRangeException(nameof(AuthKeyType), "Selected MiPlay safety types must be non-zero.");
        }

        if (AesKeyType.HasValue != AesIvType.HasValue)
        {
            throw new ArgumentException("aesKeyType and aesIvType must be supplied together.");
        }

        if (AesKeyType is 0 || AesIvType is 0)
        {
            throw new ArgumentOutOfRangeException(nameof(AesKeyType), "Selected MiPlay AES types must be non-zero.");
        }
    }
}

/// <summary>
/// Decoded 0x1401 payload. In the recovered native source, result "0" accepts the
/// selected safety types and advances the safety state machine.
/// </summary>
public sealed record MiPlaySafetyInfoAcknowledgement(string Result, MiPlaySafetyInfoSelection Selection);

/// <summary>
/// JSON payloads for the verified 0x1400 SafetyInfo offer and 0x1401 SafetyInfo acknowledgement.
/// Numeric type fields are serialized as decimal JSON strings, matching the Xiaomi native implementation.
/// </summary>
public static class MiPlaySafetyInfoCodec
{
    public static byte[] EncodeOffer(MiPlaySafetyInfoOffer offer)
    {
        ArgumentNullException.ThrowIfNull(offer);

        return WriteJson(writer =>
        {
            writer.WriteString("authKeyTypes", offer.AuthKeyTypes.ToString(CultureInfo.InvariantCulture));
            writer.WriteString("authAlgorithmTypes", offer.AuthAlgorithmTypes.ToString(CultureInfo.InvariantCulture));
            writer.WriteString("integrityTypes", offer.IntegrityTypes.ToString(CultureInfo.InvariantCulture));
            writer.WriteString("aesKeyTypes", offer.AesKeyTypes.ToString(CultureInfo.InvariantCulture));
            writer.WriteString("aesIvTypes", offer.AesIvTypes.ToString(CultureInfo.InvariantCulture));
        });
    }

    public static byte[] EncodeSelection(MiPlaySafetyInfoSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);

        return WriteJson(writer =>
        {
            writer.WriteString("result", "0");
            WriteOptionalType(writer, "authKeyType", selection.AuthKeyType);
            WriteOptionalType(writer, "authAlgorithmType", selection.AuthAlgorithmType);
            WriteOptionalType(writer, "integrityType", selection.IntegrityType);
            WriteOptionalType(writer, "aesKeyType", selection.AesKeyType);
            WriteOptionalType(writer, "aesIvType", selection.AesIvType);
        });
    }

    public static bool TryDecodeSelection(ReadOnlySpan<byte> payload, out MiPlaySafetyInfoSelection? selection)
    {
        selection = null;

        if (!TryDecodeAcknowledgement(payload, out var acknowledgement) ||
            acknowledgement is null ||
            !string.Equals(acknowledgement.Result, "0", StringComparison.Ordinal))
        {
            return false;
        }

        selection = acknowledgement.Selection;
        return true;
    }

    public static bool TryDecodeOffer(ReadOnlySpan<byte> payload, out MiPlaySafetyInfoOffer? offer)
    {
        offer = null;

        try
        {
            using var document = JsonDocument.Parse(payload.ToArray());
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !TryReadRequiredType(root, "authKeyTypes", out var authKeyTypes) ||
                !TryReadRequiredType(root, "authAlgorithmTypes", out var authAlgorithmTypes) ||
                !TryReadRequiredType(root, "integrityTypes", out var integrityTypes) ||
                !TryReadRequiredType(root, "aesKeyTypes", out var aesKeyTypes) ||
                !TryReadRequiredType(root, "aesIvTypes", out var aesIvTypes))
            {
                return false;
            }

            offer = new MiPlaySafetyInfoOffer(
                authKeyTypes,
                authAlgorithmTypes,
                integrityTypes,
                aesKeyTypes,
                aesIvTypes);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static bool TryDecodeAcknowledgement(
        ReadOnlySpan<byte> payload,
        out MiPlaySafetyInfoAcknowledgement? acknowledgement)
    {
        acknowledgement = null;

        try
        {
            using var document = JsonDocument.Parse(payload.ToArray());
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !TryGetString(root, "result", out var result) ||
                string.IsNullOrEmpty(result))
            {
                return false;
            }

            if (!TryReadOptionalType(root, "authKeyType", out var authKeyType) ||
                !TryReadOptionalType(root, "authAlgorithmType", out var authAlgorithmType) ||
                !TryReadOptionalType(root, "integrityType", out var integrityType) ||
                !TryReadOptionalType(root, "aesKeyType", out var aesKeyType) ||
                !TryReadOptionalType(root, "aesIvType", out var aesIvType))
            {
                return false;
            }

            acknowledgement = new MiPlaySafetyInfoAcknowledgement(
                result,
                new MiPlaySafetyInfoSelection(
                    authKeyType,
                    authAlgorithmType,
                    integrityType,
                    aesKeyType,
                    aesIvType));
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static byte[] WriteJson(Action<Utf8JsonWriter> writeFields)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writeFields(writer);
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private static void WriteOptionalType(Utf8JsonWriter writer, string name, uint? value)
    {
        if (value.HasValue)
        {
            writer.WriteString(name, value.Value.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static bool TryReadOptionalType(JsonElement root, string name, out uint? value)
    {
        value = null;
        if (!root.TryGetProperty(name, out var property))
        {
            return true;
        }

        if (property.ValueKind != JsonValueKind.String ||
            !uint.TryParse(
                property.GetString(),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var parsed))
        {
            return false;
        }

        value = parsed;
        return true;
    }

    private static bool TryReadRequiredType(JsonElement root, string name, out uint value)
    {
        value = 0;
        return root.TryGetProperty(name, out var property) &&
               property.ValueKind == JsonValueKind.String &&
               uint.TryParse(
                   property.GetString(),
                   NumberStyles.None,
                   CultureInfo.InvariantCulture,
                   out value) &&
               value != 0;
    }

    private static bool TryGetString(JsonElement root, string name, out string? value)
    {
        value = null;
        return root.TryGetProperty(name, out var property) &&
               property.ValueKind == JsonValueKind.String &&
               (value = property.GetString()) is not null;
    }
}
