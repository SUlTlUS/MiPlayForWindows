namespace DLNACast.Core.MiPlay;

/// <summary>
/// One output PTS clock for every receiver in a shared AAC stream. FFmpeg's
/// raw-PCM encoder uses sample positions, not the capture-age metadata. Anchor
/// wall time at the first media handoff, after the ordered prewarm runway, and
/// apply MicRecorder's forward-only 50-us output correction once per AU.
/// </summary>
internal sealed class MiPlaySharedOutputTimeline
{
    internal const ulong FixThresholdMicroseconds = 50;
    internal const int RetainedTimestampCount = 64;
    private readonly Lock gate = new();
    private readonly Dictionary<long, MiPlayOutputTimestamp> retainedTimestamps = [];
    private readonly Queue<long> retainedTimestampOrder = new(RetainedTimestampCount);
    private long lastAccessUnitIndex = -1;
    private ulong firstOutputMicroseconds;
    private ulong previousOutputMicroseconds;
    private ulong correctionMicroseconds;
    private long correctionCount;
    private ulong transportAdvance90Khz;
    private long lastTransportRecoveryIndex = -1;
    private ulong lastTransportCompletionMicroseconds;
    private uint lastTransportRecoveryDelta;
    private MiPlayOutputTimestamp current;

    internal MiPlayOutputTimestamp GetTimestamp(long accessUnitIndex, ulong outputMicroseconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(accessUnitIndex);
        lock (gate)
        {
            // Other receivers consume exactly the same result, even if their
            // thread runs later. A receiver never owns an independent fix pts.
            if (retainedTimestamps.TryGetValue(accessUnitIndex, out var retained))
            {
                return retained;
            }
            if (accessUnitIndex <= lastAccessUnitIndex)
            {
                throw new InvalidOperationException(
                    "The requested shared output timestamp is older than the retained late-join window.");
            }
            if (accessUnitIndex != lastAccessUnitIndex + 1)
            {
                throw new InvalidOperationException("Shared output PTS access units must remain ordered.");
            }
            if (lastAccessUnitIndex < 0)
            {
                firstOutputMicroseconds = outputMicroseconds;
            }
            else if (outputMicroseconds < previousOutputMicroseconds)
            {
                throw new ArgumentOutOfRangeException(nameof(outputMicroseconds));
            }

            var encoderPts = CalculateEncoderPresentationTimeMicroseconds(accessUnitIndex);
            var pts = checked(encoderPts + correctionMicroseconds);
            var transportAdvanceMicroseconds = checked((ulong)(
                (UInt128)transportAdvance90Khz * 1_000_000 / 90_000));
            var effectivePts = checked(pts + transportAdvanceMicroseconds);
            var elapsed = outputMicroseconds - firstOutputMicroseconds;
            var correction = elapsed > effectivePts ? elapsed - effectivePts : 0;
            var corrected = correction > FixThresholdMicroseconds;
            if (corrected)
            {
                correctionMicroseconds = checked(correctionMicroseconds + correction);
                pts = checked(pts + correction);
                correctionCount++;
            }

            current = new MiPlayOutputTimestamp(
                checked((ulong)((UInt128)pts * 9 / 100)),
                MiPlayWfdMediaClock.CreateTabletProgramClockReference27Mhz(outputMicroseconds),
                corrected,
                correctionCount,
                correctionMicroseconds);
            previousOutputMicroseconds = outputMicroseconds;
            lastAccessUnitIndex = accessUnitIndex;
            retainedTimestamps.Add(accessUnitIndex, current);
            retainedTimestampOrder.Enqueue(accessUnitIndex);
            while (retainedTimestampOrder.Count > RetainedTimestampCount)
            {
                retainedTimestamps.Remove(retainedTimestampOrder.Dequeue());
            }
            return current;
        }
    }

    internal uint RecoverAfterSend(long completedAccessUnitIndex, ulong completedMicroseconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(completedAccessUnitIndex);
        lock (gate)
        {
            if (completedAccessUnitIndex == lastTransportRecoveryIndex)
            {
                if (completedMicroseconds != lastTransportCompletionMicroseconds)
                {
                    throw new InvalidOperationException("Receivers disagreed about the shared send completion.");
                }
                return lastTransportRecoveryDelta;
            }
            if (completedAccessUnitIndex != lastAccessUnitIndex)
            {
                throw new InvalidOperationException("Transport recovery must match the current shared output AU.");
            }
            if (completedMicroseconds < previousOutputMicroseconds)
            {
                throw new ArgumentOutOfRangeException(nameof(completedMicroseconds));
            }

            // Only compensate lag that remains AFTER this AU was written.
            // A fix before writing cannot cover a socket that blocks later,
            // and the pacing-origin rebase must not apply the same lag again.
            var effectivePts = checked(
                CalculateEncoderPresentationTimeMicroseconds(completedAccessUnitIndex) +
                correctionMicroseconds +
                (ulong)((UInt128)transportAdvance90Khz * 1_000_000 / 90_000));
            var elapsed = completedMicroseconds - firstOutputMicroseconds;
            var remainingLag = elapsed > effectivePts ? elapsed - effectivePts : 0;
            var advance90Khz = remainingLag > FixThresholdMicroseconds
                ? checked((uint)((UInt128)remainingLag * 9 / 100))
                : 0;
            transportAdvance90Khz = checked(transportAdvance90Khz + advance90Khz);
            lastTransportRecoveryIndex = completedAccessUnitIndex;
            lastTransportCompletionMicroseconds = completedMicroseconds;
            lastTransportRecoveryDelta = advance90Khz;
            return advance90Khz;
        }
    }

    internal static ulong CalculateEncoderPresentationTimeMicroseconds(long accessUnitIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(accessUnitIndex);
        // Derive from total samples, never repeatedly add a truncated 21,333 us.
        return checked((ulong)((UInt128)(ulong)accessUnitIndex * 1_024 * 1_000_000 / 48_000));
    }
}

internal readonly record struct MiPlayOutputTimestamp(
    ulong PresentationTimestamp90Khz,
    ulong ProgramClockReference27Mhz,
    bool Corrected,
    long CorrectionCount,
    ulong TotalCorrectionMicroseconds);
