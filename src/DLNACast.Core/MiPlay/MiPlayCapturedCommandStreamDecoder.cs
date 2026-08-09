using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace DLNACast.Core.MiPlay;

public sealed record MiPlayCapturedSafetyDataHeaderSummary(
    int HeaderLength,
    byte Flags,
    byte? PaddingLength,
    uint? IntegrityValue,
    int PayloadOffset,
    int PayloadLength,
    bool IsEncrypted,
    bool HasPaddingLengthField,
    bool HasIntegrityValue);

public sealed record MiPlayCapturedCommandFrameSummary(
    int Index,
    int Offset,
    ushort Command,
    ushort Sequence,
    int PayloadLength,
    string PayloadSha256Hex,
    string PayloadHexPrefix,
    MiPlayCapturedSafetyDataHeaderSummary? SafetyDataHeader)
{
    public bool HasSafetyDataVersion1Header => SafetyDataHeader is not null;
}

public sealed record MiPlayCapturedCommandStreamIssue(int Offset, string Reason);

public sealed record MiPlayCapturedCommandStreamDecodeResult(
    IReadOnlyList<MiPlayCapturedCommandFrameSummary> Frames,
    IReadOnlyList<MiPlayCapturedCommandStreamIssue> Issues,
    int BytesScanned,
    int BytesSkipped,
    bool EndsWithIncompleteFrame);

/// <summary>
/// Offline decoder for TCP payload bytes captured from an official MiPlay sender.
/// It parses only the legacy '$' command envelope and optional SafetyData v1
/// metadata. It never decrypts, replays, opens sockets, or sends device frames.
/// </summary>
public static class MiPlayCapturedCommandStreamDecoder
{
    public const int DefaultPayloadHexPrefixBytes = 32;

    public static MiPlayCapturedCommandStreamDecodeResult Decode(
        ReadOnlySpan<byte> tcpPayloadStream,
        int payloadHexPrefixBytes = DefaultPayloadHexPrefixBytes)
    {
        if (payloadHexPrefixBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(payloadHexPrefixBytes), "Payload prefix length cannot be negative.");
        }

        var frames = new List<MiPlayCapturedCommandFrameSummary>();
        var issues = new List<MiPlayCapturedCommandStreamIssue>();
        var offset = 0;
        var bytesSkipped = 0;
        var endsWithIncompleteFrame = false;

        while (offset < tcpPayloadStream.Length)
        {
            if (tcpPayloadStream[offset] != MiPlayProtocolConstants.CommandFrameMagic)
            {
                var nextMagic = tcpPayloadStream[offset..].IndexOf(MiPlayProtocolConstants.CommandFrameMagic);
                if (nextMagic < 0)
                {
                    bytesSkipped += tcpPayloadStream.Length - offset;
                    _ = tcpPayloadStream.Length;
                    break;
                }

                bytesSkipped += nextMagic;
                offset += nextMagic;
            }

            var remaining = tcpPayloadStream.Length - offset;
            if (remaining < MiPlayProtocolConstants.CommandHeaderLength)
            {
                issues.Add(new MiPlayCapturedCommandStreamIssue(offset, "Incomplete command header at end of captured stream."));
                endsWithIncompleteFrame = true;
                break;
            }

            var payloadLength = BinaryPrimitives.ReadUInt32BigEndian(tcpPayloadStream.Slice(offset + 5, 4));
            if (payloadLength > MiPlayCommandFrameCodec.MaximumPayloadLength)
            {
                issues.Add(new MiPlayCapturedCommandStreamIssue(
                    offset,
                    $"Payload length {payloadLength} exceeds the MiPlay command maximum {MiPlayCommandFrameCodec.MaximumPayloadLength}; skipped one candidate magic byte."));
                offset++;
                bytesSkipped++;
                continue;
            }

            var frameLength = checked(MiPlayProtocolConstants.CommandHeaderLength + (int)payloadLength);
            if (remaining < frameLength)
            {
                issues.Add(new MiPlayCapturedCommandStreamIssue(
                    offset,
                    $"Incomplete command frame at end of captured stream: header declares {payloadLength} payload bytes, but only {remaining - MiPlayProtocolConstants.CommandHeaderLength} are available."));
                endsWithIncompleteFrame = true;
                break;
            }

            if (!MiPlayCommandFrameCodec.TryDecode(tcpPayloadStream[offset..], out var frame, out var bytesConsumed) ||
                frame is null)
            {
                issues.Add(new MiPlayCapturedCommandStreamIssue(offset, "Command frame candidate failed strict decode; skipped one candidate magic byte."));
                offset++;
                bytesSkipped++;
                continue;
            }

            frames.Add(CreateFrameSummary(frames.Count, offset, frame, payloadHexPrefixBytes));
            offset += bytesConsumed;
        }

        return new MiPlayCapturedCommandStreamDecodeResult(
            frames,
            issues,
            tcpPayloadStream.Length,
            bytesSkipped,
            endsWithIncompleteFrame);
    }

    public static bool TryParseHexDump(string text, out byte[] bytes, out string? error)
    {
        ArgumentNullException.ThrowIfNull(text);

        var hex = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (char.IsWhiteSpace(ch) || ch is ':' or '-' or '_' or ',')
            {
                continue;
            }

            if (ch == '0' && i + 1 < text.Length && text[i + 1] is 'x' or 'X')
            {
                i++;
                continue;
            }

            if (!Uri.IsHexDigit(ch))
            {
                bytes = [];
                error = $"Invalid hex character '{ch}' at offset {i}.";
                return false;
            }

            hex.Append(ch);
        }

        if (hex.Length == 0)
        {
            bytes = [];
            error = null;
            return true;
        }

        if (hex.Length % 2 != 0)
        {
            bytes = [];
            error = "Hex input contains an odd number of hex digits.";
            return false;
        }

        bytes = new byte[hex.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] = Convert.ToByte(hex.ToString(i * 2, 2), 16);
        }

        error = null;
        return true;
    }

    private static MiPlayCapturedCommandFrameSummary CreateFrameSummary(
        int index,
        int offset,
        MiPlayCommandFrame frame,
        int payloadHexPrefixBytes)
    {
        var payload = frame.Payload.AsSpan();
        var hash = SHA256.HashData(payload);
        var prefixLength = Math.Min(payload.Length, payloadHexPrefixBytes);
        var prefix = payload[..prefixLength];

        return new MiPlayCapturedCommandFrameSummary(
            index,
            offset,
            frame.Command,
            frame.Sequence,
            frame.Payload.Length,
            Convert.ToHexString(hash),
            Convert.ToHexString(prefix),
            TryCreateSafetyDataSummary(payload));
    }

    private static MiPlayCapturedSafetyDataHeaderSummary? TryCreateSafetyDataSummary(ReadOnlySpan<byte> payload)
    {
        if (!MiPlaySafetyDataHeaderCodec.TryDecodeVersion1(payload, out var header) || header is null)
        {
            return null;
        }

        return new MiPlayCapturedSafetyDataHeaderSummary(
            header.HeaderLength,
            header.Flags,
            header.PaddingLength,
            header.IntegrityValue,
            header.PayloadOffset,
            header.PayloadLength,
            header.IsEncrypted,
            header.HasPaddingLengthField,
            header.HasIntegrityValue);
    }
}
