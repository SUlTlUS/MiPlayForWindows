namespace DLNACast.Core.MiPlay;

/// <summary>
/// Decodes the application-data container stored in the mDNS appsData TXT
/// field. Header bits 0-6 indicate data for an app slot and bit 7 continues
/// the header; each present slot is followed by a one-byte length and payload.
/// </summary>
public static class MiPlayMdnsAppData
{
    public static bool TryParse(
        string? base64,
        IReadOnlyList<int> applicationIds,
        out IReadOnlyDictionary<int, byte[]> applicationData)
    {
        ArgumentNullException.ThrowIfNull(applicationIds);
        applicationData = new Dictionary<int, byte[]>();
        if (string.IsNullOrWhiteSpace(base64))
        {
            return false;
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            return false;
        }

        if (bytes.Length == 0)
        {
            return false;
        }

        var headerLength = 0;
        do
        {
            if (headerLength >= bytes.Length)
            {
                return false;
            }
        }
        while ((bytes[headerLength++] & 0x80) != 0);

        var payloadOffset = headerLength;
        var result = new Dictionary<int, byte[]>();
        for (var slot = 0; slot < applicationIds.Count; slot++)
        {
            var headerIndex = slot / 7;
            if (headerIndex >= headerLength)
            {
                break;
            }

            var hasData = (bytes[headerIndex] & (1 << (slot % 7))) != 0;
            if (!hasData)
            {
                result[applicationIds[slot]] = [];
                continue;
            }

            if (payloadOffset >= bytes.Length)
            {
                return false;
            }

            var length = bytes[payloadOffset++];
            if (length > bytes.Length - payloadOffset)
            {
                return false;
            }

            result[applicationIds[slot]] = bytes.AsSpan(payloadOffset, length).ToArray();
            payloadOffset += length;
        }

        // Consume data belonging to bitmap slots unknown to this caller so a
        // truncated or ambiguous aggregate is never accepted as valid.
        for (var slot = applicationIds.Count; slot < headerLength * 7; slot++)
        {
            if ((bytes[slot / 7] & (1 << (slot % 7))) == 0)
            {
                continue;
            }

            if (payloadOffset >= bytes.Length)
            {
                return false;
            }

            var length = bytes[payloadOffset++];
            if (length > bytes.Length - payloadOffset)
            {
                return false;
            }

            payloadOffset += length;
        }

        if (payloadOffset != bytes.Length)
        {
            return false;
        }

        applicationData = result;
        return true;
    }
}
