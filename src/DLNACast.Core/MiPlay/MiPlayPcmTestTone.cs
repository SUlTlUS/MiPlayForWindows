using System.Buffers.Binary;

namespace DLNACast.Core.MiPlay;

/// <summary>
/// Deterministic, low-amplitude PCM tone for a short audible transport test.
/// It produces no network output and is not used as application media.
/// </summary>
public static class MiPlayPcmTestTone
{
    public const double DefaultFrequencyHz = 440;
    public const double DefaultAmplitude = 0.12;

    public static byte[] CreateFrame(
        long firstSampleIndex,
        int sampleCount,
        int sampleRate = MiPlayFfmpegAacEncoder.InputSampleRate,
        double frequencyHz = DefaultFrequencyHz,
        double amplitude = DefaultAmplitude)
    {
        if (firstSampleIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(firstSampleIndex));
        }
        if (sampleCount <= 0 || sampleRate <= 0 || frequencyHz <= 0 ||
            amplitude is <= 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleCount));
        }

        var pcm = new byte[sampleCount * MiPlayFfmpegAacEncoder.InputChannels * sizeof(short)];
        var phaseStep = 2 * Math.PI * frequencyHz / sampleRate;
        for (var sample = 0; sample < sampleCount; sample++)
        {
            var value = (short)Math.Round(
                Math.Sin((firstSampleIndex + sample) * phaseStep) * short.MaxValue * amplitude);
            var offset = sample * MiPlayFfmpegAacEncoder.InputChannels * sizeof(short);
            BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(offset, sizeof(short)), value);
            BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(offset + sizeof(short), sizeof(short)), value);
        }
        return pcm;
    }
}
