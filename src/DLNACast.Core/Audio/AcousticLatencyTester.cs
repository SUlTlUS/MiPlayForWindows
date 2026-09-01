using System.Collections.Concurrent;
using System.Diagnostics;
using NAudio.Wave;

namespace DLNACast.Core.Audio;

public interface IAcousticLatencyTester
{
    Task<AcousticLatencyMeasurement> MeasureAsync(
        Func<CancellationToken, Task<long>> emitProbeAsync,
        CancellationToken cancellationToken = default);
}

public sealed record AcousticLatencyMeasurement(
    TimeSpan Latency,
    double Confidence,
    bool UsedWakeToneFallback = false);

/// <summary>
/// Measures the acoustic round-trip delay from the cast PCM queue to the room
/// by listening for a short probe through the default Windows microphone.
/// </summary>
public sealed class WindowsAcousticLatencyTester : IAcousticLatencyTester
{
    private const int MicrophoneWarmupMilliseconds = 240;
    private const int CaptureAfterProbeMilliseconds = 7_000;
    private const int AnalysisIntervalMilliseconds = 250;

    public async Task<AcousticLatencyMeasurement> MeasureAsync(
        Func<CancellationToken, Task<long>> emitProbeAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(emitProbeAsync);

        var blocks = new ConcurrentQueue<CapturedMicrophoneBlock>();
        var firstData = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var microphone = new WaveInEvent
        {
            DeviceNumber = -1,
            WaveFormat = new WaveFormat(PcmFrameBuffer.SampleRate, 16, 1),
            BufferMilliseconds = PcmFrameBuffer.FrameMilliseconds,
            NumberOfBuffers = 4,
        };

        microphone.DataAvailable += OnDataAvailable;
        microphone.RecordingStopped += OnRecordingStopped;
        var isRecording = false;
        try
        {
            microphone.StartRecording();
            isRecording = true;
            await firstData.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken)
                .ConfigureAwait(false);
            await Task.Delay(MicrophoneWarmupMilliseconds, cancellationToken).ConfigureAwait(false);

            var probeStartedAt = await emitProbeAsync(cancellationToken).ConfigureAwait(false);
            AcousticLatencyMeasurement? measurement = null;
            AcousticLatencyDetectionDiagnostics diagnostics = AcousticLatencyDetectionDiagnostics.Empty;
            var analysisDeadline = Stopwatch.GetTimestamp() + (long)Math.Round(
                CaptureAfterProbeMilliseconds * Stopwatch.Frequency / 1000d);
            while (Stopwatch.GetTimestamp() < analysisDeadline)
            {
                await Task.Delay(AnalysisIntervalMilliseconds, cancellationToken).ConfigureAwait(false);
                measurement = TryAnalyze(blocks.ToArray(), probeStartedAt, out diagnostics);
                if (measurement is not null) break;
            }

            microphone.StopRecording();
            isRecording = false;
            var recordingError = await stopped.Task
                .WaitAsync(TimeSpan.FromSeconds(1), cancellationToken)
                .ConfigureAwait(false);
            if (recordingError is not null)
            {
                throw new InvalidOperationException("The microphone recording stopped unexpectedly.", recordingError);
            }

            return measurement ?? throw new InvalidOperationException(
                $"The microphone did not detect the latency test sound. " +
                $"Best correlation={diagnostics.BestCorrelation:F3}, " +
                $"wake coherence={diagnostics.BestWakeToneCoherence:F3}, " +
                $"wake amplitude={diagnostics.BestWakeToneAmplitude:F6}, " +
                $"capture peak={diagnostics.PeakAmplitude:F6}, " +
                $"capture RMS={diagnostics.Rms:F6}.");
        }
        finally
        {
            if (isRecording)
            {
                try { microphone.StopRecording(); }
                catch { }
            }
            microphone.DataAvailable -= OnDataAvailable;
            microphone.RecordingStopped -= OnRecordingStopped;
        }

        void OnDataAvailable(object? sender, WaveInEventArgs args)
        {
            if (args.BytesRecorded < sizeof(short)) return;

            var sampleCount = args.BytesRecorded / sizeof(short);
            var samples = new float[sampleCount];
            for (var index = 0; index < sampleCount; index++)
            {
                samples[index] = BitConverter.ToInt16(args.Buffer, index * sizeof(short)) / 32768f;
            }

            var receivedAt = Stopwatch.GetTimestamp();
            var durationTicks = (long)Math.Round(
                sampleCount * Stopwatch.Frequency / (double)PcmFrameBuffer.SampleRate);
            blocks.Enqueue(new CapturedMicrophoneBlock(receivedAt - durationTicks, samples));
            firstData.TrySetResult();
        }

        void OnRecordingStopped(object? sender, StoppedEventArgs args) =>
            stopped.TrySetResult(args.Exception);
    }

    private static AcousticLatencyMeasurement? TryAnalyze(
        IReadOnlyList<CapturedMicrophoneBlock> blocks,
        long probeStartedAt,
        out AcousticLatencyDetectionDiagnostics diagnostics)
    {
        diagnostics = AcousticLatencyDetectionDiagnostics.Empty;
        if (blocks.Count == 0)
        {
            return null;
        }

        var firstSampleAt = blocks[0].FirstSampleTimestamp;
        var samples = new float[blocks.Sum(block => block.Samples.Length)];
        var writeOffset = 0;
        foreach (var block in blocks)
        {
            block.Samples.CopyTo(samples, writeOffset);
            writeOffset += block.Samples.Length;
        }

        var expectedProbeIndex = (int)Math.Round(
            (probeStartedAt - firstSampleAt) * PcmFrameBuffer.SampleRate /
            (double)Stopwatch.Frequency);
        var detection = AcousticLatencyDetector.Find(
            samples,
            AcousticLatencyProbeSignal.MonoSamples,
            expectedProbeIndex,
            CaptureAfterProbeMilliseconds * PcmFrameBuffer.SampleRate / 1000,
            out diagnostics);
        if (detection is null)
        {
            return null;
        }

        var arrivalAt = firstSampleAt + (long)Math.Round(
            detection.SampleIndex * Stopwatch.Frequency / (double)PcmFrameBuffer.SampleRate);
        var latencyTicks = Math.Max(0, arrivalAt - probeStartedAt);
        return new AcousticLatencyMeasurement(
            TimeSpan.FromSeconds(latencyTicks / (double)Stopwatch.Frequency),
            detection.Confidence,
            detection.UsedWakeToneFallback);
    }

    private sealed record CapturedMicrophoneBlock(
        long FirstSampleTimestamp,
        float[] Samples);
}

internal static class AcousticLatencyDetector
{
    private const int DownsampleFactor = 4;
    private const double MinimumCorrelation = 0.12;
    private const int WakeToneWindowMilliseconds = 20;
    private const int WakeToneStepMilliseconds = 10;
    private const int RequiredWakeToneWindows = 5;
    private const double MinimumWakeToneCoherence = 0.28;
    private const double MinimumWakeToneAmplitude = 0.0008;

    internal static AcousticLatencyDetection? Find(
        ReadOnlySpan<float> captured,
        ReadOnlySpan<float> reference,
        int expectedStartIndex,
        int maximumDelaySamples)
    {
        return Find(
            captured,
            reference,
            expectedStartIndex,
            maximumDelaySamples,
            out _);
    }

    internal static AcousticLatencyDetection? Find(
        ReadOnlySpan<float> captured,
        ReadOnlySpan<float> reference,
        int expectedStartIndex,
        int maximumDelaySamples,
        out AcousticLatencyDetectionDiagnostics diagnostics)
    {
        diagnostics = MeasureCapture(captured);
        if (captured.IsEmpty || reference.IsEmpty) return null;

        var capturedDownsampled = Downsample(captured);
        var referenceDownsampled = Downsample(reference);
        var earliest = Math.Max(
            0,
            expectedStartIndex / DownsampleFactor -
            PcmFrameBuffer.SampleRate * 30 / 1000 / DownsampleFactor);
        var latest = Math.Min(
            capturedDownsampled.Length - referenceDownsampled.Length,
            (expectedStartIndex + maximumDelaySamples) / DownsampleFactor);
        if (latest < earliest)
        {
            return FindWakeTone(captured, expectedStartIndex, maximumDelaySamples, ref diagnostics);
        }

        double referenceEnergy = 0;
        double referenceMean = 0;
        foreach (var sample in referenceDownsampled) referenceMean += sample;
        referenceMean /= referenceDownsampled.Length;
        foreach (var sample in referenceDownsampled)
        {
            var centered = sample - referenceMean;
            referenceEnergy += centered * centered;
        }
        if (referenceEnergy <= double.Epsilon) return null;

        var energyPrefix = new double[capturedDownsampled.Length + 1];
        for (var index = 0; index < capturedDownsampled.Length; index++)
        {
            var value = capturedDownsampled[index];
            energyPrefix[index + 1] = energyPrefix[index] + value * value;
        }

        var bestIndex = -1;
        var bestCorrelation = 0d;
        for (var start = earliest; start <= latest; start++)
        {
            var windowEnergy = energyPrefix[start + referenceDownsampled.Length] - energyPrefix[start];
            if (windowEnergy <= double.Epsilon) continue;

            double dot = 0;
            for (var offset = 0; offset < referenceDownsampled.Length; offset++)
            {
                dot += capturedDownsampled[start + offset] *
                       (referenceDownsampled[offset] - referenceMean);
            }

            var correlation = Math.Abs(dot) / Math.Sqrt(windowEnergy * referenceEnergy);
            if (correlation <= bestCorrelation) continue;
            bestCorrelation = correlation;
            bestIndex = start;
        }

        diagnostics = diagnostics with { BestCorrelation = bestCorrelation };
        if (bestIndex >= 0 && bestCorrelation >= MinimumCorrelation)
        {
            return new AcousticLatencyDetection(
                bestIndex * DownsampleFactor,
                bestCorrelation,
                UsedWakeToneFallback: false);
        }

        return FindWakeTone(captured, expectedStartIndex, maximumDelaySamples, ref diagnostics);
    }

    private static AcousticLatencyDetection? FindWakeTone(
        ReadOnlySpan<float> captured,
        int expectedProbeStartIndex,
        int maximumDelaySamples,
        ref AcousticLatencyDetectionDiagnostics diagnostics)
    {
        var windowSamples = PcmFrameBuffer.SampleRate * WakeToneWindowMilliseconds / 1000;
        var stepSamples = PcmFrameBuffer.SampleRate * WakeToneStepMilliseconds / 1000;
        var expectedWakeStart = expectedProbeStartIndex - AcousticLatencyProbeSignal.LeadInSampleCount;
        var earliest = Math.Max(0, expectedWakeStart - PcmFrameBuffer.SampleRate * 50 / 1000);
        var latest = Math.Min(
            captured.Length - windowSamples,
            expectedWakeStart + maximumDelaySamples);
        if (latest < earliest) return null;

        var baselineEnd = Math.Max(0, earliest - windowSamples);
        var baselineStart = Math.Max(0, baselineEnd - PcmFrameBuffer.SampleRate / 2);
        var baselineAmplitudes = new List<double>();
        for (var start = baselineStart; start + windowSamples <= baselineEnd; start += stepSamples)
        {
            var tone = MeasureTone(captured.Slice(start, windowSamples));
            baselineAmplitudes.Add(tone.Amplitude);
        }
        baselineAmplitudes.Sort();
        var baselineAmplitude = baselineAmplitudes.Count == 0
            ? 0
            : baselineAmplitudes[baselineAmplitudes.Count / 2];
        var requiredAmplitude = Math.Max(MinimumWakeToneAmplitude, baselineAmplitude * 2.5);

        var consecutive = 0;
        var runStart = -1;
        var runCoherence = 0d;
        var bestCoherence = 0d;
        var bestAmplitude = 0d;
        for (var start = earliest; start <= latest; start += stepSamples)
        {
            var tone = MeasureTone(captured.Slice(start, windowSamples));
            if (tone.Coherence > bestCoherence) bestCoherence = tone.Coherence;
            if (tone.Amplitude > bestAmplitude) bestAmplitude = tone.Amplitude;

            if (tone.Coherence >= MinimumWakeToneCoherence && tone.Amplitude >= requiredAmplitude)
            {
                if (consecutive == 0)
                {
                    runStart = start;
                    runCoherence = 0;
                }
                consecutive++;
                runCoherence += tone.Coherence;
                if (consecutive >= RequiredWakeToneWindows)
                {
                    diagnostics = diagnostics with
                    {
                        BestWakeToneCoherence = bestCoherence,
                        BestWakeToneAmplitude = bestAmplitude,
                    };
                    var formalProbeArrival = runStart + AcousticLatencyProbeSignal.LeadInSampleCount;
                    return new AcousticLatencyDetection(
                        formalProbeArrival,
                        Math.Min(1, runCoherence / consecutive),
                        UsedWakeToneFallback: true);
                }
            }
            else
            {
                consecutive = 0;
                runStart = -1;
                runCoherence = 0;
            }
        }

        diagnostics = diagnostics with
        {
            BestWakeToneCoherence = bestCoherence,
            BestWakeToneAmplitude = bestAmplitude,
        };
        return null;
    }

    private static ToneMeasurement MeasureTone(ReadOnlySpan<float> samples)
    {
        if (samples.IsEmpty) return default;

        double mean = 0;
        foreach (var sample in samples) mean += sample;
        mean /= samples.Length;

        double real = 0;
        double imaginary = 0;
        double energy = 0;
        var angularStep = 2 * Math.PI * AcousticLatencyProbeSignal.WakeupFrequencyHz /
                          PcmFrameBuffer.SampleRate;
        for (var index = 0; index < samples.Length; index++)
        {
            var centered = samples[index] - mean;
            var phase = angularStep * index;
            real += centered * Math.Cos(phase);
            imaginary -= centered * Math.Sin(phase);
            energy += centered * centered;
        }

        var amplitude = 2 * Math.Sqrt(real * real + imaginary * imaginary) / samples.Length;
        var rms = Math.Sqrt(energy / samples.Length);
        var coherence = rms <= double.Epsilon
            ? 0
            : amplitude / (Math.Sqrt(2) * rms);
        return new ToneMeasurement(amplitude, coherence);
    }

    private static AcousticLatencyDetectionDiagnostics MeasureCapture(ReadOnlySpan<float> samples)
    {
        if (samples.IsEmpty) return AcousticLatencyDetectionDiagnostics.Empty;

        double energy = 0;
        double peak = 0;
        foreach (var sample in samples)
        {
            var absolute = Math.Abs(sample);
            if (absolute > peak) peak = absolute;
            energy += sample * sample;
        }
        return AcousticLatencyDetectionDiagnostics.Empty with
        {
            PeakAmplitude = peak,
            Rms = Math.Sqrt(energy / samples.Length),
        };
    }

    private static float[] Downsample(ReadOnlySpan<float> samples)
    {
        var result = new float[(samples.Length + DownsampleFactor - 1) / DownsampleFactor];
        for (var resultIndex = 0; resultIndex < result.Length; resultIndex++)
        {
            var start = resultIndex * DownsampleFactor;
            var end = Math.Min(start + DownsampleFactor, samples.Length);
            var sum = 0f;
            for (var index = start; index < end; index++) sum += samples[index];
            result[resultIndex] = sum / (end - start);
        }
        return result;
    }

    private readonly record struct ToneMeasurement(double Amplitude, double Coherence);
}

internal sealed record AcousticLatencyDetection(
    int SampleIndex,
    double Confidence,
    bool UsedWakeToneFallback);

internal sealed record AcousticLatencyDetectionDiagnostics(
    double BestCorrelation,
    double BestWakeToneCoherence,
    double BestWakeToneAmplitude,
    double PeakAmplitude,
    double Rms)
{
    internal static AcousticLatencyDetectionDiagnostics Empty { get; } =
        new(0, 0, 0, 0, 0);
}
