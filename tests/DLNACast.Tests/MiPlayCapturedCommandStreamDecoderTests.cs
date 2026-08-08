using System.Security.Cryptography;
using System.Text;
using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayCapturedCommandStreamDecoderTests
{
    [Fact]
    public void DecodeSplitsConcatenatedCommandFramesAndKeepsCaptureOffsets()
    {
        var firstPayload = Encoding.ASCII.GetBytes("abc");
        var secondPayload = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var first = MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.GetDeviceInfoCommand,
            0x0004,
            firstPayload);
        var second = MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.SetPlaySourceCommand,
            0x0005,
            secondPayload);
        var stream = new byte[2 + first.Length + second.Length];
        stream[0] = 0xde;
        stream[1] = 0xad;
        first.CopyTo(stream.AsSpan(2));
        second.CopyTo(stream.AsSpan(2 + first.Length));

        var result = MiPlayCapturedCommandStreamDecoder.Decode(stream, payloadHexPrefixBytes: 8);

        Assert.False(result.EndsWithIncompleteFrame);
        Assert.Empty(result.Issues);
        Assert.Equal(2, result.BytesSkipped);
        Assert.Equal(stream.Length, result.BytesScanned);
        Assert.Collection(
            result.Frames,
            frame =>
            {
                Assert.Equal(0, frame.Index);
                Assert.Equal(2, frame.Offset);
                Assert.Equal(MiPlayProtocolConstants.GetDeviceInfoCommand, frame.Command);
                Assert.Equal(0x0004, frame.Sequence);
                Assert.Equal(firstPayload.Length, frame.PayloadLength);
                Assert.Equal("616263", frame.PayloadHexPrefix);
                Assert.Equal(Convert.ToHexString(SHA256.HashData(firstPayload)), frame.PayloadSha256Hex);
                Assert.False(frame.HasSafetyDataVersion1Header);
            },
            frame =>
            {
                Assert.Equal(1, frame.Index);
                Assert.Equal(2 + first.Length, frame.Offset);
                Assert.Equal(MiPlayProtocolConstants.SetPlaySourceCommand, frame.Command);
                Assert.Equal(0x0005, frame.Sequence);
                Assert.Equal("01020304", frame.PayloadHexPrefix);
            });
    }

    [Fact]
    public void DecodeSummarizesSafetyDataVersionOneHeaderWithoutDecrypting()
    {
        var key = Enumerable.Range(0, 16).Select(i => (byte)i).ToArray();
        var iv = Enumerable.Range(16, 16).Select(i => (byte)i).ToArray();
        var safetyData = MiPlaySafetyDataCodec.EncryptVersion1(
            Encoding.UTF8.GetBytes("{\"ref_channel\":\"playpage\"}"),
            key,
            iv);
        var stream = MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.SetPlaySourceCommand,
            0x0004,
            safetyData);

        var result = MiPlayCapturedCommandStreamDecoder.Decode(stream);

        var frame = Assert.Single(result.Frames);
        Assert.Equal(MiPlayProtocolConstants.SetPlaySourceCommand, frame.Command);
        Assert.Equal(safetyData.Length, frame.PayloadLength);
        Assert.NotNull(frame.SafetyDataHeader);
        Assert.True(frame.SafetyDataHeader.IsEncrypted);
        Assert.True(frame.SafetyDataHeader.HasPaddingLengthField);
        Assert.True(frame.SafetyDataHeader.HasIntegrityValue);
        Assert.Equal(9, frame.SafetyDataHeader.HeaderLength);
        Assert.Equal(9, frame.SafetyDataHeader.PayloadOffset);
        Assert.Equal(safetyData.Length - 9, frame.SafetyDataHeader.PayloadLength);
        Assert.True(frame.SafetyDataHeader.PayloadLength % 16 == 0);
        Assert.NotEmpty(frame.PayloadSha256Hex);
    }

    [Fact]
    public void TryParseHexDumpAcceptsCommonExportSeparatorsAndPrefixes()
    {
        var ok = MiPlayCapturedCommandStreamDecoder.TryParseHexDump(
            "0x24 00:1e-00_04, 00 00 00 00",
            out var bytes,
            out var error);

        Assert.True(ok, error);
        Assert.Null(error);
        Assert.Equal(
            [0x24, 0x00, 0x1e, 0x00, 0x04, 0x00, 0x00, 0x00, 0x00],
            bytes);
    }

    [Fact]
    public void DecodeStopsAtIncompleteTrailingFrameWithoutInventingPayload()
    {
        var frame = MiPlayCommandFrameCodec.Encode(
            MiPlayProtocolConstants.GetDeviceInfoCommand,
            0x0004,
            Encoding.ASCII.GetBytes("payload"));
        var truncated = frame[..^2];

        var result = MiPlayCapturedCommandStreamDecoder.Decode(truncated);

        Assert.Empty(result.Frames);
        Assert.True(result.EndsWithIncompleteFrame);
        var issue = Assert.Single(result.Issues);
        Assert.Equal(0, issue.Offset);
        Assert.Contains("Incomplete command frame", issue.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void TryParseHexDumpRejectsOddOrNonHexInput()
    {
        Assert.False(MiPlayCapturedCommandStreamDecoder.TryParseHexDump("24 0", out _, out var oddError));
        Assert.Contains("odd", oddError, StringComparison.OrdinalIgnoreCase);

        Assert.False(MiPlayCapturedCommandStreamDecoder.TryParseHexDump("24 zz", out _, out var invalidError));
        Assert.Contains("Invalid hex character", invalidError, StringComparison.Ordinal);
    }
}
