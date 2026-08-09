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
    public async Task ResetStatisticsKeepsBufferedAudioForPlayback()
    {
        await using var buffer = new PcmFrameBuffer(40);
        buffer.Write(Filled(1));
        buffer.Write(Filled(2));
        buffer.Write(Filled(3));
        await buffer.ReadFrameOrSilenceAsync(CancellationToken.None);
        await buffer.ReadFrameOrSilenceAsync(CancellationToken.None);
        await buffer.ReadFrameOrSilenceAsync(CancellationToken.None);

        Assert.Equal(1, buffer.Overruns);
        Assert.Equal(1, buffer.Underruns);
        buffer.Write(Filled(4));

        buffer.ResetStatistics();

        Assert.Equal(0, buffer.Overruns);
        Assert.Equal(0, buffer.Underruns);
        Assert.Equal(20, buffer.BufferedMilliseconds);
        Assert.Equal((byte)4, (await buffer.ReadFrameOrSilenceAsync(CancellationToken.None))[0]);
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

    [Theory]
    [InlineData(AudioChannelRoute.LeftAsMono, (short)1200)]
    [InlineData(AudioChannelRoute.RightAsMono, (short)-2300)]
    public async Task RoutesOneStereoChannelToBothOutputChannels(
        AudioChannelRoute route,
        short expectedSample)
    {
        await using var buffer = new PcmFrameBuffer(route);
        var input = new byte[PcmFrameBuffer.BytesPerFrame];
        for (var offset = 0; offset < input.Length; offset += 4)
        {
            BitConverter.TryWriteBytes(input.AsSpan(offset, 2), (short)1200);
            BitConverter.TryWriteBytes(input.AsSpan(offset + 2, 2), (short)-2300);
        }

        buffer.Write(input);
        var output = await buffer.ReadFrameOrSilenceAsync(CancellationToken.None);

        for (var offset = 0; offset < output.Length; offset += 4)
        {
            Assert.Equal(expectedSample, BitConverter.ToInt16(output, offset));
            Assert.Equal(expectedSample, BitConverter.ToInt16(output, offset + 2));
        }
    }

    [Fact]
    public void RejectsUnknownChannelRoute()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PcmFrameBuffer((AudioChannelRoute)99));
    }

    private static byte[] Filled(byte value) => [.. Enumerable.Repeat(value, PcmFrameBuffer.BytesPerFrame)];
}
