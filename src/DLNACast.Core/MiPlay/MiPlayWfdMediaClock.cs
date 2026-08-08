namespace DLNACast.Core.MiPlay;

/// <summary>
/// Maps the RTSP monotonic TIME_OFFSET clock to the MPEG-TS program clock used
/// by the rooted-phone legacy audio path. RTP and PTS still start at zero; the
/// PCR anchors that zero-based media timeline close to the receiver's current
/// monotonic time minus the negotiated playback buffer.
/// </summary>
public static class MiPlayWfdMediaClock
{
    public const ulong ProgramClockRate = 90_000;
    public const ulong MicrosecondsPerSecond = 1_000_000;
    public const ulong ProgramClockModulus = 1UL << 33;

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

    public static double MeasurePlaybackDelayMicroseconds(
        ulong timeOffsetMicroseconds,
        ulong programClockReference90Khz) =>
        timeOffsetMicroseconds -
        (programClockReference90Khz * (double)MicrosecondsPerSecond / ProgramClockRate);
}
