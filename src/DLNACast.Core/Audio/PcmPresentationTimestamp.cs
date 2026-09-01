using System.Diagnostics;

namespace DLNACast.Core.Audio;

public static class PcmPresentationTimestamp
{
    private const ulong NanosecondsPerSecond = 1_000_000_000;
    private const ulong NanosecondsPerMicrosecond = 1_000;

    public static ulong GetElapsedRealtimeNanoseconds() =>
        checked((ulong)(
            ((UInt128)(ulong)Stopwatch.GetTimestamp() * NanosecondsPerSecond) /
            (ulong)Stopwatch.Frequency));

    public static ulong CalculateMicroseconds(
        ulong elapsedRealtimeNanoseconds,
        int actualBytesRead,
        PcmFrameFormat format)
    {
        if (actualBytesRead <= 0) throw new ArgumentOutOfRangeException(nameof(actualBytesRead));
        ArgumentNullException.ThrowIfNull(format);
        if (actualBytesRead % (format.Channels * format.BytesPerSample) != 0)
        {
            throw new ArgumentException(
                "PCM reads must contain complete stereo signed-16 samples.",
                nameof(actualBytesRead));
        }

        var bytesPerSecond = checked((ulong)(
            format.SampleRate * format.Channels * format.BytesPerSample));
        var readDurationNanoseconds = checked((ulong)(
            ((UInt128)(uint)actualBytesRead * NanosecondsPerSecond) /
            bytesPerSecond));
        if (elapsedRealtimeNanoseconds < readDurationNanoseconds)
        {
            throw new ArgumentOutOfRangeException(
                nameof(elapsedRealtimeNanoseconds),
                "The monotonic read timestamp must be later than the PCM read duration.");
        }

        return (elapsedRealtimeNanoseconds - readDurationNanoseconds) /
            NanosecondsPerMicrosecond;
    }

    public static ulong AddBytes(
        ulong presentationTimeMicroseconds,
        int byteCount,
        PcmFrameFormat format)
    {
        if (byteCount < 0) throw new ArgumentOutOfRangeException(nameof(byteCount));
        ArgumentNullException.ThrowIfNull(format);
        var bytesPerSecond = checked((ulong)(
            format.SampleRate * format.Channels * format.BytesPerSample));
        var durationMicroseconds = checked((ulong)(
            ((UInt128)(uint)byteCount * 1_000_000UL) /
            bytesPerSecond));
        return checked(presentationTimeMicroseconds + durationMicroseconds);
    }
}

public readonly record struct PcmFrameRead(
    byte[] Bytes,
    ulong PresentationTimeMicroseconds,
    bool IsBufferUnderrunSilence = false,
    bool CaptureDiscontinuity = false,
    bool ReanchorPresentationTimeline = false);
