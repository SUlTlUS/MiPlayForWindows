using System.Globalization;
using System.Text;

namespace DLNACast.Core.MiPlay;

public sealed record MiPlayRtspWireHeader(
    string Name,
    string Value,
    bool SpaceAfterColon = true);

public sealed record MiPlayRtspWireMessage(
    string StartLine,
    IReadOnlyList<MiPlayRtspWireHeader> Headers,
    byte[] Body)
{
    public string? GetHeader(string name) =>
        Headers.FirstOrDefault(header => header.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value;
}

/// <summary>
/// Direction-neutral RTSP wire codec that preserves header order and the one
/// captured TimeOffset header without a space after ':'. It can consume one
/// message from a coalesced TCP buffer and does not retain later media bytes.
/// </summary>
public static class MiPlayRtspWireMessageCodec
{
    public const int MaximumHeaderLength = 64 * 1024;
    public const int MaximumBodyLength = 4 * 1024 * 1024;

    public static byte[] Encode(
        string startLine,
        IEnumerable<MiPlayRtspWireHeader> headers,
        ReadOnlySpan<byte> body)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(startLine);
        ArgumentNullException.ThrowIfNull(headers);
        if (startLine.Contains('\r', StringComparison.Ordinal) ||
            startLine.Contains('\n', StringComparison.Ordinal))
        {
            throw new ArgumentException("The RTSP start line must be a single line.", nameof(startLine));
        }
        if (body.Length > MaximumBodyLength)
        {
            throw new ArgumentOutOfRangeException(nameof(body));
        }

        var headerList = headers.ToArray();
        foreach (var header in headerList)
        {
            ValidateHeader(header);
        }

        var contentLengthHeader = headerList.FirstOrDefault(
            header => header.Name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase));
        if (contentLengthHeader is null && !body.IsEmpty)
        {
            throw new ArgumentException("An RTSP body requires an exact Content-Length header.", nameof(headers));
        }
        if (contentLengthHeader is not null &&
            (!int.TryParse(contentLengthHeader.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var contentLength) ||
             contentLength != body.Length))
        {
            throw new ArgumentException("The RTSP Content-Length header must equal the body length.", nameof(headers));
        }

        using var stream = new MemoryStream();
        using (var writer = new StreamWriter(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.NewLine = "\r\n";
            writer.Write(startLine);
            writer.Write("\r\n");
            foreach (var header in headerList)
            {
                writer.Write(header.Name);
                writer.Write(header.SpaceAfterColon ? ": " : ":");
                writer.Write(header.Value);
                writer.Write("\r\n");
            }
            writer.Write("\r\n");
        }

        stream.Write(body);
        return stream.ToArray();
    }

    public static bool TryDecode(
        ReadOnlySpan<byte> data,
        out MiPlayRtspWireMessage? message,
        out int bytesConsumed)
    {
        message = null;
        bytesConsumed = 0;
        var headerEnd = data.IndexOf("\r\n\r\n"u8);
        if (headerEnd < 0 || headerEnd > MaximumHeaderLength)
        {
            return false;
        }

        var headerLength = headerEnd + 4;
        var lines = Encoding.ASCII.GetString(data[..headerEnd])
            .Split("\r\n", StringSplitOptions.None);
        if (lines.Length == 0 || string.IsNullOrWhiteSpace(lines[0]))
        {
            return false;
        }

        var headers = new List<MiPlayRtspWireHeader>(Math.Max(0, lines.Length - 1));
        var bodyLength = 0;
        for (var index = 1; index < lines.Length; index++)
        {
            var separator = lines[index].IndexOf(':');
            if (separator <= 0)
            {
                return false;
            }

            var hadSpace = separator + 1 < lines[index].Length && lines[index][separator + 1] == ' ';
            var header = new MiPlayRtspWireHeader(
                lines[index][..separator],
                lines[index][(separator + 1)..].TrimStart(),
                hadSpace);
            headers.Add(header);
            if (header.Name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) &&
                (!int.TryParse(header.Value, NumberStyles.None, CultureInfo.InvariantCulture, out bodyLength) ||
                 bodyLength is < 0 or > MaximumBodyLength))
            {
                return false;
            }
        }

        if (data.Length - headerLength < bodyLength)
        {
            return false;
        }

        message = new MiPlayRtspWireMessage(
            lines[0],
            headers,
            data.Slice(headerLength, bodyLength).ToArray());
        bytesConsumed = headerLength + bodyLength;
        return true;
    }

    private static void ValidateHeader(MiPlayRtspWireHeader header)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentException.ThrowIfNullOrWhiteSpace(header.Name);
        ArgumentNullException.ThrowIfNull(header.Value);
        if (header.Name.Contains(':', StringComparison.Ordinal) ||
            header.Name.Contains('\r', StringComparison.Ordinal) ||
            header.Name.Contains('\n', StringComparison.Ordinal) ||
            header.Value.Contains('\r', StringComparison.Ordinal) ||
            header.Value.Contains('\n', StringComparison.Ordinal))
        {
            throw new ArgumentException("RTSP headers must be one name/value line.", nameof(header));
        }
    }
}
