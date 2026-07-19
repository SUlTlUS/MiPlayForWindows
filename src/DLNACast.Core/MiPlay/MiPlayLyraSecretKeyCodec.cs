using System.Text.Json;

namespace DLNACast.Core.MiPlay;

/// <summary>
/// The trusted Lyra session material consumed by CmdSource::setLyraInfo in the
/// Xiaomi 18.0.0.3 sample. Acquiring these values is intentionally outside this
/// codec: they must arrive through a trusted Lyra/Continuity session.
/// </summary>
public sealed record MiPlayLyraSecretKeyCommand(
    string Wlan0Ip,
    string AuthKey,
    string StreamKey,
    string StreamIv)
{
    public byte[] ToJsonPayload() => MiPlayLyraSecretKeyCodec.Encode(this);
}

/// <summary>
/// Offline JSON codec for the four fields validated by the native Lyra setup path.
/// This class never discovers, generates, logs, or transmits secret key material.
/// </summary>
public static class MiPlayLyraSecretKeyCodec
{
    public static byte[] Encode(MiPlayLyraSecretKeyCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("wlan0ip", command.Wlan0Ip);
            writer.WriteString("authKey", command.AuthKey);
            writer.WriteString("streamKey", command.StreamKey);
            writer.WriteString("streamIV", command.StreamIv);
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    public static bool TryDecode(
        ReadOnlySpan<byte> payload,
        out MiPlayLyraSecretKeyCommand? command)
    {
        command = null;

        try
        {
            using var document = JsonDocument.Parse(payload.ToArray());
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !TryGetString(root, "wlan0ip", out var wlan0Ip) ||
                !TryGetString(root, "authKey", out var authKey) ||
                !TryGetString(root, "streamKey", out var streamKey) ||
                !TryGetString(root, "streamIV", out var streamIv))
            {
                return false;
            }

            command = new MiPlayLyraSecretKeyCommand(wlan0Ip, authKey, streamKey, streamIv);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryGetString(JsonElement root, string name, out string value)
    {
        value = string.Empty;
        if (!root.TryGetProperty(name, out var property) ||
            property.ValueKind != JsonValueKind.String ||
            property.GetString() is not { } stringValue)
        {
            return false;
        }

        value = stringValue;
        return true;
    }
}
