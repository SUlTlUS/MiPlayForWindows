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
    public async Task MiPlayFormatUsesOneCompleteAacInputFrame()
    {
        await using var buffer = new PcmFrameBuffer(
            AudioChannelRoute.Stereo,
            PcmFrameFormat.MiPlayAac);
        var pcm = Enumerable.Repeat((byte)0x35, 4_096).ToArray();

        buffer.Write(pcm);
        var frame = await buffer.ReadFrameOrSilenceAsync(CancellationToken.None);

        Assert.Equal(48_000, buffer.SampleRateHz);
        Assert.Equal(1_024, buffer.SamplesPerFrame);
        Assert.Equal(4_096, buffer.FrameByteCount);
        Assert.Equal(pcm, frame);
    }

    [Fact]
    public async Task MiPlayAssemblerSplitsCapturePacketsAtFourThousandNinetySixBytes()
    {
        await using var buffer = new PcmFrameBuffer(
            AudioChannelRoute.Stereo,
            PcmFrameFormat.MiPlayAac);
        var assembler = new PcmFrameAssembler(buffer);
        var pcm = Enumerable.Range(0, 8_192).Select(index => (byte)index).ToArray();

        assembler.Push(pcm.AsSpan(0, 3_000));
        assembler.Push(pcm.AsSpan(3_000));

        Assert.Equal(43, buffer.BufferedMilliseconds);
        Assert.Equal(pcm[..4_096], await buffer.ReadFrameOrSilenceAsync(CancellationToken.None));
        Assert.Equal(pcm[4_096..], await buffer.ReadFrameOrSilenceAsync(CancellationToken.None));
    }

    [Fact]
    public async Task MiPlayLatencyProbeUsesFortyEightKilohertzFrames()
    {
        await using var buffer = new PcmFrameBuffer(
            AudioChannelRoute.Stereo,
            PcmFrameFormat.MiPlayAac);
        buffer.ScheduleLatencyProbe();

        var first = await buffer.ReadFrameOrSilenceAsync(CancellationToken.None);
        var second = await buffer.ReadFrameOrSilenceAsync(CancellationToken.None);

        Assert.Equal(4_096, first.Length);
        Assert.Equal(4_096, second.Length);
        Assert.Contains(second, value => value != 0);
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

    [Fact]
    public async Task ScheduledLatencyProbeWakesReceiverBeforeTimestampedProbeFrames()
    {
        await using var buffer = new PcmFrameBuffer();
        var playback = buffer.ScheduleLatencyProbe();

        var probeFrames = new List<byte[]>();
        for (var index = 0; index < AcousticLatencyProbeSignal.ProbeStartFrame; index++)
        {
            probeFrames.Add(await buffer.ReadFrameOrSilenceAsync(CancellationToken.None));
            Assert.False(playback.StartedAt.IsCompleted);
        }

        probeFrames.Add(await buffer.ReadFrameOrSilenceAsync(CancellationToken.None));
        Assert.True(playback.StartedAt.IsCompletedSuccessfully);
        for (var index = AcousticLatencyProbeSignal.ProbeStartFrame + 1;
             index < AcousticLatencyProbeSignal.FrameCount;
             index++)
        {
            probeFrames.Add(await buffer.ReadFrameOrSilenceAsync(CancellationToken.None));
        }

        Assert.Contains(
            probeFrames.Take(AcousticLatencyProbeSignal.WakeupFrameCount).SelectMany(frame => frame),
            value => value != 0);
        Assert.All(
            probeFrames
                .Skip(AcousticLatencyProbeSignal.WakeupFrameCount)
                .Take(AcousticLatencyProbeSignal.SeparationFrameCount),
            frame => Assert.All(frame, value => Assert.Equal(0, value)));
        Assert.Contains(
            probeFrames.Skip(AcousticLatencyProbeSignal.ProbeStartFrame).SelectMany(frame => frame),
            value => value != 0);
        buffer.Write(Filled(0x2A));
        Assert.Equal((byte)0x2A, (await buffer.ReadFrameOrSilenceAsync(CancellationToken.None))[0]);
    }

    [Fact]
    public async Task RejectsOverlappingLatencyProbes()
    {
        await using var buffer = new PcmFrameBuffer();
        buffer.ScheduleLatencyProbe();

        Assert.Throws<InvalidOperationException>(() => buffer.ScheduleLatencyProbe());
    }

    [Fact]
    public async Task SequencePreservingBufferBackpressuresInsteadOfDroppingFrames()
    {
        await using var buffer = new PcmFrameBuffer(
            AudioChannelRoute.Stereo,
            PcmFrameFormat.MiPlayAac,
            maximumMilliseconds: 22,
            preserveFrameSequence: true);
        var first = new PcmFrameRead(
            Enumerable.Repeat((byte)0x11, buffer.FrameByteCount).ToArray(),
            1_000_000);
        var second = new PcmFrameRead(
            Enumerable.Repeat((byte)0x22, buffer.FrameByteCount).ToArray(),
            1_021_333,
            ReanchorPresentationTimeline: true);

        await buffer.WritePreservingSequenceAsync(first, CancellationToken.None);
        var blockedWrite = buffer.WritePreservingSequenceAsync(
            second,
            CancellationToken.None).AsTask();
        Assert.False(blockedWrite.IsCompleted);

        var firstRead = await buffer.ReadFrameWithTimingOrSilenceAsync(CancellationToken.None);
        await blockedWrite;
        var secondRead = await buffer.ReadFrameWithTimingOrSilenceAsync(CancellationToken.None);

        Assert.Equal(0x11, firstRead.Bytes[0]);
        Assert.Equal(0x22, secondRead.Bytes[0]);
        Assert.True(secondRead.ReanchorPresentationTimeline);
        Assert.Equal(0, buffer.Overruns);
    }

    private static byte[] Filled(byte value) => [.. Enumerable.Repeat(value, PcmFrameBuffer.BytesPerFrame)];
}
