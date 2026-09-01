using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayStartupAccessUnitBufferTests
{
    [Fact]
    public async Task HoldsAFiniteOrderedRunwayForTheCapturedStartupCadence()
    {
        var next = 0;
        var buffer = await MiPlayStartupAccessUnitBuffer.FillAsync(
            _ => ValueTask.FromResult(new MiPlayEncodedAccessUnit(
                [checked((byte)next)],
                checked((ulong)next++))),
            CancellationToken.None);

        Assert.Equal(MiPlayStartupAccessUnitBuffer.RequiredAccessUnitCount, next);
        Assert.Equal(MiPlayStartupAccessUnitBuffer.RequiredAccessUnitCount, buffer.Count);
        for (var expected = 0;
             expected < MiPlayStartupAccessUnitBuffer.RequiredAccessUnitCount;
             expected++)
        {
            Assert.True(buffer.TryRead(out var accessUnit));
            Assert.Equal(expected, accessUnit.Bytes[0]);
            Assert.Equal((ulong)expected, accessUnit.PresentationTimeMicroseconds);
        }
        Assert.False(buffer.TryRead(out _));
    }
}
