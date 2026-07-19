using System.Globalization;
using System.Text;

namespace DLNACast.Core.MiPlay;

public sealed record MiPlayRtspRequest(
    string Method,
    string RequestTarget,
    Version Version,
    IReadOnlyDictionary<string, string> Headers,
    byte[] Body)
{
    public MiPlayRtspTransport? Transport =>
        Headers.TryGetValue("Transport", out var value) && MiPlayRtspTransport.TryParse(value, out var transport)
            ? transport
            : null;
}

public static class MiPlayRtspRequestCodec
{
    public const int MaximumHeaderLength = 64 * 1024;
    public const int MaximumBodyLength = 4 * 1024 * 1024;

    public static bool TryDecode(
        ReadOnlySpan<byte> data,
        out MiPlayRtspRequest? request,
        out int bytesConsumed)
    {
        request = null;
        bytesConsumed = 0;
        var headerEnd = data.IndexOf("\r\n\r\n"u8);
        if (headerEnd < 0 || headerEnd > MaximumHeaderLength)
        {
            return false;
        }

        var headerLength = headerEnd + 4;
        var headerText = Encoding.ASCII.GetString(data[..headerEnd]);
        var lines = headerText.Split("\r\n", StringSplitOptions.None);
        if (lines.Length == 0)
        {
            return false;
        }

        var requestLine = lines[0].Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (requestLine.Length != 3 ||
            !requestLine[2].StartsWith("RTSP/", StringComparison.OrdinalIgnoreCase) ||
            !Version.TryParse(requestLine[2][5..], out var version))
        {
            return false;
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 1; index < lines.Length; index++)
        {
            var separator = lines[index].IndexOf(':');
            if (separator <= 0)
            {
                return false;
            }

            headers[lines[index][..separator].Trim()] = lines[index][(separator + 1)..].Trim();
        }

        var bodyLength = 0;
        if (headers.TryGetValue("Content-Length", out var contentLength) &&
            (!int.TryParse(contentLength, NumberStyles.None, CultureInfo.InvariantCulture, out bodyLength) ||
             bodyLength is < 0 or > MaximumBodyLength))
        {
            return false;
        }

        if (data.Length - headerLength < bodyLength)
        {
            return false;
        }

        request = new MiPlayRtspRequest(
            requestLine[0],
            requestLine[1],
            version,
            headers,
            data.Slice(headerLength, bodyLength).ToArray());
        bytesConsumed = headerLength + bodyLength;
        return true;
    }
}
