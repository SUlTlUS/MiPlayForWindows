using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DLNACast.Core.MiPlay;

public enum MiPlaySafetyHashAlgorithm : uint
{
    Md5 = 1,
    Sha1 = 2,
    Sha256 = 4
}

public sealed record MiPlaySafetyAuthChallenge(string AuthMessage)
{
    public byte[] ToJsonPayload() => MiPlaySafetyAuthCodec.EncodeChallenge(this);
}

public sealed record MiPlaySafetyAuthAcknowledgement(string AuthMessageAck, string Result = "1")
{
    public byte[] ToJsonPayload() => MiPlaySafetyAuthCodec.EncodeAcknowledgement(this);
}

/// <summary>
/// Builds and validates the JSON/HMAC payloads used by verified modern MiPlay SafetyAuth commands.
/// This class has no network behaviour and does not establish device trust.
/// </summary>
public static class MiPlaySafetyAuthCodec
{
    public static MiPlaySafetyAuthChallenge CreateChallenge(long timestampMicroseconds)
    {
        if (timestampMicroseconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timestampMicroseconds));
        }

        var timestamp = timestampMicroseconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var message = ToLowerHex(MD5.HashData(Encoding.UTF8.GetBytes(timestamp)));
        return new MiPlaySafetyAuthChallenge(message);
    }

    public static MiPlaySafetyAuthAcknowledgement CreateAcknowledgement(
        string peerAuthMessage,
        string authKey,
        MiPlaySafetyHashAlgorithm algorithm)
    {
        ValidateNonEmpty(peerAuthMessage, nameof(peerAuthMessage));
        ValidateNonEmpty(authKey, nameof(authKey));

        return new MiPlaySafetyAuthAcknowledgement(ComputeHmac(peerAuthMessage, authKey, algorithm));
    }

    public static bool VerifyAcknowledgement(
        string localAuthMessage,
        string authKey,
        MiPlaySafetyHashAlgorithm algorithm,
        MiPlaySafetyAuthAcknowledgement acknowledgement)
    {
        ArgumentNullException.ThrowIfNull(acknowledgement);
        ValidateNonEmpty(localAuthMessage, nameof(localAuthMessage));
        ValidateNonEmpty(authKey, nameof(authKey));

        var expected = Encoding.UTF8.GetBytes(ComputeHmac(localAuthMessage, authKey, algorithm));
        var actual = Encoding.UTF8.GetBytes(acknowledgement.AuthMessageAck);
        return expected.Length == actual.Length &&
               CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    public static byte[] EncodeChallenge(MiPlaySafetyAuthChallenge challenge)
    {
        ArgumentNullException.ThrowIfNull(challenge);
        ValidateNonEmpty(challenge.AuthMessage, nameof(challenge));

        return WriteJson(writer => writer.WriteString("authMsg", challenge.AuthMessage));
    }

    public static byte[] EncodeAcknowledgement(MiPlaySafetyAuthAcknowledgement acknowledgement)
    {
        ArgumentNullException.ThrowIfNull(acknowledgement);
        ValidateNonEmpty(acknowledgement.AuthMessageAck, nameof(acknowledgement));
        if (!IsAcceptedAcknowledgementResult(acknowledgement.Result))
        {
            throw new ArgumentOutOfRangeException(nameof(acknowledgement), acknowledgement.Result, "Unsupported MiPlay SafetyAuth acknowledgement result.");
        }

        return WriteJson(writer =>
        {
            writer.WriteString("result", acknowledgement.Result);
            writer.WriteString("authMsgAck", acknowledgement.AuthMessageAck);
        });
    }

    public static bool TryDecodeChallenge(ReadOnlySpan<byte> payload, out MiPlaySafetyAuthChallenge? challenge)
    {
        challenge = null;
        if (!TryGetRequiredString(payload, "authMsg", out var authMessage))
        {
            return false;
        }

        challenge = new MiPlaySafetyAuthChallenge(authMessage);
        return true;
    }

    public static bool TryDecodeAcknowledgement(
        ReadOnlySpan<byte> payload,
        out MiPlaySafetyAuthAcknowledgement? acknowledgement)
    {
        acknowledgement = null;
        if (!TryGetRequiredString(payload, "result", out var result) ||
            !IsAcceptedAcknowledgementResult(result) ||
            !TryGetRequiredString(payload, "authMsgAck", out var authMessageAck))
        {
            return false;
        }

        acknowledgement = new MiPlaySafetyAuthAcknowledgement(authMessageAck, result);
        return true;
    }

    private static string ComputeHmac(
        string message,
        string key,
        MiPlaySafetyHashAlgorithm algorithm)
    {
        var messageBytes = Encoding.UTF8.GetBytes(message);
        var keyBytes = Encoding.UTF8.GetBytes(key);
        var hash = algorithm switch
        {
            MiPlaySafetyHashAlgorithm.Md5 => HMACMD5.HashData(keyBytes, messageBytes),
            MiPlaySafetyHashAlgorithm.Sha1 => HMACSHA1.HashData(keyBytes, messageBytes),
            MiPlaySafetyHashAlgorithm.Sha256 => HMACSHA256.HashData(keyBytes, messageBytes),
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, "Unsupported MiPlay SafetyAuth algorithm.")
        };

        return ToLowerHex(hash);
    }

    private static bool TryGetRequiredString(ReadOnlySpan<byte> payload, string propertyName, out string value)
    {
        value = string.Empty;

        try
        {
            using var document = JsonDocument.Parse(payload.ToArray());
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty(propertyName, out var property) ||
                property.ValueKind != JsonValueKind.String ||
                string.IsNullOrEmpty(property.GetString()))
            {
                return false;
            }

            value = property.GetString()!;
            return true;
        }
        catch (JsonException)
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

    private static string ToLowerHex(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(bytes).ToLowerInvariant();

    private static bool IsAcceptedAcknowledgementResult(string result) =>
        string.Equals(result, "0", StringComparison.Ordinal) ||
        string.Equals(result, "1", StringComparison.Ordinal);

    private static void ValidateNonEmpty(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrEmpty(value, parameterName);
    }
}

