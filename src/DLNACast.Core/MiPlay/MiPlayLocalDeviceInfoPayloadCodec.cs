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
    public static byte[] EncodeSourceName(
        string sourceName,
        string? bluetoothMac,
        string canAlonePlayCtrl = "0",
        bool includeControlFields = true)
    {
        if (string.IsNullOrEmpty(sourceName))
        {
            throw new ArgumentException("Source name must not be null or empty.", nameof(sourceName));
        }

        return WriteJson(writer =>
        {
            writer.WriteString("sourceName", sourceName);
            writer.WriteString("mSourceBtMac", EncodeBluetoothMacHash(bluetoothMac));

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
