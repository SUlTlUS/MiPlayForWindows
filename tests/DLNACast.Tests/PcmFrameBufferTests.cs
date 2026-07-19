using DLNACast.Core.Audio;

namespace DLNACast.Tests;

public sealed class PcmFrameBufferTests
{
    [Fact]
    public async Task DropsOldestWholeFrameWhenCapacityIsReached()
    {
        await using var buffer = new PcmFrameBuffer(40);
        buffer.Write(Filled(1));
        buffer.Write(Filled(2));
        buffer.Write(Filled(3));

        var first = await buffer.ReadFrameOrSilenceAsync(CancellationToken.None);
        var second = await buffer.ReadFrameOrSilenceAsync(CancellationToken.None);

        Assert.Equal((byte)2, first[0]);
        Assert.Equal((byte)3, second[0]);
        Assert.Equal(1, buffer.Overruns);
    }

    [Fact]
    public async Task ProducesClockedSilenceWhenNoCapturePacketArrives()
    {
        await using var buffer = new PcmFrameBuffer();
        var frame = await buffer.ReadFrameOrSilenceAsync(CancellationToken.None);

        Assert.Equal(PcmFrameBuffer.BytesPerFrame, frame.Length);
        Assert.All(frame, value => Assert.Equal(0, value));
        Assert.Equal(1, buffer.Underruns);
    }

    [Fact]
    public async Task DefaultBufferNeverExceedsOneHundredMilliseconds()
    {
        await using var buffer = new PcmFrameBuffer();
        for (byte value = 1; value <= 8; value++) buffer.Write(Filled(value));

        Assert.Equal(100, buffer.BufferedMilliseconds);
        Assert.Equal((byte)4, (await buffer.ReadFrameOrSilenceAsync(CancellationToken.None))[0]);
    }

    [Fact]
    public async Task TrimToLatestRemovesPreconnectionBacklog()
    {
        await using var buffer = new PcmFrameBuffer();
        buffer.Write(Filled(1));
        buffer.Write(Filled(2));
        buffer.Write(Filled(3));

        var removed = buffer.TrimToLatest();

        Assert.Equal(2, removed);
        Assert.Equal(20, buffer.BufferedMilliseconds);
        Assert.Equal((byte)3, (await buffer.ReadFrameOrSilenceAsync(CancellationToken.None))[0]);
    }

    [Fact]
    public async Task ConsumerUsesTwentyMillisecondCadenceInsteadOfBurstDraining()
    {
        await using var buffer = new PcmFrameBuffer();
        buffer.Write(Filled(1));
        buffer.Write(Filled(2));
        buffer.Write(Filled(3));
        await buffer.PrepareForPlaybackAsync(cancellationToken: CancellationToken.None);

        var started = System.Diagnostics.Stopwatch.StartNew();
        await buffer.ReadFrameOrSilenceAsync(CancellationToken.None);
        await buffer.ReadFrameOrSilenceAsync(CancellationToken.None);
        await buffer.ReadFrameOrSilenceAsync(CancellationToken.None);

        Assert.True(started.ElapsedMilliseconds >= 30, $"Frames drained in {started.ElapsedMilliseconds} ms");
        Assert.Equal(0, buffer.Underruns);
    }

    private static byte[] Filled(byte value) => Enumerable.Repeat(value, PcmFrameBuffer.BytesPerFrame).ToArray();
}
