namespace DLNACast.Core.MiPlay;

/// <summary>
/// Keeps the original media timeline intact, but prevents queued AAC access
/// units from being written back-to-back after a long runtime stall.
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

    public static long PreserveNominalGap(long remainingTicks, long stopwatchFrequency)
    {
        if (stopwatchFrequency <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stopwatchFrequency));
        }

        var nominalTicks = checked((long)Math.Round(
            MiPlayWfdStartupPacingPlan.NominalAccessUnitMilliseconds *
            stopwatchFrequency /
            1_000d));
        return Math.Max(remainingTicks, nominalTicks);
    }
}
