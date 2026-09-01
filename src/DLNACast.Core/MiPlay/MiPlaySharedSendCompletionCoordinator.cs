namespace DLNACast.Core.MiPlay;

/// <summary>
/// Keeps active receiver sockets on one frame boundary while allowing a native
/// multiAudioSender-style member removal without failing the remaining group.
/// </summary>
internal sealed class MiPlaySharedSendCompletionCoordinator
{
    private readonly Lock gate = new();
    private readonly int capacity;
    private readonly Dictionary<int, long> activeParticipants = [];
    private readonly Dictionary<int, SendObservation> observations = [];
    private TaskCompletionSource<MiPlaySharedSendCompletion> ready =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int nextParticipantId;
    private long nextAccessUnitIndex;
    private Exception? failure;

    public MiPlaySharedSendCompletionCoordinator(int capacity)
    {
        if (capacity is < 2 or > 63)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }
        this.capacity = capacity;
    }

    public MiPlaySharedSendParticipant Register(long startAccessUnitIndex = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(startAccessUnitIndex);
        lock (gate)
        {
            ThrowIfFailed();
            if (activeParticipants.Count >= capacity)
            {
                throw new InvalidOperationException(
                    $"The shared send coordinator accepts at most {capacity} active receivers.");
            }
            var participant = new MiPlaySharedSendParticipant(++nextParticipantId);
            activeParticipants.Add(participant.ParticipantId, startAccessUnitIndex);
            return participant;
        }
    }

    public async Task<MiPlaySharedSendCompletion> SynchronizeAsync(
        MiPlaySharedSendParticipant participant,
        long accessUnitIndex,
        long mediaWriteCompletedAt,
        double sendGapMilliseconds,
        long mediaStartedAt,
        long stopwatchFrequency,
        bool captureTimelineReanchored,
        CancellationToken cancellationToken)
    {
        ValidateObservation(
            mediaWriteCompletedAt,
            sendGapMilliseconds,
            mediaStartedAt,
            stopwatchFrequency);

        Task<MiPlaySharedSendCompletion> waitTask;
        Completion? completion;
        lock (gate)
        {
            ThrowIfFailed();
            if (!activeParticipants.TryGetValue(participant.ParticipantId, out var startAccessUnitIndex))
            {
                throw new OperationCanceledException(
                    "The MiPlay receiver is no longer part of the shared send group.");
            }
            if (accessUnitIndex < startAccessUnitIndex)
            {
                throw new InvalidOperationException(
                    $"The MiPlay receiver joins at access unit {startAccessUnitIndex}, not {accessUnitIndex}.");
            }
            if (accessUnitIndex != nextAccessUnitIndex)
            {
                throw new InvalidOperationException(
                    $"Shared send completion expected access unit {nextAccessUnitIndex}, not {accessUnitIndex}.");
            }
            if (!observations.TryAdd(
                    participant.ParticipantId,
                    new SendObservation(
                        mediaWriteCompletedAt,
                        sendGapMilliseconds,
                        mediaStartedAt,
                        stopwatchFrequency,
                        captureTimelineReanchored)))
            {
                throw new InvalidOperationException(
                    "A receiver entered the same shared send-completion frame twice.");
            }
            waitTask = ready.Task;
            completion = TryCompleteLocked();
        }
        completion?.Source.TrySetResult(completion.Value);
        return await waitTask.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Unregister(MiPlaySharedSendParticipant participant)
    {
        Completion? completion;
        lock (gate)
        {
            if (!activeParticipants.Remove(participant.ParticipantId))
            {
                return;
            }
            observations.Remove(participant.ParticipantId);
            completion = TryCompleteLocked();
        }
        completion?.Source.TrySetResult(completion.Value);
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
            ready.TrySetException(exception);
        }
    }

    private Completion? TryCompleteLocked()
    {
        var currentParticipants = activeParticipants
            .Where(pair => pair.Value <= nextAccessUnitIndex)
            .Select(pair => pair.Key)
            .ToArray();
        if (currentParticipants.Length == 0 ||
            currentParticipants.Any(id => !observations.ContainsKey(id)))
        {
            return null;
        }

        var active = currentParticipants.Select(id => observations[id]).ToArray();
        var frequency = active[0].StopwatchFrequency;
        var mediaStartedAt = active[0].MediaStartedAt;
        var reanchored = active[0].CaptureTimelineReanchored;
        if (active.Any(value =>
                value.StopwatchFrequency != frequency ||
                value.MediaStartedAt != mediaStartedAt ||
                value.CaptureTimelineReanchored != reanchored))
        {
            throw new InvalidOperationException(
                "Active MiPlay receivers disagreed about their shared media timeline.");
        }

        var minimumCompletedAt = active.Min(value => value.MediaWriteCompletedAt);
        var maximumCompletedAt = active.Max(value => value.MediaWriteCompletedAt);
        var rebase = active.Any(value => MiPlayMediaStallRecoveryPlan.ShouldActivate(
            nextAccessUnitIndex,
            value.SendGapMilliseconds));
        var synchronizedMediaStartedAt = rebase
            ? MiPlayMediaStallRecoveryPlan.RebaseMediaStartedAt(
                maximumCompletedAt,
                nextAccessUnitIndex + 1,
                frequency)
            : mediaStartedAt;
        var value = new MiPlaySharedSendCompletion(
            synchronizedMediaStartedAt,
            (maximumCompletedAt - minimumCompletedAt) * 1_000d / frequency,
            rebase,
            MiPlayMediaStallRecoveryPlan.CalculateTimelineAdvance90Khz(
                mediaStartedAt,
                synchronizedMediaStartedAt,
                frequency,
                reanchored),
            MiPlayWfdMediaClock.ConvertStopwatchTicksToMicroseconds(
                maximumCompletedAt,
                frequency));
        var source = ready;
        ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        observations.Clear();
        nextAccessUnitIndex++;
        return new Completion(source, value);
    }

    private static void ValidateObservation(
        long mediaWriteCompletedAt,
        double sendGapMilliseconds,
        long mediaStartedAt,
        long stopwatchFrequency)
    {
        if (mediaWriteCompletedAt < 0) throw new ArgumentOutOfRangeException(nameof(mediaWriteCompletedAt));
        if (double.IsNaN(sendGapMilliseconds) || sendGapMilliseconds < 0)
            throw new ArgumentOutOfRangeException(nameof(sendGapMilliseconds));
        if (mediaStartedAt < 0) throw new ArgumentOutOfRangeException(nameof(mediaStartedAt));
        if (stopwatchFrequency <= 0) throw new ArgumentOutOfRangeException(nameof(stopwatchFrequency));
    }

    private void ThrowIfFailed()
    {
        if (failure is not null)
        {
            throw new InvalidOperationException("Shared MiPlay send coordination failed.", failure);
        }
    }

    private readonly record struct SendObservation(
        long MediaWriteCompletedAt,
        double SendGapMilliseconds,
        long MediaStartedAt,
        long StopwatchFrequency,
        bool CaptureTimelineReanchored);

    private sealed record Completion(
        TaskCompletionSource<MiPlaySharedSendCompletion> Source,
        MiPlaySharedSendCompletion Value);
}

internal readonly record struct MiPlaySharedSendParticipant(int ParticipantId);
