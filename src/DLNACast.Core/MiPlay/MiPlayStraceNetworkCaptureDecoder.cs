using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace DLNACast.Core.MiPlay;

public enum MiPlayStraceNetworkDirection
{
    Outbound,
    Inbound,
}

public sealed record MiPlayStraceTcpEndpoint(
    string LocalAddress,
    int LocalPort,
    string RemoteAddress,
    int RemotePort,
    int? FileDescriptor = null)
{
    public bool IsMapped => FileDescriptor is null;

    public override string ToString() =>
        FileDescriptor is int fileDescriptor
            ? $"fd:{fileDescriptor} (preconnected endpoint unmapped)"
            : $"{LocalAddress}:{LocalPort}->{RemoteAddress}:{RemotePort}";
}

public sealed record MiPlayStraceTcpChunkSummary(
    int LineNumber,
    int ThreadId,
    string Timestamp,
    MiPlayStraceNetworkDirection Direction,
    MiPlayStraceTcpEndpoint Endpoint,
    int ByteLength,
    string Sha256Hex);

public sealed record MiPlayStraceCommandFrameSummary(
    int Index,
    int FirstLineNumber,
    MiPlayStraceNetworkDirection Direction,
    MiPlayStraceTcpEndpoint Endpoint,
    int StreamOffset,
    ushort Command,
    ushort Sequence,
    int PayloadLength,
    string PayloadSha256Hex,
    string FrameSha256Hex,
    string PayloadHexPrefix);

public sealed record MiPlayStraceNetworkCaptureIssue(int LineNumber, string Reason);

public sealed record MiPlayStraceNetworkCaptureDecodeResult(
    IReadOnlyList<MiPlayStraceTcpChunkSummary> Chunks,
    IReadOnlyList<MiPlayStraceCommandFrameSummary> Frames,
    IReadOnlyList<MiPlayStraceNetworkCaptureIssue> Issues,
    int ControlPort,
    bool ContainsRawPayloads);

/// <summary>
/// Offline parser for strace -xx sendto/recvfrom output. It reconstructs
/// unfinished/resumed syscalls by thread id, keeps only TCP streams involving
/// the selected MiPlay control port, and exposes hashes plus command metadata.
/// Raw syscall payloads are retained only while decoding and are never returned.
/// </summary>
public static partial class MiPlayStraceNetworkCaptureDecoder
{
    public const int DefaultPayloadHexPrefixBytes = 0;

    public static MiPlayStraceNetworkCaptureDecodeResult Decode(
        string straceText,
        int controlPort = MiPlayProtocolConstants.DefaultControlPort,
        int payloadHexPrefixBytes = DefaultPayloadHexPrefixBytes)
    {
        ArgumentNullException.ThrowIfNull(straceText);
        if (controlPort is < 1 or > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(controlPort));
        }

        if (payloadHexPrefixBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(payloadHexPrefixBytes));
        }

        var pending = new Dictionary<int, PendingCall>();
        var rawChunks = new List<RawChunk>();
        var issues = new List<MiPlayStraceNetworkCaptureIssue>();
        var lines = straceText.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        for (var index = 0; index < lines.Length; index++)
        {
            var lineNumber = index + 1;
            var line = lines[index];
            var prefix = LinePrefixRegex().Match(line);
            if (!prefix.Success ||
                !int.TryParse(prefix.Groups["tid"].Value, out var threadId))
            {
                continue;
            }

            var timestamp = prefix.Groups["time"].Value;
            var body = line[prefix.Length..];
            var resumed = ResumedCallRegex().Match(body);
            if (resumed.Success)
            {
                if (!pending.Remove(threadId, out var pendingCall))
                {
                    // The unfinished half may have been an explicitly mapped
                    // non-control TCP/UDP/UNIX call that this filtered decoder
                    // intentionally skipped. A resumed line has no fd/endpoint,
                    // so it cannot safely be reclassified as MiPlay control.
                    continue;
                }

                if (!string.Equals(
                        resumed.Groups["call"].Value,
                        pendingCall.CallName,
                        StringComparison.Ordinal))
                {
                    issues.Add(new(lineNumber, "The resumed syscall name does not match its unfinished call."));
                    continue;
                }

                var resumedPayload = TryExtractFirstCString(body, out var decoded, out var decodeError)
                    ? decoded
                    : null;
                if (decodeError is not null)
                {
                    issues.Add(new(lineNumber, decodeError));
                    continue;
                }

                CompleteCall(
                    pendingCall with { Payload = resumedPayload ?? pendingCall.Payload },
                    body,
                    lineNumber,
                    rawChunks,
                    issues);
                continue;
            }

            var call = NetworkCallRegex().Match(body);
            if (!call.Success ||
                !int.TryParse(call.Groups["fd"].Value, out var fileDescriptor))
            {
                continue;
            }

            var hasMappedEndpoint = TryParseEndpoint(body, out var endpoint);
            if (!hasMappedEndpoint &&
                (body.Contains("<UDP:", StringComparison.Ordinal) ||
                 body.Contains("<UNIX:", StringComparison.Ordinal)))
            {
                continue;
            }

            if (hasMappedEndpoint &&
                endpoint.LocalPort != controlPort &&
                endpoint.RemotePort != controlPort)
            {
                continue;
            }

            endpoint = hasMappedEndpoint
                ? endpoint
                : new MiPlayStraceTcpEndpoint(
                    "unmapped",
                    0,
                    "unmapped",
                    0,
                    fileDescriptor);

            var callName = call.Groups["call"].Value;
            var direction = callName == "sendto"
                ? MiPlayStraceNetworkDirection.Outbound
                : MiPlayStraceNetworkDirection.Inbound;
            var hasPayload = TryExtractFirstCString(body, out var payload, out var payloadError);
            if (payloadError is not null)
            {
                issues.Add(new(lineNumber, payloadError));
                continue;
            }

            var parsedCall = new PendingCall(
                lineNumber,
                threadId,
                timestamp,
                callName,
                direction,
                endpoint,
                RequireStrictCommandPayload: !hasMappedEndpoint,
                hasPayload ? payload : null);

            if (body.Contains("<unfinished ...>", StringComparison.Ordinal))
            {
                if (!pending.TryAdd(threadId, parsedCall))
                {
                    issues.Add(new(lineNumber, "This thread already has an unfinished network syscall."));
                }

                continue;
            }

            CompleteCall(parsedCall, body, lineNumber, rawChunks, issues);
        }

        foreach (var unfinished in pending.Values)
        {
            issues.Add(new(unfinished.LineNumber, "The capture ended before this network syscall resumed."));
        }

        var chunks = rawChunks
            .Select(chunk => new MiPlayStraceTcpChunkSummary(
                chunk.LineNumber,
                chunk.ThreadId,
                chunk.Timestamp,
                chunk.Direction,
                chunk.Endpoint,
                chunk.Payload.Length,
                Convert.ToHexString(SHA256.HashData(chunk.Payload))))
            .ToArray();

        var frameCandidates = new List<FrameCandidate>();
        foreach (var streamGroup in rawChunks.GroupBy(chunk => new StreamKey(chunk.Direction, chunk.Endpoint)))
        {
            var orderedChunks = streamGroup.OrderBy(chunk => chunk.LineNumber).ToArray();
            var streamLength = orderedChunks.Sum(chunk => chunk.Payload.Length);
            var stream = new byte[streamLength];
            var chunkRanges = new List<ChunkRange>(orderedChunks.Length);
            var streamOffset = 0;
            foreach (var chunk in orderedChunks)
            {
                chunk.Payload.CopyTo(stream.AsSpan(streamOffset));
                chunkRanges.Add(new ChunkRange(streamOffset, streamOffset + chunk.Payload.Length, chunk.LineNumber));
                streamOffset += chunk.Payload.Length;
            }

            var decodedStream = MiPlayCapturedCommandStreamDecoder.Decode(stream, payloadHexPrefixBytes);
            foreach (var streamIssue in decodedStream.Issues)
            {
                issues.Add(new(
                    FindFirstLineNumber(chunkRanges, streamIssue.Offset),
                    $"{streamGroup.Key.Direction} {streamGroup.Key.Endpoint} stream offset {streamIssue.Offset}: {streamIssue.Reason}"));
            }

            foreach (var frame in decodedStream.Frames)
            {
                var frameLength = MiPlayProtocolConstants.CommandHeaderLength + frame.PayloadLength;
                var frameBytes = stream.AsSpan(frame.Offset, frameLength);
                frameCandidates.Add(new FrameCandidate(
                    FindFirstLineNumber(chunkRanges, frame.Offset),
                    streamGroup.Key.Direction,
                    streamGroup.Key.Endpoint,
                    frame.Offset,
                    frame.Command,
                    frame.Sequence,
                    frame.PayloadLength,
                    frame.PayloadSha256Hex,
                    Convert.ToHexString(SHA256.HashData(frameBytes)),
                    frame.PayloadHexPrefix));
            }
        }

        var frames = frameCandidates
            .OrderBy(frame => frame.FirstLineNumber)
            .ThenBy(frame => frame.StreamOffset)
            .Select((frame, index) => new MiPlayStraceCommandFrameSummary(
                index,
                frame.FirstLineNumber,
                frame.Direction,
                frame.Endpoint,
                frame.StreamOffset,
                frame.Command,
                frame.Sequence,
                frame.PayloadLength,
                frame.PayloadSha256Hex,
                frame.FrameSha256Hex,
                frame.PayloadHexPrefix))
            .ToArray();

        return new MiPlayStraceNetworkCaptureDecodeResult(
            chunks,
            frames,
            [.. issues.OrderBy(issue => issue.LineNumber)],
            controlPort,
            ContainsRawPayloads: false);
    }

    private static void CompleteCall(
        PendingCall call,
        string completionText,
        int completionLineNumber,
        ICollection<RawChunk> chunks,
        ICollection<MiPlayStraceNetworkCaptureIssue> issues)
    {
        var returnMatch = ReturnValueRegex().Match(completionText);
        if (!returnMatch.Success ||
            !int.TryParse(returnMatch.Groups["count"].Value, out var returnedCount))
        {
            issues.Add(new(completionLineNumber, "The network syscall return byte count could not be parsed."));
            return;
        }

        if (returnedCount < 0)
        {
            return;
        }

        if (returnedCount == 0)
        {
            return;
        }

        if (call.Payload is null)
        {
            issues.Add(new(completionLineNumber, "The successful network syscall has no captured byte string."));
            return;
        }

        if (call.Payload.Length < returnedCount)
        {
            // A capture started after connect(2) cannot map an already-open fd.
            // For those fallback candidates strace's default -s truncation is
            // common, especially on RTSP/media sockets. Report truncation only
            // when the exposed prefix itself proves that the syscall contained
            // one MiPlay command frame of the returned length; otherwise skip it
            // silently instead of flooding an offline control-frame scan.
            if (call.RequireStrictCommandPayload &&
                !LooksLikeTruncatedCommandFrame(call.Payload, returnedCount))
            {
                return;
            }

            issues.Add(new(
                completionLineNumber,
                $"The syscall returned {returnedCount} bytes but strace exposed only {call.Payload.Length}; the truncated chunk was not decoded."));
            return;
        }

        var payload = call.Payload.Length == returnedCount
            ? call.Payload
            : call.Payload.AsSpan(0, returnedCount).ToArray();
        if (call.RequireStrictCommandPayload && !ContainsOnlyCompleteCommandFrames(payload))
        {
            return;
        }

        chunks.Add(new RawChunk(
            call.LineNumber,
            call.ThreadId,
            call.Timestamp,
            call.Direction,
            call.Endpoint,
            payload));
    }

    private static bool ContainsOnlyCompleteCommandFrames(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < MiPlayProtocolConstants.CommandHeaderLength)
        {
            return false;
        }

        var offset = 0;
        while (offset < payload.Length)
        {
            if (!MiPlayCommandFrameCodec.TryDecode(
                    payload[offset..],
                    out var frame,
                    out var bytesConsumed) ||
                frame is null ||
                bytesConsumed <= 0)
            {
                return false;
            }

            offset += bytesConsumed;
        }

        return offset == payload.Length;
    }

    private static bool LooksLikeTruncatedCommandFrame(ReadOnlySpan<byte> prefix, int returnedCount)
    {
        if (prefix.Length < MiPlayProtocolConstants.CommandHeaderLength ||
            prefix[0] != MiPlayProtocolConstants.CommandFrameMagic)
        {
            return false;
        }

        var payloadLength = BinaryPrimitives.ReadUInt32BigEndian(prefix.Slice(5, 4));
        return payloadLength <= int.MaxValue - MiPlayProtocolConstants.CommandHeaderLength &&
               MiPlayProtocolConstants.CommandHeaderLength + (int)payloadLength == returnedCount;
    }

    private static bool TryParseEndpoint(string text, out MiPlayStraceTcpEndpoint endpoint)
    {
        endpoint = null!;
        var match = EndpointRegex().Match(text);
        if (!match.Success ||
            !int.TryParse(match.Groups["localPort"].Value, out var localPort) ||
            !int.TryParse(match.Groups["remotePort"].Value, out var remotePort))
        {
            return false;
        }

        endpoint = new MiPlayStraceTcpEndpoint(
            match.Groups["local"].Value,
            localPort,
            match.Groups["remote"].Value,
            remotePort);
        return true;
    }

    private static bool TryExtractFirstCString(
        string text,
        out byte[] payload,
        out string? error)
    {
        payload = [];
        error = null;
        var quote = text.IndexOf('"');
        if (quote < 0)
        {
            return false;
        }

        var bytes = new List<byte>();
        for (var index = quote + 1; index < text.Length; index++)
        {
            var character = text[index];
            if (character == '"')
            {
                payload = [.. bytes];
                return true;
            }

            if (character != '\\')
            {
                if (character > 0x7f)
                {
                    error = "A strace byte string contains an unescaped non-ASCII character.";
                    return false;
                }

                bytes.Add((byte)character);
                continue;
            }

            if (++index >= text.Length)
            {
                error = "A strace byte string ends with an incomplete escape.";
                return false;
            }

            var escaped = text[index];
            if (escaped == 'x')
            {
                if (index + 2 >= text.Length ||
                    !byte.TryParse(
                        text.AsSpan(index + 1, 2),
                        System.Globalization.NumberStyles.HexNumber,
                        provider: null,
                        out var hexadecimal))
                {
                    error = "A strace byte string contains an invalid hexadecimal escape.";
                    return false;
                }

                bytes.Add(hexadecimal);
                index += 2;
                continue;
            }

            if (escaped is >= '0' and <= '7')
            {
                var value = escaped - '0';
                var digits = 1;
                while (digits < 3 && index + 1 < text.Length && text[index + 1] is >= '0' and <= '7')
                {
                    value = (value * 8) + (text[++index] - '0');
                    digits++;
                }

                if (value > byte.MaxValue)
                {
                    error = "A strace byte string contains an octal escape larger than one byte.";
                    return false;
                }

                bytes.Add((byte)value);
                continue;
            }

            bytes.Add(escaped switch
            {
                'a' => 0x07,
                'b' => 0x08,
                't' => 0x09,
                'n' => 0x0a,
                'v' => 0x0b,
                'f' => 0x0c,
                'r' => 0x0d,
                '\\' => 0x5c,
                '"' => 0x22,
                _ => (byte)escaped,
            });
        }

        error = "A strace byte string has no closing quote.";
        return false;
    }

    private static int FindFirstLineNumber(IReadOnlyList<ChunkRange> ranges, int offset) =>
        ranges.FirstOrDefault(range => offset >= range.Start && offset < range.End)?.LineNumber ??
        ranges.LastOrDefault()?.LineNumber ??
        0;

    private sealed record PendingCall(
        int LineNumber,
        int ThreadId,
        string Timestamp,
        string CallName,
        MiPlayStraceNetworkDirection Direction,
        MiPlayStraceTcpEndpoint Endpoint,
        bool RequireStrictCommandPayload,
        byte[]? Payload);

    private sealed record RawChunk(
        int LineNumber,
        int ThreadId,
        string Timestamp,
        MiPlayStraceNetworkDirection Direction,
        MiPlayStraceTcpEndpoint Endpoint,
        byte[] Payload);

    private sealed record StreamKey(
        MiPlayStraceNetworkDirection Direction,
        MiPlayStraceTcpEndpoint Endpoint);

    private sealed record ChunkRange(int Start, int End, int LineNumber);

    private sealed record FrameCandidate(
        int FirstLineNumber,
        MiPlayStraceNetworkDirection Direction,
        MiPlayStraceTcpEndpoint Endpoint,
        int StreamOffset,
        ushort Command,
        ushort Sequence,
        int PayloadLength,
        string PayloadSha256Hex,
        string FrameSha256Hex,
        string PayloadHexPrefix);

    [GeneratedRegex(@"^(?<tid>\d+)\s+(?<time>\d{2}:\d{2}:\d{2}\.\d+)\s+")]
    private static partial Regex LinePrefixRegex();

    [GeneratedRegex(@"(?<call>sendto|recvfrom)\((?<fd>\d+)")]
    private static partial Regex NetworkCallRegex();

    [GeneratedRegex(@"<\.\.\.\s+(?<call>sendto|recvfrom)\s+resumed>")]
    private static partial Regex ResumedCallRegex();

    [GeneratedRegex(@"TCP:\[(?<local>[^:\]]+):(?<localPort>\d+)->(?<remote>[^:\]]+):(?<remotePort>\d+)\]")]
    private static partial Regex EndpointRegex();

    [GeneratedRegex(@"=\s*(?<count>-?\d+)")]
    private static partial Regex ReturnValueRegex();
}
