using System.Buffers.Binary;
using DLNACast.Core.MiPlay;

namespace DLNACast.Tests;

public sealed class MiPlayPcm16SignalMeterTests
{
    [Fact]
    public void SeparatesSilenceFromBoundedNonzeroPcm()
    {
        var silence = new MiPlayPcm16SignalMeter();
        silence.Add(new byte[8]);
        var silent = silence.Snapshot();
        Assert.Equal(4, silent.SampleCount);
        Assert.Equal(0, silent.NonZeroSampleCount);
        Assert.Equal(0, silent.PeakAbsoluteSample);
        Assert.Equal(0, silent.RmsNormalized);
        Assert.Equal(double.NegativeInfinity, silent.RmsDecibelsFullScale);
        Assert.False(silent.ContainsAudibleSignal);

        var meter = new MiPlayPcm16SignalMeter();
        var samples = new byte[8];
        BinaryPrimitives.WriteInt16LittleEndian(samples.AsSpan(0, 2), 16_384);
        BinaryPrimitives.WriteInt16LittleEndian(samples.AsSpan(2, 2), -16_384);
        BinaryPrimitives.WriteInt16LittleEndian(samples.AsSpan(4, 2), short.MinValue);
        BinaryPrimitives.WriteInt16LittleEndian(samples.AsSpan(6, 2), 0);
        meter.Add(samples);

        var snapshot = meter.Snapshot();
        Assert.Equal(4, snapshot.SampleCount);
        Assert.Equal(3, snapshot.NonZeroSampleCount);
        Assert.Equal(32_768, snapshot.PeakAbsoluteSample);
        Assert.Equal(1, snapshot.PeakNormalized);
        Assert.True(snapshot.RmsNormalized > 0.6);
        Assert.True(snapshot.ContainsAudibleSignal);
    }

    [Fact]
    public void RejectsPartialOrEmptySamples()
    {
        var meter = new MiPlayPcm16SignalMeter();

        Assert.Throws<ArgumentException>(() => meter.Add([]));
        Assert.Throws<ArgumentException>(() => meter.Add([1]));
    }
}
