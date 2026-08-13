using System.Diagnostics;

namespace DLNACast.Core.MiPlay;

/// <summary>
/// Coordinates the shared media timeline used by a multi-receiver MiPlay cast.
/// The rooted-phone pair trace uses one TIME_OFFSET, one AAC stream, and one
/// RTP sequence/timestamp timeline for every receiver.
/// </summary>
internal sealed class MiPlaySharedMediaCoordinator
{
    private readonly Lock gate = new();
    private readonly int participantCount;
    private readonly TaskCompletionSource<ulong> timeOffsetReady =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<long> mediaReady =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource<byte[]> accessUnitReady =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private ulong timeOffsetCandidate;
    private ulong timeOffsetArrivals;
    private int timeOffsetArrivalCount;
    private ulong mediaArrivals;
    private int mediaArrivalCount;
    private ulong sharedTimeOffset;
    private long nextAccessUnitIndex;
    private ulong accessUnitArrivals;
    private int accessUnitArrivalCount;
    private byte[]? sharedAccessUnit;
    private Exception? failure;

    public MiPlaySharedMediaCoordinator(int participantCount)
    {
        if (participantCount is < 2 or > 63)
        {
            throw new ArgumentOutOfRangeException(nameof(participantCount));
        }
        this.participantCount = participantCount;
    }

    public bool IsMediaLeader(int participantId)
    {
        ValidateParticipant(participantId);
        return participantId == 1;
    }

    public async Task<ulong> SynchronizeTimeOffsetAsync(
        int participantId,
        ulong candidate,
        CancellationToken cancellationToken)
    {
        Task<ulong> waitTask;
        ulong? completedValue = null;
        lock (gate)
        {
            ThrowIfFailed();
            MarkArrival(ref timeOffsetArrivals, participantId, "TIME_OFFSET");
            timeOffsetArrivalCount++;
            timeOffsetCandidate = Math.Max(timeOffsetCandidate, candidate);
            waitTask = timeOffsetReady.Task;
            if (timeOffsetArrivalCount == participantCount)
            {
                sharedTimeOffset = timeOffsetCandidate;
                completedValue = sharedTimeOffset;
            }
        }
        if (completedValue is ulong value)
        {
            timeOffsetReady.TrySetResult(value);
        }
        return await WaitOrFailAsync(
                waitTask,
                "A receiver left during shared TIME_OFFSET synchronization.",
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<long> SynchronizeMediaAsync(
        int participantId,
        ulong timeOffset,
        CancellationToken cancellationToken)
    {
        Task<long> waitTask;
        long? completedValue = null;
        lock (gate)
        {
            ThrowIfFailed();
            if (!timeOffsetReady.Task.IsCompletedSuccessfully || timeOffset != sharedTimeOffset)
            {
                throw new InvalidOperationException(
                    "Shared MiPlay media must use the synchronized TIME_OFFSET.");
            }
            MarkArrival(ref mediaArrivals, participantId, "media start");
            mediaArrivalCount++;
            waitTask = mediaReady.Task;
            if (mediaArrivalCount == participantCount)
            {
                completedValue = Stopwatch.GetTimestamp();
            }
        }
        if (completedValue is long value)
        {
            mediaReady.TrySetResult(value);
        }
        return await WaitOrFailAsync(
                waitTask,
                "A receiver left during shared media-start synchronization.",
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<byte[]> SynchronizeAccessUnitAsync(
        int participantId,
        long accessUnitIndex,
        byte[]? leaderAccessUnit,
        CancellationToken cancellationToken)
    {
        Task<byte[]> waitTask;
        TaskCompletionSource<byte[]>? completed = null;
        byte[]? completedValue = null;
        lock (gate)
        {
            ThrowIfFailed();
            if (!mediaReady.Task.IsCompletedSuccessfully)
            {
                throw new InvalidOperationException(
                    "Shared MiPlay media has not completed start synchronization.");
            }
            if (accessUnitIndex != nextAccessUnitIndex)
            {
                throw new InvalidOperationException(
                    $"Shared MiPlay access-unit index changed from {nextAccessUnitIndex} to {accessUnitIndex}.");
            }
            var isLeader = IsMediaLeader(participantId);
            if (isLeader != (leaderAccessUnit is not null))
            {
                throw new InvalidOperationException(
                    isLeader
                        ? "The shared MiPlay media leader did not provide an AAC access unit."
                        : "A shared MiPlay media follower attempted to publish an AAC access unit.");
            }
            if (leaderAccessUnit is { Length: 0 })
            {
                throw new ArgumentException("The shared AAC access unit is empty.", nameof(leaderAccessUnit));
            }

            MarkArrival(ref accessUnitArrivals, participantId, "access unit");
            accessUnitArrivalCount++;
            sharedAccessUnit ??= leaderAccessUnit;
            waitTask = accessUnitReady.Task;
            if (accessUnitArrivalCount == participantCount)
            {
                completedValue = sharedAccessUnit ?? throw new InvalidOperationException(
                    "The shared MiPlay access unit completed without leader data.");
                completed = accessUnitReady;
                accessUnitReady = new TaskCompletionSource<byte[]>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                nextAccessUnitIndex++;
                accessUnitArrivals = 0;
                accessUnitArrivalCount = 0;
                sharedAccessUnit = null;
            }
        }
        if (completed is not null)
        {
            completed.TrySetResult(completedValue!);
        }
        return await WaitOrFailAsync(
                waitTask,
                "A receiver left during shared AAC synchronization.",
                cancellationToken)
            .ConfigureAwait(false);
    }

    public void Fail(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        lock (gate)
        {
            if (failure is not null)
            {
                return;
            }
            failure = exception;
            timeOffsetReady.TrySetException(exception);
            mediaReady.TrySetException(exception);
            accessUnitReady.TrySetException(exception);
        }
    }

    private async Task<T> WaitOrFailAsync<T>(
        Task<T> task,
        string cancellationMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            return await task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Fail(new OperationCanceledException(cancellationMessage, cancellationToken));
            throw;
        }
    }

    private void MarkArrival(ref ulong arrivals, int participantId, string phase)
    {
        ValidateParticipant(participantId);
        var participantBit = 1UL << (participantId - 1);
        if ((arrivals & participantBit) != 0)
        {
            throw new InvalidOperationException(
                $"A receiver entered shared MiPlay {phase} synchronization more than once.");
        }
        arrivals |= participantBit;
    }

    private void ValidateParticipant(int participantId)
    {
        if (participantId < 1 || participantId > participantCount)
        {
            throw new ArgumentOutOfRangeException(nameof(participantId));
        }
    }

    private void ThrowIfFailed()
    {
        if (failure is not null)
        {
            throw new InvalidOperationException("Shared MiPlay media synchronization failed.", failure);
        }
    }
}
