using System.Buffers.Binary;

namespace DLNACast.Core.Audio;

internal static class AcousticLatencyProbeSignal
{
    // MiPlay pauses the receiver after sustained source silence.  A short probe
    // can otherwise be encoded completely while the asynchronous resume control
    // exchange is still in flight.  Keep an audible, spectrally distinct lead-in
    // before the actual correlation probe and timestamp only the probe itself.
    internal const int WakeupFrameCount = 20;
    internal const int SeparationFrameCount = 2;
    internal const int ProbeFrameCount = 9;
    internal const int ProbeStartFrame = WakeupFrameCount + SeparationFrameCount;
    internal const int FrameCount = ProbeStartFrame + ProbeFrameCount;
    internal const int WakeupFrequencyHz = 880;
    private const double WakeupDurationSeconds =
        WakeupFrameCount * PcmFrameBuffer.FrameMilliseconds / 1_000d;
    private const double LeadInDurationSeconds =
        ProbeStartFrame * PcmFrameBuffer.FrameMilliseconds / 1_000d;
    private const double ProbeDurationSeconds =
        ProbeFrameCount * PcmFrameBuffer.FrameMilliseconds / 1_000d;
    internal const int LeadInSampleCount =
        ProbeStartFrame * PcmFrameBuffer.SampleRate * PcmFrameBuffer.FrameMilliseconds / 1000;
    internal static readonly float[] MonoSamples = CreateProbeSamples(
        PcmFrameBuffer.SampleRate,
        ProbeDurationSeconds);

    internal static IReadOnlyList<byte[]> CreateStereoFrames(PcmFrameFormat? format = null)
    {
        format ??= PcmFrameFormat.Default;
        var frames = new byte[FrameCount][];
        var probeStartSeconds =
            ProbeStartFrame * format.SamplesPerFrame / (double)format.SampleRate;
        var wakeupStartSeconds = Math.Max(0, probeStartSeconds - LeadInDurationSeconds);
        for (var frameIndex = 0; frameIndex < frames.Length; frameIndex++)
        {
            var frame = new byte[format.BytesPerFrame];
            for (var sampleIndex = 0; sampleIndex < format.SamplesPerFrame; sampleIndex++)
            {
                var value = CreateSequenceSample(
                    frameIndex,
                    sampleIndex,
                    format.SampleRate,
                    format.SamplesPerFrame,
                    wakeupStartSeconds,
                    probeStartSeconds);
                var pcmValue = (short)Math.Round(value * short.MaxValue);
                var byteOffset = sampleIndex * format.Channels * format.BytesPerSample;
                BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(byteOffset, 2), pcmValue);
                BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(byteOffset + 2, 2), pcmValue);
            }
            frames[frameIndex] = frame;
        }
        return frames;
    }

    private static float CreateSequenceSample(
        int frameIndex,
        int sampleIndex,
        int sampleRate,
        int samplesPerFrame,
        double wakeupStartSeconds,
        double probeStartSeconds)
    {
        var streamSampleIndex = frameIndex * samplesPerFrame + sampleIndex;
        var streamTime = streamSampleIndex / (double)sampleRate;
        var wakeupTime = streamTime - wakeupStartSeconds;
        if (wakeupTime >= 0 && wakeupTime < WakeupDurationSeconds)
        {
            const double fadeSeconds = 0.015;
            var fadeIn = Math.Min(1, wakeupTime / fadeSeconds);
            var fadeOut = Math.Min(1, (WakeupDurationSeconds - wakeupTime) / fadeSeconds);
            var envelope = Math.Min(fadeIn, fadeOut);
            return (float)(
                0.32 * envelope * Math.Sin(2 * Math.PI * WakeupFrequencyHz * wakeupTime));
        }

        var probeTime = streamTime - probeStartSeconds;
        if (probeTime < 0 || probeTime >= ProbeDurationSeconds)
        {
            return 0;
        }

        return CreateProbeSample(probeTime, ProbeDurationSeconds);
    }

    private static float[] CreateProbeSamples(int sampleRate, double durationSeconds)
    {
        var sampleCount = checked((int)Math.Round(durationSeconds * sampleRate));
        var samples = new float[sampleCount];
        for (var index = 0; index < samples.Length; index++)
        {
            samples[index] = CreateProbeSample(index / (double)sampleRate, durationSeconds);
        }
        return samples;
    }

    private static float CreateProbeSample(double time, double durationSeconds)
    {
        const double startFrequency = 1_350;
        const double endFrequency = 4_350;
        const double amplitude = 0.68;
        const double fadeSeconds = 0.012;
        var sweepRate = Math.Log(endFrequency / startFrequency) / durationSeconds;
        var phase = 2 * Math.PI * startFrequency *
                    (Math.Exp(sweepRate * time) - 1) / sweepRate;
        var fadeIn = Math.Min(1, time / fadeSeconds);
        var fadeOut = Math.Min(1, (durationSeconds - time) / fadeSeconds);
        var envelope = Math.Sin(Math.PI * Math.Min(fadeIn, fadeOut) / 2);
        envelope *= envelope;
        return (float)(amplitude * envelope * Math.Sin(phase));
    }
}
