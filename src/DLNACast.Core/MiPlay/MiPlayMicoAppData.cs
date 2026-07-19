using System.Text.Json;

namespace DLNACast.Core.MiPlay;

/// <summary>
/// Extracts the JSON suffix embedded in the Base64 mDNS appsData field used by
/// Xiaomi speakers. The binary prefix is preserved but its fields are not yet named.
/// </summary>
public sealed record MiPlayMicoAppData(byte[] BinaryPrefix, string DeviceId)
{
    public static bool TryParse(string? base64, out MiPlayMicoAppData? appData)
    {
        appData = null;
        if (string.IsNullOrWhiteSpace(base64))
        {
            return false;
        }

        try
        {
            return TryParse(Convert.FromBase64String(base64), out appData);
        }
        catch (FormatException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static bool TryParse(ReadOnlySpan<byte> bytes, out MiPlayMicoAppData? appData)
    {
        appData = null;
        var jsonOffset = bytes.IndexOf((byte)'{');
        if (jsonOffset < 0)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(bytes[jsonOffset..].ToArray());
            if (!document.RootElement.TryGetProperty("mico", out var mico) ||
                !mico.TryGetProperty("device_id", out var deviceIdElement))
            {
                return false;
            }

            var deviceId = deviceIdElement.GetString();
            if (string.IsNullOrWhiteSpace(deviceId))
            {
                return false;
            }

            appData = new MiPlayMicoAppData(bytes[..jsonOffset].ToArray(), deviceId.Trim());
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
