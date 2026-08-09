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

    public void Add(ReadOnlySpan<byte> pcm)
    {
        if (pcm.Length == 0 || pcm.Length % sizeof(short) != 0)
        {
            throw new ArgumentException(
                "PCM must contain complete signed-16 little-endian samples.",
                nameof(pcm));
        }

        lock (gate)
        {
            for (var offset = 0; offset < pcm.Length; offset += sizeof(short))
            {
                var sample = BinaryPrimitives.ReadInt16LittleEndian(pcm.Slice(offset, sizeof(short)));
                var absolute = sample == short.MinValue ? 32_768 : Math.Abs(sample);
                sampleCount++;
                if (sample != 0)
                {
                    nonZeroSampleCount++;
                }
                peakAbsoluteSample = Math.Max(peakAbsoluteSample, absolute);
                var normalized = sample / 32_768d;
                sumOfSquares += normalized * normalized;
            }
        }
    }

    public MiPlayPcm16SignalSnapshot Snapshot()
    {
        lock (gate)
        {
            var rms = sampleCount == 0 ? 0 : Math.Sqrt(sumOfSquares / sampleCount);
            return new MiPlayPcm16SignalSnapshot(
                sampleCount,
                nonZeroSampleCount,
                peakAbsoluteSample,
                peakAbsoluteSample / 32_768d,
                rms,
                rms == 0 ? double.NegativeInfinity : 20 * Math.Log10(rms));
        }
    }
}
