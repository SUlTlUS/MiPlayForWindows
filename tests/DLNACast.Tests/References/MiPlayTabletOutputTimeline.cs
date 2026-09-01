namespace DLNACast.Tests.References;

/// <summary>
/// Offline reference for Mi Pad MicRecorder.muxAudio and the native clock
/// conversions. This is deliberately not part of the production sender: the
/// Windows prewarm/start boundary must be aligned before using these PTS on wire.
/// See docs/miplay-tablet-output-timestamps.md for binary addresses and evidence.
/// </summary>
internal sealed class MiPlayTabletOutputTimeline
{
    internal const long FixThresholdMicroseconds = 50;

    private long firstAudioTime;
    private long firstSystemTime;
    private bool initialized;

    internal long CorrectionCount { get; private set; }
    internal long TotalCorrectionMicroseconds { get; private set; }

    internal long Map(long encodedPresentationTimeMicroseconds, long outputTimeMicroseconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(encodedPresentationTimeMicroseconds);
        ArgumentOutOfRangeException.ThrowIfNegative(outputTimeMicroseconds);
        if (!initialized)
        {
            initialized = true;
            firstAudioTime = encodedPresentationTimeMicroseconds;
            firstSystemTime = outputTimeMicroseconds;
        }

        var pts = checked(encodedPresentationTimeMicroseconds - firstAudioTime);
        var elapsed = checked(outputTimeMicroseconds - firstSystemTime);
        var behind = checked(elapsed - pts);
        if (behind > FixThresholdMicroseconds)
        {
            firstAudioTime = checked(firstAudioTime - behind);
            pts = checked(encodedPresentationTimeMicroseconds - firstAudioTime);
            CorrectionCount++;
            TotalCorrectionMicroseconds = checked(TotalCorrectionMicroseconds + behind);
        }
        return pts;
    }

    internal static ulong To90Khz(long presentationTimeMicroseconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(presentationTimeMicroseconds);
        return checked((ulong)((UInt128)(ulong)presentationTimeMicroseconds * 9 / 100));
    }

    internal static long CreateTimeOffset(
        long nativeServerTimeMicroseconds,
        long presentationTimeMicroseconds,
        int playbackDelayMicroseconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(playbackDelayMicroseconds);
        return checked(nativeServerTimeMicroseconds - presentationTimeMicroseconds + playbackDelayMicroseconds);
    }

    internal static ulong CreatePcr27Mhz(long nativeNowMicroseconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(nativeNowMicroseconds);
        return checked((ulong)nativeNowMicroseconds * 27);
    }
}
