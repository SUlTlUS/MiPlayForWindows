using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayOrderedAccessUnitBufferTests
{
    [Fact]
    public async Task PreservesEveryAccessUnitInOriginalOrder()
    {
        var buffer = new MiPlayOrderedAccessUnitBuffer(capacity: 4);
        var units = Enumerable.Range(1, 4)
            .Select(value => new MiPlayEncodedAccessUnit(
                new byte[] { (byte)value },
                (ulong)value * 1_000))
            .ToArray();

        foreach (var unit in units)
        {
            await buffer.WriteAsync(unit);
        }

        foreach (var unit in units)
        {
            Assert.Equal(unit, await buffer.ReadAsync());
        }
        Assert.Equal(0, buffer.StarvationCount);
    }

    [Fact]
    public async Task FullBufferBackpressuresInsteadOfDroppingStartupAudio()
    {
        var buffer = new MiPlayOrderedAccessUnitBuffer(capacity: 2);
        var first = new MiPlayEncodedAccessUnit(new byte[] { 1 }, 1_000);
        var second = new MiPlayEncodedAccessUnit(new byte[] { 2 }, 2_000);
        var third = new MiPlayEncodedAccessUnit(new byte[] { 3 }, 3_000);

        await buffer.WriteAsync(first);
        await buffer.WriteAsync(second);
        var blockedWrite = buffer.WriteAsync(third).AsTask();
        Assert.False(blockedWrite.IsCompleted);

        Assert.Equal(first, await buffer.ReadAsync());
        await blockedWrite;
        Assert.Equal(second, await buffer.ReadAsync());
        Assert.Equal(third, await buffer.ReadAsync());
    }

    [Fact]
    public async Task EncoderFailureReachesWaitingSender()
    {
        var buffer = new MiPlayOrderedAccessUnitBuffer();
        var failure = new IOException("encoder failed");
        var pendingRead = buffer.ReadAsync().AsTask();

        buffer.Complete(failure);

        var observed = await Assert.ThrowsAsync<IOException>(() => pendingRead);
        Assert.Same(failure, observed);
        Assert.Equal(1, buffer.StarvationCount);
    }

    [Fact]
    public void RejectsInvalidCapacity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new MiPlayOrderedAccessUnitBuffer(capacity: 0));
    }
}
