namespace DLNACast.Core.MiPlay;

/// <summary>
/// Rebases the sender's absolute pacing origin after a long runtime stall so
/// queued AAC access units are not burst-written and recovery does not add a
/// permanent per-frame delay.
/// </summary>
internal static class MiPlayMediaStallRecoveryPlan
{
    public const long MinimumAccessUnitIndex = 250;
    public const double ActivationSendGapMilliseconds = 250;

    public static bool ShouldActivate(long accessUnitIndex, double sendGapMilliseconds)
    {
        if (accessUnitIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(accessUnitIndex));
        }
        if (double.IsNaN(sendGapMilliseconds) || sendGapMilliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sendGapMilliseconds));
        }

        return accessUnitIndex >= MinimumAccessUnitIndex &&
            sendGapMilliseconds >= ActivationSendGapMilliseconds;
    }

    public static long RebaseMediaStartedAt(
        long mediaWriteCompletedAt,
        long accessUnitsSent,
        long stopwatchFrequency)
    {
        if (mediaWriteCompletedAt < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(mediaWriteCompletedAt));
        }
        if (accessUnitsSent <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(accessUnitsSent));
        }
        if (stopwatchFrequency <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stopwatchFrequency));
        }

        var nominalTicks = checked((long)Math.Round(
            MiPlayWfdStartupPacingPlan.NominalAccessUnitMilliseconds *
            stopwatchFrequency /
            1_000d));
        var dueAfterAccessUnits = checked((long)Math.Round(
            MiPlayWfdStartupPacingPlan.GetDueAfterMilliseconds(accessUnitsSent) *
            stopwatchFrequency /
            1_000d));
        return checked(mediaWriteCompletedAt + nominalTicks - dueAfterAccessUnits);
    }

    public static uint CalculateTimelineAdvance90Khz(
        long previousMediaStartedAt,
        long rebasedMediaStartedAt,
        long stopwatchFrequency,
        bool captureTimelineReanchored = false)
    {
        if (previousMediaStartedAt < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(previousMediaStartedAt));
        }
        if (rebasedMediaStartedAt < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rebasedMediaStartedAt));
        }
        if (stopwatchFrequency <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stopwatchFrequency));
        }
        if (captureTimelineReanchored)
        {
            return 0;
        }
        if (rebasedMediaStartedAt <= previousMediaStartedAt)
        {
            return 0;
        }

        var advanceTicks = rebasedMediaStartedAt - previousMediaStartedAt;
        return checked((uint)Math.Round(
            advanceTicks * (double)MiPlayWfdMediaClock.ProgramClockRate /
            stopwatchFrequency));
    }

    public static ulong CalculateRuntimeTimeOffsetMicroseconds(
        ulong currentMonotonicMicroseconds,
        ulong mediaPosition90Khz,
        int playbackDelayMicroseconds)
    {
        if (playbackDelayMicroseconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(playbackDelayMicroseconds));
        }

        var mediaPositionMicroseconds = checked((ulong)(
            ((UInt128)mediaPosition90Khz * MiPlayWfdMediaClock.MicrosecondsPerSecond) /
            MiPlayWfdMediaClock.ProgramClockRate));
        if (currentMonotonicMicroseconds < mediaPositionMicroseconds)
        {
            throw new ArgumentOutOfRangeException(
                nameof(currentMonotonicMicroseconds),
                "The sender clock must not precede the current media position.");
        }

        return MiPlayWfdMediaClock.CreateTabletTimeOffsetMicroseconds(
            currentMonotonicMicroseconds,
            mediaPositionMicroseconds,
            playbackDelayMicroseconds);
    }
}
