using System.Buffers.Binary;
using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayPcmTestToneTests
{
    [Fact]
    public void GeneratesContinuousLowAmplitudeStereoSigned16Pcm()
    {
        var first = MiPlayPcmTestTone.CreateFrame(0, 441);
        var second = MiPlayPcmTestTone.CreateFrame(441, 441);

        Assert.Equal(441 * 4, first.Length);
        Assert.Equal(441 * 4, second.Length);
        Assert.Equal((short)0, BinaryPrimitives.ReadInt16LittleEndian(first));

        var samples = first
            .Chunk(4)
            .Select(bytes => (
                Left: BinaryPrimitives.ReadInt16LittleEndian(bytes),
                Right: BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(2))))
            .ToArray();
        Assert.All(samples, sample => Assert.Equal(sample.Left, sample.Right));
        Assert.InRange(samples.Max(sample => Math.Abs((int)sample.Left)), 3_900, 3_940);

        var combined = first.Concat(second).ToArray();
        var positiveCrossings = 0;
        short previous = BinaryPrimitives.ReadInt16LittleEndian(combined);
        for (var offset = 4; offset < combined.Length; offset += 4)
        {
            var current = BinaryPrimitives.ReadInt16LittleEndian(combined.AsSpan(offset));
            if (previous <= 0 && current > 0)
            {
                positiveCrossings++;
            }
            previous = current;
        }
        Assert.InRange(positiveCrossings, 8, 9);
    }

    [Fact]
    public void RejectsInvalidToneBounds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MiPlayPcmTestTone.CreateFrame(-1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => MiPlayPcmTestTone.CreateFrame(0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => MiPlayPcmTestTone.CreateFrame(0, 1, amplitude: 1.1));
    }
}
