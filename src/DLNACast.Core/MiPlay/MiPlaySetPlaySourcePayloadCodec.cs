using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace DLNACast.Core.MiPlay;

public sealed record MiPlaySetPlaySourcePayload(
    string? RefChannel,
    string? RefFunction,
    string? RefContent);

/// <summary>
/// Offline codec for the official source-side Cmd_SetPlaySource (0x0040)
/// payload recovered from the Mi13P phone firmware.
///
/// MiLinkOS3Cn StatsUtils.ontrackDataToJson builds a JSONObject with putOpt in
/// this exact key order, converts it to UTF-8, and passes those bytes to
/// CmdSessionControl.setPlaySource(byte[]). This codec models only those bytes;
/// it does not send them and does not authorize later open/media commands.
/// </summary>
public static class MiPlaySetPlaySourcePayloadCodec
{
    public static byte[] Encode(
        string? refChannel,
        string? refFunction,
        string? refContent)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }))
        {
            writer.WriteStartObject();
            WritePutOptString(writer, MiPlaySetPlaySourcePayloadSemanticsEvidence.RefChannelKey, refChannel);
            WritePutOptString(writer, MiPlaySetPlaySourcePayloadSemanticsEvidence.RefFunctionKey, refFunction);
            WritePutOptString(writer, MiPlaySetPlaySourcePayloadSemanticsEvidence.RefContentKey, refContent);
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    public static byte[] EncodeOfficialStatsDefaults(
        string? refChannel,
        string refFunction = "",
        string refContent = "") =>
        Encode(refChannel, refFunction, refContent);

    public static byte[] EncodeRecoveredOfficialRuntimePayload() =>
        Encode(
            MiPlayRealPhonePostAuthPlaintextEvidence.OfficialSetPlaySourceRefChannel,
            MiPlayRealPhonePostAuthPlaintextEvidence.OfficialSetPlaySourceRefFunction,
            MiPlayRealPhonePostAuthPlaintextEvidence.OfficialSetPlaySourceRefContent);

    public static MiPlaySetPlaySourcePayload Decode(ReadOnlySpan<byte> payload)
    {
        using var document = JsonDocument.Parse(payload.ToArray());
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("Cmd_SetPlaySource payload must be a JSON object.");
        }

        return new MiPlaySetPlaySourcePayload(
            ReadOptionalString(document.RootElement, MiPlaySetPlaySourcePayloadSemanticsEvidence.RefChannelKey),
            ReadOptionalString(document.RootElement, MiPlaySetPlaySourcePayloadSemanticsEvidence.RefFunctionKey),
            ReadOptionalString(document.RootElement, MiPlaySetPlaySourcePayloadSemanticsEvidence.RefContentKey));
    }

    public static string DecodeUtf8(ReadOnlySpan<byte> payload) =>
        Encoding.UTF8.GetString(payload);

    private static void WritePutOptString(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is not null)
        {
            writer.WriteString(name, value);
        }
    }

    private static string? ReadOptionalString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw new FormatException($"Cmd_SetPlaySource field '{name}' must be a string.");
        }

        return value.GetString();
    }
}
