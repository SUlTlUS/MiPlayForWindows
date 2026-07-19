using System.Buffers.Binary;
using DLNACast.Core.Audio;

namespace DLNACast.Tests;

public sealed class WaveStreamHeaderTests
{
    [Fact]
    public void CreatesIndefiniteStereoPcmHeader()
    {
        var header = WaveStreamHeader.CreateIndefinitePcmHeader();

        Assert.Equal(44, header.Length);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(header, 0, 4));
        Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(header, 8, 4));
        Assert.Equal((ushort)2, BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(22, 2)));
        Assert.Equal((uint)44_100, BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(24, 4)));
        Assert.Equal((ushort)16, BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(34, 2)));
        Assert.Equal(uint.MaxValue, BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(40, 4)));
    }
}

