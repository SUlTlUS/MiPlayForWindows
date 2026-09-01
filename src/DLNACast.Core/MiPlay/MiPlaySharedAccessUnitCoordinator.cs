using DLNACast.Core.Audio;

namespace DLNACast.Core.MiPlay;

/// <summary>
/// Presents one frame boundary to the whole receiver set while retaining one
/// encoded AAC access unit per channel route. Send completion is also grouped,
/// so one slow socket cannot make only one speaker rebase its media timeline.
/// </summary>
internal sealed class MiPlaySharedAccessUnitCoordinator
{
    private readonly Lock gate = new();
    private readonly Dictionary<AudioChannelRoute, GroupState> groups;
    private readonly int participantCount;
    private TaskCompletionSource<FrameBundle> accessUnitReady =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource<MiPlaySharedSendCompletion> sendCompletionReady =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int registeredCount;
    private long nextAccessUnitIndex;
    private ulong accessUnitArrivals;
    private int accessUnitArrivalCount;
    private ulong framePresentationTimeMicroseconds;
    private bool? frameReanchorPresentationTimeline;
    private long nextSendCompletionIndex;
    private ulong sendCompletionArrivals;
    private int sendCompletionArrivalCount;
    private long firstMediaStartedAt;
    private long minimumMediaWriteCompletedAt;
    private long maximumMediaWriteCompletedAt;
    private bool rebaseMediaTimeline;
    private bool? captureTimelineReanchoredForSend;
    private Exception? failure;

    public MiPlaySharedAccessUnitCoordinator(
        IReadOnlyDictionary<AudioChannelRoute, int> participantCounts)
    {
        ArgumentNullException.ThrowIfNull(participantCounts);
        if (participantCounts.Count == 0 ||
            participantCounts.Values.Any(count => count is < 1 or > 63))
        {
            throw new ArgumentOutOfRangeException(nameof(participantCounts));
        }

        groups = participantCounts.ToDictionary(
            pair => pair.Key,
            pair => new GroupState(pair.Value));
        participantCount = participantCounts.Values.Sum();
        if (participantCount is < 1 or > 63)
        {
            throw new ArgumentOutOfRangeException(nameof(participantCounts));
        }
    }

    public MiPlaySharedAccessUnitParticipant Register(AudioChannelRoute route)
    {
        lock (gate)
        {
            ThrowIfFailed();
            if (!groups.TryGetValue(route, out var group))
            {
                throw new InvalidOperationException($"No shared AAC group was configured for {route}.");
            }
            if (group.RegisteredCount >= group.ParticipantCount)
            {
                throw new InvalidOperationException(
                    $"The shared AAC group for {route} accepts exactly {group.ParticipantCount} receivers.");
            }

            group.RegisteredCount++;
            var participantId = ++registeredCount;
            return new(route, participantId, IsLeader: group.RegisteredCount == 1);
        }
    }

    public async Task<MiPlaySharedAccessUnit> SynchronizeAsync(
        MiPlaySharedAccessUnitParticipant participant,
        long accessUnitIndex,
        MiPlayEncodedAccessUnit? leaderAccessUnit,
        CancellationToken cancellationToken)
    {
        Task<FrameBundle> waitTask;
        TaskCompletionSource<FrameBundle>? completed = null;
        FrameBundle? completedValue = null;
        lock (gate)
        {
            ThrowIfFailed();
            if (!groups.TryGetValue(participant.Route, out var group) ||
                participant.ParticipantId is < 1 ||
                participant.ParticipantId > registeredCount)
            {
                throw new InvalidOperationException("The shared AAC participant is not registered.");
            }
            if (registeredCount != participantCount)
            {
                throw new InvalidOperationException("The shared AAC cast is not fully registered.");
            }
            if (accessUnitIndex != nextAccessUnitIndex)
            {
                throw new InvalidOperationException(
                    $"Shared AAC cast expected access unit {nextAccessUnitIndex}, not {accessUnitIndex}.");
            }

            MarkArrival(ref accessUnitArrivals, participant.ParticipantId, "AAC frame");
            if (participant.IsLeader)
            {
                group.SharedAccessUnit = leaderAccessUnit ??
                    throw new InvalidOperationException("The AAC group leader must provide an access unit.");
                framePresentationTimeMicroseconds = Math.Max(
                    framePresentationTimeMicroseconds,
                    leaderAccessUnit.Value.PresentationTimeMicroseconds);
                if (frameReanchorPresentationTimeline is bool expectedReanchor &&
                    expectedReanchor != leaderAccessUnit.Value.ReanchorPresentationTimeline)
                {
                    throw new InvalidOperationException(
                        $"Shared AAC route groups disagreed about capture timeline recovery: " +
                        $"accessUnit={accessUnitIndex},route={participant.Route}," +
                        $"expected={expectedReanchor}," +
                        $"actual={leaderAccessUnit.Value.ReanchorPresentationTimeline}," +
                        $"presentationUs={leaderAccessUnit.Value.PresentationTimeMicroseconds}.");
                }
                frameReanchorPresentationTimeline =
                    leaderAccessUnit.Value.ReanchorPresentationTimeline;
            }
            else if (leaderAccessUnit is not null)
            {
                throw new InvalidOperationException("Only the AAC group leader may provide an access unit.");
            }

            accessUnitArrivalCount++;
            waitTask = accessUnitReady.Task;
            if (accessUnitArrivalCount == participantCount)
            {
                var accessUnits = groups.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value.SharedAccessUnit ??
                        throw new InvalidOperationException(
                            $"The AAC group leader for {pair.Key} did not provide an access unit."));
                completedValue = new FrameBundle(
                    accessUnits,
                    framePresentationTimeMicroseconds,
                    frameReanchorPresentationTimeline ?? false);
                completed = accessUnitReady;
                accessUnitReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
                nextAccessUnitIndex++;
                accessUnitArrivals = 0;
                accessUnitArrivalCount = 0;
                framePresentationTimeMicroseconds = 0;
                frameReanchorPresentationTimeline = null;
                foreach (var state in groups.Values)
                {
                    state.SharedAccessUnit = null;
                }
            }
        }

        completed?.TrySetResult(completedValue!);
        try
        {
            var frame = await waitTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            return new MiPlaySharedAccessUnit(
                frame.AccessUnits[participant.Route].Bytes,
                frame.PresentationTimeMicroseconds,
                frame.ReanchorPresentationTimeline);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Fail(new OperationCanceledException(
                $"A receiver left the shared {participant.Route} AAC group."));
            throw;
        }
    }

    public async Task<MiPlaySharedSendCompletion> SynchronizeSendCompletionAsync(
        MiPlaySharedAccessUnitParticipant participant,
        long accessUnitIndex,
        long mediaWriteCompletedAt,
        double sendGapMilliseconds,
        long mediaStartedAt,
        long stopwatchFrequency,
        bool captureTimelineReanchored,
        CancellationToken cancellationToken)
    {
        if (mediaWriteCompletedAt < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(mediaWriteCompletedAt));
        }
        if (double.IsNaN(sendGapMilliseconds) || sendGapMilliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sendGapMilliseconds));
        }
        if (mediaStartedAt < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(mediaStartedAt));
        }
        if (stopwatchFrequency <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stopwatchFrequency));
        }

        Task<MiPlaySharedSendCompletion> waitTask;
        TaskCompletionSource<MiPlaySharedSendCompletion>? completed = null;
        MiPlaySharedSendCompletion completedValue = default;
        lock (gate)
        {
            ThrowIfFailed();
            if (participant.ParticipantId is < 1 || participant.ParticipantId > registeredCount)
            {
                throw new InvalidOperationException("The shared AAC participant is not registered.");
            }
            if (registeredCount != participantCount)
            {
                throw new InvalidOperationException("The shared AAC cast is not fully registered.");
            }
            if (accessUnitIndex != nextSendCompletionIndex)
            {
                throw new InvalidOperationException(
                    $"Shared AAC send completion expected access unit " +
                    $"{nextSendCompletionIndex}, not {accessUnitIndex}.");
            }

            MarkArrival(ref sendCompletionArrivals, participant.ParticipantId, "send completion");
            if (captureTimelineReanchoredForSend is bool expectedReanchor &&
                expectedReanchor != captureTimelineReanchored)
            {
                throw new InvalidOperationException(
                    "Shared MiPlay receivers disagreed about capture timeline recovery.");
            }
            captureTimelineReanchoredForSend = captureTimelineReanchored;
            if (sendCompletionArrivalCount == 0)
            {
                firstMediaStartedAt = mediaStartedAt;
                minimumMediaWriteCompletedAt = mediaWriteCompletedAt;
                maximumMediaWriteCompletedAt = mediaWriteCompletedAt;
            }
            else
            {
                if (mediaStartedAt != firstMediaStartedAt)
                {
                    throw new InvalidOperationException(
                        "Shared MiPlay receivers entered a frame with different pacing origins.");
                }
                minimumMediaWriteCompletedAt = Math.Min(
                    minimumMediaWriteCompletedAt,
                    mediaWriteCompletedAt);
                maximumMediaWriteCompletedAt = Math.Max(
                    maximumMediaWriteCompletedAt,
                    mediaWriteCompletedAt);
            }
            rebaseMediaTimeline |= MiPlayMediaStallRecoveryPlan.ShouldActivate(
                accessUnitIndex,
                sendGapMilliseconds);
            sendCompletionArrivalCount++;
            waitTask = sendCompletionReady.Task;
            if (sendCompletionArrivalCount == participantCount)
            {
                var synchronizedMediaStartedAt = rebaseMediaTimeline
                    ? MiPlayMediaStallRecoveryPlan.RebaseMediaStartedAt(
                        maximumMediaWriteCompletedAt,
                        accessUnitIndex + 1,
                        stopwatchFrequency)
                    : firstMediaStartedAt;
                completedValue = new MiPlaySharedSendCompletion(
                    synchronizedMediaStartedAt,
                    (maximumMediaWriteCompletedAt - minimumMediaWriteCompletedAt) * 1_000d /
                        stopwatchFrequency,
                    rebaseMediaTimeline,
                    MiPlayMediaStallRecoveryPlan.CalculateTimelineAdvance90Khz(
                        firstMediaStartedAt,
                        synchronizedMediaStartedAt,
                        stopwatchFrequency,
                        captureTimelineReanchoredForSend ?? false),
                    MiPlayWfdMediaClock.ConvertStopwatchTicksToMicroseconds(
                        maximumMediaWriteCompletedAt,
                        stopwatchFrequency));
                completed = sendCompletionReady;
                sendCompletionReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
                nextSendCompletionIndex++;
                sendCompletionArrivals = 0;
                sendCompletionArrivalCount = 0;
                firstMediaStartedAt = 0;
                minimumMediaWriteCompletedAt = 0;
                maximumMediaWriteCompletedAt = 0;
                rebaseMediaTimeline = false;
                captureTimelineReanchoredForSend = null;
            }
        }

        completed?.TrySetResult(completedValue);
        try
        {
            return await waitTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Fail(new OperationCanceledException(
                "A receiver left the shared AAC send-completion barrier."));
            throw;
        }
    }

    public void Fail(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        lock (gate)
        {
            if (failure is not null) return;
            failure = exception;
            foreach (var group in groups.Values)
            {
                group.SharedAccessUnit = null;
            }
            accessUnitReady.TrySetException(exception);
            sendCompletionReady.TrySetException(exception);
        }
    }

    private static void MarkArrival(ref ulong arrivals, int participantId, string phase)
    {
        var mask = 1UL << (participantId - 1);
        if ((arrivals & mask) != 0)
        {
            throw new InvalidOperationException(
                $"A receiver entered the same shared AAC {phase} twice.");
        }
        arrivals |= mask;
    }

    private void ThrowIfFailed()
    {
        if (failure is not null)
        {
            throw new InvalidOperationException("Shared AAC coordination has failed.", failure);
        }
    }

    private sealed class GroupState(int participantCount)
    {
        public int ParticipantCount { get; } = participantCount;
        public int RegisteredCount { get; set; }
        public MiPlayEncodedAccessUnit? SharedAccessUnit { get; set; }
    }

    private sealed record FrameBundle(
        IReadOnlyDictionary<AudioChannelRoute, MiPlayEncodedAccessUnit> AccessUnits,
        ulong PresentationTimeMicroseconds,
        bool ReanchorPresentationTimeline);
}

internal readonly record struct MiPlaySharedAccessUnitParticipant(
    AudioChannelRoute Route,
    int ParticipantId,
    bool IsLeader);

internal readonly record struct MiPlaySharedAccessUnit(
    byte[] Bytes,
    ulong PresentationTimeMicroseconds,
    bool ReanchorPresentationTimeline);

internal readonly record struct MiPlaySharedSendCompletion(
    long MediaStartedAt,
    double SendCompletionSkewMilliseconds,
    bool TimelineRebased,
    uint TimelineAdvance90Khz,
    ulong ReferenceMonotonicMicroseconds);
