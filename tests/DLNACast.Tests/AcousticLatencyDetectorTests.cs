using DLNACast.Core.Audio;

namespace DLNACast.Tests;

public sealed class AcousticLatencyDetectorTests
{
    [Fact]
    public void FortyEightKilohertzMiPlayProbeMatchesMicrophoneReferenceDuration()
    {
        var frames = AcousticLatencyProbeSignal.CreateStereoFrames(PcmFrameFormat.MiPlayAac);
        var source = new float[frames.Count * PcmFrameFormat.MiPlayAac.SamplesPerFrame];
        var sourceOffset = 0;
        foreach (var frame in frames)
        {
            for (var byteOffset = 0; byteOffset < frame.Length; byteOffset += 4)
            {
                source[sourceOffset++] = BitConverter.ToInt16(frame, byteOffset) / 32768f;
            }
        }

        var microphone = Resample(
            source,
            PcmFrameFormat.MiPlayAac.SampleRate,
            PcmFrameBuffer.SampleRate);
        var expectedProbeStart = checked((int)Math.Round(
            AcousticLatencyProbeSignal.ProbeStartFrame *
            PcmFrameFormat.MiPlayAac.SamplesPerFrame *
            PcmFrameBuffer.SampleRate /
            (double)PcmFrameFormat.MiPlayAac.SampleRate));

        var detection = AcousticLatencyDetector.Find(
            microphone,
            AcousticLatencyProbeSignal.MonoSamples,
            expectedProbeStart,
            maximumDelaySamples: PcmFrameBuffer.SampleRate / 10);

        Assert.NotNull(detection);
        Assert.False(detection.UsedWakeToneFallback);
        Assert.InRange(detection.SampleIndex, expectedProbeStart - 8, expectedProbeStart + 8);
        Assert.True(detection.Confidence > 0.8, $"Correlation was {detection.Confidence:F3}");
    }

    [Fact]
    public void FindsProbeArrivalInsideNoisyMicrophoneSamples()
    {
        const int expectedStart = 5_000;
        const int delay = 13_230;
        var random = new Random(42);
        var captured = new float[expectedStart + delay + AcousticLatencyProbeSignal.MonoSamples.Length + 2_000];
        for (var index = 0; index < captured.Length; index++)
        {
            captured[index] = (float)((random.NextDouble() - 0.5) * 0.025);
        }
        for (var index = 0; index < AcousticLatencyProbeSignal.MonoSamples.Length; index++)
        {
            captured[expectedStart + delay + index] +=
                AcousticLatencyProbeSignal.MonoSamples[index] * 0.45f;
        }

        var detection = AcousticLatencyDetector.Find(
            captured,
            AcousticLatencyProbeSignal.MonoSamples,
            expectedStart,
            maximumDelaySamples: 20_000);

        Assert.NotNull(detection);
        Assert.InRange(detection.SampleIndex, expectedStart + delay - 4, expectedStart + delay + 4);
        Assert.True(detection.Confidence > 0.7, $"Correlation was {detection.Confidence:F3}");
    }

    [Fact]
    public void RejectsBackgroundNoiseWithoutProbe()
    {
        var random = new Random(7);
        var captured = Enumerable.Range(0, 30_000)
            .Select(_ => (float)((random.NextDouble() - 0.5) * 0.04))
            .ToArray();

        var detection = AcousticLatencyDetector.Find(
            captured,
            AcousticLatencyProbeSignal.MonoSamples,
            expectedStartIndex: 2_000,
            maximumDelaySamples: 20_000);

        Assert.Null(detection);
    }

    [Fact]
    public void FindsAttenuatedWakeToneWhenRoomDistortionHidesTheChirp()
    {
        const int expectedProbeStart = 36_000;
        const int delay = 52_800;
        var wakeStart = expectedProbeStart - AcousticLatencyProbeSignal.LeadInSampleCount + delay;
        var captured = new float[wakeStart + AcousticLatencyProbeSignal.LeadInSampleCount + 8_000];
        var random = new Random(81);
        for (var index = 0; index < captured.Length; index++)
        {
            captured[index] = (float)((random.NextDouble() - 0.5) * 0.003);
        }

        var wakeSamples = AcousticLatencyProbeSignal.WakeupFrameCount *
                          PcmFrameBuffer.SampleRate * PcmFrameBuffer.FrameMilliseconds / 1000;
        for (var index = 0; index < wakeSamples; index++)
        {
            var time = index / (double)PcmFrameBuffer.SampleRate;
            var fade = Math.Min(1, Math.Min(index, wakeSamples - index - 1) /
                                   (PcmFrameBuffer.SampleRate * 0.015));
            captured[wakeStart + index] += (float)(
                0.018 * fade * Math.Sin(
                    2 * Math.PI * AcousticLatencyProbeSignal.WakeupFrequencyHz * time + 0.73));
        }

        var detection = AcousticLatencyDetector.Find(
            captured,
            AcousticLatencyProbeSignal.MonoSamples,
            expectedProbeStart,
            maximumDelaySamples: 96_000);

        Assert.NotNull(detection);
        Assert.InRange(
            detection.SampleIndex,
            expectedProbeStart + delay - 960,
            expectedProbeStart + delay + 960);
        Assert.True(detection.Confidence >= 0.28, $"Confidence was {detection.Confidence:F3}");
    }

    [Fact]
    public void RejectsContinuousWakeFrequencyAlreadyPresentInTheBaseline()
    {
        const int expectedProbeStart = 48_000;
        var captured = new float[180_000];
        for (var index = 0; index < captured.Length; index++)
        {
            var time = index / (double)PcmFrameBuffer.SampleRate;
            captured[index] = (float)(0.012 * Math.Sin(
                2 * Math.PI * AcousticLatencyProbeSignal.WakeupFrequencyHz * time));
        }

        var detection = AcousticLatencyDetector.Find(
            captured,
            AcousticLatencyProbeSignal.MonoSamples,
            expectedProbeStart,
            maximumDelaySamples: 96_000);

        Assert.Null(detection);
    }

    private static float[] Resample(
        IReadOnlyList<float> source,
        int sourceSampleRate,
        int destinationSampleRate)
    {
        var destinationLength = checked((int)Math.Floor(
            source.Count * destinationSampleRate / (double)sourceSampleRate));
        var destination = new float[destinationLength];
        for (var index = 0; index < destination.Length; index++)
        {
            var sourcePosition = index * sourceSampleRate / (double)destinationSampleRate;
            var first = Math.Min((int)Math.Floor(sourcePosition), source.Count - 1);
            var second = Math.Min(first + 1, source.Count - 1);
            var fraction = sourcePosition - first;
            destination[index] = (float)(source[first] * (1 - fraction) + source[second] * fraction);
        }
        return destination;
    }
}
