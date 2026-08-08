using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayAdtsStreamParserTests
{
    [Fact]
    public void ParsesSplitFramesAndNormalizesMpeg4IdToCapturedMpeg2Id()
    {
        var first = CreateMpeg4AccessUnit(64, 0x11);
        var second = CreateMpeg4AccessUnit(80, 0x22);
        var stream = first.Concat(second).ToArray();
        var parser = new MiPlayAdtsStreamParser();

        var prefix = parser.Push(stream.AsSpan(0, 5));
        Assert.Empty(prefix);
        Assert.Equal(5, parser.PendingByteCount);

        var completed = parser.Push(stream.AsSpan(5));
        Assert.Equal(2, completed.Count);
        Assert.Equal(0, parser.PendingByteCount);
        Assert.All(completed, frame => Assert.Equal(Convert.FromHexString("FFF94C80"), frame[..4]));
        Assert.Equal(Enumerable.Repeat((byte)0x11, 64), completed[0][7..]);
        Assert.Equal(Enumerable.Repeat((byte)0x22, 80), completed[1][7..]);
    }

    [Fact]
    public void RejectsUnsupportedSampleRateBeforePacketization()
    {
        var accessUnit = CreateMpeg4AccessUnit(64, 0);
        accessUnit[2] = 0x50; // AAC-LC, 44.1 kHz.

        Assert.Throws<ArgumentException>(() =>
            MiPlayAdtsStreamParser.NormalizeMpeg2AacLc48KhzStereo(accessUnit));
    }

    internal static byte[] CreateMpeg4AccessUnit(int payloadLength, byte fill)
    {
        var accessUnit = MiPlayAdtsHeader.Prepend(Enumerable.Repeat(fill, payloadLength).ToArray());
        accessUnit[1] = 0xf1;
        return accessUnit;
    }
}
