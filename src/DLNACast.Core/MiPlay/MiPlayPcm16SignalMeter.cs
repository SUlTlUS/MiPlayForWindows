using System.Buffers.Binary;

namespace DLNACast.Core.MiPlay;

public sealed record MiPlayPcm16SignalSnapshot(
    long SampleCount,
    long NonZeroSampleCount,
    int PeakAbsoluteSample,
    double PeakNormalized,
    double RmsNormalized,
    double RmsDecibelsFullScale)
{
    public bool ContainsAudibleSignal =>
        SampleCount > 0 && PeakNormalized >= 0.001 && RmsNormalized >= 0.0001;
}

/// <summary>
/// Pure signed-16 little-endian PCM meter used to distinguish a silent Windows
/// loopback capture from a receiver/protocol failure. It does not retain PCM.
/// </summary>
public sealed class MiPlayPcm16SignalMeter
{
    private readonly Lock gate = new();
    private long sampleCount;
    private long nonZeroSampleCount;
    private int peakAbsoluteSample;
    private double sumOfSquares;

    public MiPlayPcm16SignalSnapshot Add(ReadOnlySpan<byte> pcm)
    {
        if (pcm.Length == 0 || pcm.Length % sizeof(short) != 0)
        {
            throw new ArgumentException(
                "PCM must contain complete signed-16 little-endian samples.",
                nameof(pcm));
        }

        long addedSampleCount = 0;
        long addedNonZeroSampleCount = 0;
        var addedPeakAbsoluteSample = 0;
        double addedSumOfSquares = 0;
        for (var offset = 0; offset < pcm.Length; offset += sizeof(short))
        {
            var sample = BinaryPrimitives.ReadInt16LittleEndian(pcm.Slice(offset, sizeof(short)));
            var absolute = sample == short.MinValue ? 32_768 : Math.Abs(sample);
            addedSampleCount++;
            if (sample != 0)
            {
                addedNonZeroSampleCount++;
            }
            addedPeakAbsoluteSample = Math.Max(addedPeakAbsoluteSample, absolute);
            var normalized = sample / 32_768d;
            addedSumOfSquares += normalized * normalized;
        }

        lock (gate)
        {
            sampleCount += addedSampleCount;
            nonZeroSampleCount += addedNonZeroSampleCount;
            peakAbsoluteSample = Math.Max(peakAbsoluteSample, addedPeakAbsoluteSample);
            sumOfSquares += addedSumOfSquares;
        }

        return CreateSnapshot(
            addedSampleCount,
            addedNonZeroSampleCount,
            addedPeakAbsoluteSample,
            addedSumOfSquares);
    }

    public MiPlayPcm16SignalSnapshot Snapshot()
    {
        lock (gate)
        {
            return CreateSnapshot();
        }
    }

    public MiPlayPcm16SignalSnapshot SnapshotAndReset()
    {
        lock (gate)
        {
            var snapshot = CreateSnapshot();
            sampleCount = 0;
            nonZeroSampleCount = 0;
            peakAbsoluteSample = 0;
            sumOfSquares = 0;
            return snapshot;
        }
    }

    private MiPlayPcm16SignalSnapshot CreateSnapshot() =>
        CreateSnapshot(sampleCount, nonZeroSampleCount, peakAbsoluteSample, sumOfSquares);

    private static MiPlayPcm16SignalSnapshot CreateSnapshot(
        long samples,
        long nonZeroSamples,
        int peakSample,
        double squares)
    {
        var rms = samples == 0 ? 0 : Math.Sqrt(squares / samples);
        return new MiPlayPcm16SignalSnapshot(
            samples,
            nonZeroSamples,
            peakSample,
            peakSample / 32_768d,
            rms,
            rms == 0 ? double.NegativeInfinity : 20 * Math.Log10(rms));
    }
}
