namespace DLNACast.Core.MiPlay;

public sealed record MiPlayLegacyStreamingHeartbeat(
    int DueAfterMilliseconds,
    ushort Sequence,
    byte[] CommandFrame);

/// <summary>
/// Five-second steady-state heartbeat timing recovered from the clean
/// rooted-phone receiver-selection capture.
/// </summary>
public static class MiPlayLegacyStreamingHeartbeatPlan
{
    public const int IntervalMilliseconds = 5_000;
    public const ushort InitialPostOpenSequence =
        MiPlayLegacyPostOpenPlaybackSession.FirstPeriodicHeartbeatSequence;

    public static IReadOnlyList<MiPlayLegacyStreamingHeartbeat> Create(
        double mediaDurationMilliseconds,
        ushort initialSequence = InitialPostOpenSequence)
    {
        if (mediaDurationMilliseconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(mediaDurationMilliseconds));
        }

        var count = (long)Math.Floor(mediaDurationMilliseconds / IntervalMilliseconds);
        var availableSequences = (long)ushort.MaxValue - initialSequence + 1;
        if (count > availableSequences || count > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(mediaDurationMilliseconds));
        }

        return Enumerable.Range(0, (int)count)
            .Select(index =>
            {
                var sequence = checked((ushort)(initialSequence + index));
                return new MiPlayLegacyStreamingHeartbeat(
                    checked((index + 1) * IntervalMilliseconds),
                    sequence,
                    MiPlayCommandFrameCodec.Encode(
                        MiPlayProtocolConstants.HeartbeatCommand,
                        sequence,
                        []));
            })
            .ToArray();
    }

    public static long CalculateDueTimestamp(
        long previousHeartbeatTimestamp,
        int dueAfterPreviousHeartbeatMilliseconds,
        long stopwatchFrequency)
    {
        if (previousHeartbeatTimestamp < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(previousHeartbeatTimestamp));
        }
        if (dueAfterPreviousHeartbeatMilliseconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dueAfterPreviousHeartbeatMilliseconds));
        }
        if (stopwatchFrequency <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stopwatchFrequency));
        }

        return checked(previousHeartbeatTimestamp +
            dueAfterPreviousHeartbeatMilliseconds * stopwatchFrequency / 1_000L);
    }
}
