using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace DLNACast.Core.MiPlay;

/// <summary>
/// Offline JSON payload codec for the native 0x0058 setLocalDeviceInfo command.
/// This class only mirrors the Xiaomi 18.0.0.3 payload builders; it does not
/// transmit the command or infer when a device will accept it.
/// </summary>
public static class MiPlayLocalDeviceInfoPayloadCodec
{
    /// <summary>
    /// Exact legacy-clear source identity shape captured from com.milink.service
    /// 12.4.8.13. Forward slashes are escaped to reproduce its byte transcript.
    /// </summary>
    public static byte[] EncodeLegacySourceNameOnly(string sourceName)
    {
        if (string.IsNullOrEmpty(sourceName))
        {
            throw new ArgumentException("Source name must not be null or empty.", nameof(sourceName));
        }

        var encodedName = JsonEncodedText
            .Encode(sourceName, JavaScriptEncoder.UnsafeRelaxedJsonEscaping)
            .ToString()
            .Replace("/", "\\/", StringComparison.Ordinal);
        return Encoding.UTF8.GetBytes($"{{\"sourceName\":\"{encodedName}\"}}");
    }

    public static byte[] EncodeSourceName(
        string sourceName,
        string? bluetoothMac,
        string canAlonePlayCtrl = "0",
        bool includeControlFields = true)
    {
        return EncodeSourceNameCore(
            sourceName,
            EncodeBluetoothMacHash(bluetoothMac),
            canAlonePlayCtrl,
            includeControlFields);
    }

    public static byte[] EncodeSourceNameWithBluetoothMacHash(
        string sourceName,
        string? bluetoothMacHash,
        string canAlonePlayCtrl = "0",
        bool includeControlFields = true)
    {
        return EncodeSourceNameCore(
            sourceName,
            NormalizeBluetoothMacHash(bluetoothMacHash),
            canAlonePlayCtrl,
            includeControlFields);
    }

    public static byte[] EncodeRecoveredOfficialSourceIdentity()
    {
        return EncodeSourceNameWithBluetoothMacHash(
            MiPlayRealPhonePostAuthPlaintextEvidence.RecoveredOfficialSourceName,
            MiPlayRealPhonePostAuthPlaintextEvidence.RecoveredOfficialSourceBluetoothMacHash,
            includeControlFields: false);
    }

    private static byte[] EncodeSourceNameCore(
        string sourceName,
        string encodedBluetoothMacHash,
        string canAlonePlayCtrl,
        bool includeControlFields)
    {
        if (string.IsNullOrEmpty(sourceName))
        {
            throw new ArgumentException("Source name must not be null or empty.", nameof(sourceName));
        }

        return WriteJson(writer =>
        {
            writer.WriteString("sourceName", sourceName);
            writer.WriteString("mSourceBtMac", encodedBluetoothMacHash);

            if (includeControlFields)
            {
                writer.WriteString("canAlonePlayCtrl", canAlonePlayCtrl);
                writer.WriteString("canHeadsetCtrl", "1");
            }
        });
    }

    public static byte[] EncodeLocalDeviceInfo(
        string? model,
        string? romVersion,
        int appVersion)
    {
        return WriteJson(writer =>
        {
            WriteOptionalString(writer, "model", model);
            WriteOptionalString(writer, "romVersion", romVersion);
            writer.WriteNumber("appVersion", appVersion);
        });
    }

    public static byte[] EncodeCanAlonePlayCtrl(string value = "1")
    {
        ArgumentNullException.ThrowIfNull(value);

        return WriteJson(writer => writer.WriteString("canAlonePlayCtrl", value));
    }

    public static byte[] EncodeAlonePlayCapacity(string value = "1")
    {
        ArgumentNullException.ThrowIfNull(value);

        return WriteJson(writer => writer.WriteString("alonePlayCapacity", value));
    }

    public static byte[] EncodeIsSameAccount(int value) =>
        WriteJson(writer => writer.WriteNumber("isSameAccount", value));

    public static bool TryDecodeIsSameAccount(ReadOnlySpan<byte> payload, out int value)
    {
        value = 0;
        try
        {
            using var document = JsonDocument.Parse(payload.ToArray());
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                root.EnumerateObject().Count() != 1 ||
                !root.TryGetProperty("isSameAccount", out var property) ||
                property.ValueKind != JsonValueKind.Number ||
                !property.TryGetInt32(out value))
            {
                value = 0;
                return false;
            }

            return true;
        }
        catch (JsonException)
        {
            value = 0;
            return false;
        }
    }

    public static string EncodeBluetoothMacHash(string? bluetoothMac)
    {
        if (string.IsNullOrEmpty(bluetoothMac))
        {
            return string.Empty;
        }

        var normalized = bluetoothMac.Replace(":", string.Empty, StringComparison.Ordinal);
        using var md5 = MD5.Create();
        var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(normalized));

        var builder = new StringBuilder(hash.Length * 2);
        foreach (var value in hash)
        {
            builder.Append(value.ToString("X2"));
        }

        return builder.ToString();
    }

    public static string NormalizeBluetoothMacHash(string? bluetoothMacHash)
    {
        if (string.IsNullOrEmpty(bluetoothMacHash))
        {
            return string.Empty;
        }

        if (bluetoothMacHash.Length != 32 || bluetoothMacHash.Any(value => !Uri.IsHexDigit(value)))
        {
            throw new ArgumentException("The precomputed Bluetooth MAC hash must be a 32-character hexadecimal MD5 string.", nameof(bluetoothMacHash));
        }

        return bluetoothMacHash.ToUpperInvariant();
    }

    private static byte[] WriteJson(Action<Utf8JsonWriter> writeFields)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }))
        {
            writer.WriteStartObject();
            writeFields(writer);
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private static void WriteOptionalString(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is not null)
        {
            writer.WriteString(name, value);
        }
    }
}
