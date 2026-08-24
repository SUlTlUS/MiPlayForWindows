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
}
