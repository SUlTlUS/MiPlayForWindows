using System.Net;
using System.Text;
using System.Text.Json;

namespace DLNACast.Core.MiPlay;

public sealed record MiPlaySessionKeys
{
    private MiPlaySessionKeys(
        string wlan0Ip,
        string authKey,
        string streamKey,
        string streamIv)
    {
        Wlan0Ip = wlan0Ip;
        AuthKey = authKey;
        StreamKey = streamKey;
        StreamIv = streamIv;
    }

    public string Wlan0Ip { get; }
    public string AuthKey { get; }
    public string StreamKey { get; }
    public string StreamIv { get; }

    public static MiPlaySessionKeys Generate(IPAddress senderAddress) => Create(
        senderAddress,
        GenerateKey(),
        GenerateKey(),
        GenerateKey());

    public static MiPlaySessionKeys Create(
        IPAddress senderAddress,
        string authKey,
        string streamKey,
        string streamIv)
    {
        ArgumentNullException.ThrowIfNull(senderAddress);
        ValidateKey(authKey, nameof(authKey));
        ValidateKey(streamKey, nameof(streamKey));
        ValidateKey(streamIv, nameof(streamIv));

        return new MiPlaySessionKeys(senderAddress.ToString(), authKey, streamKey, streamIv);
    }

    public string ToJson()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("wlan0ip", Wlan0Ip);
            writer.WriteString("authKey", AuthKey);
            writer.WriteString("streamKey", StreamKey);
            writer.WriteString("streamIV", StreamIv);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string GenerateKey() => Guid.NewGuid().ToString("N")[..16];

    private static void ValidateKey(string key, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(key, parameterName);
        if (Encoding.UTF8.GetByteCount(key) != 16 || key.Any(character => character > 0x7f))
        {
            throw new ArgumentException("MiPlay session keys must contain exactly 16 ASCII bytes.", parameterName);
        }
    }
}
