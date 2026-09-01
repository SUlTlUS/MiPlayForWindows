namespace DLNACast.Core.MiPlay;

/// <summary>
/// Converts the Windows monotonic clock into the RTSP TIME_OFFSET and MPEG-TS
/// PCR domains used by the rooted-tablet audio path. Shared media uses the
/// tablet formulas directly; the legacy helpers remain for single-speaker wire
/// compatibility and captured-packet comparisons.
/// </summary>
public static class MiPlayWfdMediaClock
{
    public const ulong ProgramClockRate = 90_000;
    public const ulong MicrosecondsPerSecond = 1_000_000;
    public const ulong ProgramClockModulus = 1UL << 33;
    public const ulong ProgramClockModulus27Mhz = ProgramClockModulus * 300;

    public static ulong ConvertStopwatchTicksToMicroseconds(
        long timestampTicks,
        long timestampFrequency)
    {
        if (timestampTicks < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timestampTicks));
        }
        if (timestampFrequency <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timestampFrequency));
        }

        return checked((ulong)(
            ((UInt128)(ulong)timestampTicks * MicrosecondsPerSecond) /
            (ulong)timestampFrequency));
    }

    /// <summary>
    /// Matches MultiMirrorDisplay::updateTimeOffset: the synchronized server
    /// clock minus the first normalized audio PTS plus the receiver delay.
    /// </summary>
    public static ulong CreateTabletTimeOffsetMicroseconds(
        ulong serverNowMicroseconds,
        ulong firstAudioPresentationTimeMicroseconds,
        int playbackDelayMicroseconds)
    {
        if (playbackDelayMicroseconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(playbackDelayMicroseconds));
        }
        if (serverNowMicroseconds < firstAudioPresentationTimeMicroseconds)
        {
            throw new ArgumentOutOfRangeException(nameof(serverNowMicroseconds));
        }

        return checked(
            serverNowMicroseconds -
            firstAudioPresentationTimeMicroseconds +
            (ulong)playbackDelayMicroseconds);
    }

    public static ulong CreateInitialProgramClockReference90Khz(
        ulong timeOffsetMicroseconds,
        int playbackDelayMicroseconds)
    {
        if (playbackDelayMicroseconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(playbackDelayMicroseconds));
        }
        if (timeOffsetMicroseconds < (ulong)playbackDelayMicroseconds)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeOffsetMicroseconds),
                "The RTSP monotonic clock must be later than the playback delay.");
        }

        var mediaOriginMicroseconds = timeOffsetMicroseconds - (ulong)playbackDelayMicroseconds;
        var wholeSeconds = mediaOriginMicroseconds / MicrosecondsPerSecond;
        var remainingMicroseconds = mediaOriginMicroseconds % MicrosecondsPerSecond;
        var unwrappedProgramClockReference = checked(
            (wholeSeconds * ProgramClockRate) +
            ((remainingMicroseconds * ProgramClockRate) / MicrosecondsPerSecond));
        return unwrappedProgramClockReference % ProgramClockModulus;
    }

    /// <summary>
    /// Retains the fractional PCR ticks for the legacy capture-minus-delay
    /// origin. Shared tablet-style media uses
    /// CreateTabletProgramClockReference27Mhz instead.
    /// </summary>
    public static ulong CreateProgramClockReference27Mhz(
        ulong monotonicMicroseconds,
        int playbackDelayMicroseconds)
    {
        if (playbackDelayMicroseconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(playbackDelayMicroseconds));
        }
        if (monotonicMicroseconds < (ulong)playbackDelayMicroseconds)
        {
            throw new ArgumentOutOfRangeException(
                nameof(monotonicMicroseconds),
                "The monotonic clock must be later than the playback delay.");
        }

        var originMicroseconds = monotonicMicroseconds - (ulong)playbackDelayMicroseconds;
        return (ulong)(((UInt128)originMicroseconds * 27) % ProgramClockModulus27Mhz);
    }

    /// <summary>
    /// Matches TSPacketizer::packetize, which uses ALooper::GetNowUs() * 27
    /// directly and does not subtract the receiver playback delay.
    /// </summary>
    public static ulong CreateTabletProgramClockReference27Mhz(ulong nativeNowMicroseconds) =>
        (ulong)(((UInt128)nativeNowMicroseconds * 27) % ProgramClockModulus27Mhz);

    public static double MeasurePlaybackDelayMicroseconds(
        ulong timeOffsetMicroseconds,
        ulong programClockReference90Khz) =>
        timeOffsetMicroseconds -
        (programClockReference90Khz * (double)MicrosecondsPerSecond / ProgramClockRate);
}
