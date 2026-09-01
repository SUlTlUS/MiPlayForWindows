using DLNACast.Core.Audio;

namespace DLNACast.Core.MiPlay;

/// <summary>
/// Publishes one encoded frame to every active receiver without creating a
/// second AAC FIFO per receiver. The publisher advances only after all active
/// members have taken the same route-specific frame, matching the tablet's
/// single writeData call over its native audio-sender group.
/// </summary>
internal sealed class MiPlaySharedAccessUnitBroadcast
{
    internal const int RetainedAccessUnitCount = 64;
    private readonly Lock gate = new();
    private readonly int capacity;
    private readonly Dictionary<int, ParticipantState> activeParticipants = [];
    private readonly Queue<PublishedHistoryFrame> history = new(RetainedAccessUnitCount);
    private TaskCompletionSource framePublished =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource historyPublished =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private PublishedFrame? currentFrame;
    private int nextParticipantId;
    private long nextAccessUnitIndex;
    private Exception? failure;

    public MiPlaySharedAccessUnitBroadcast(int capacity)
    {
        if (capacity is < 2 or > 63)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }
        this.capacity = capacity;
    }

    public MiPlaySharedBroadcastParticipant Register(AudioChannelRoute route) =>
        Register(route, null);

    public MiPlaySharedBroadcastParticipant Register(
        AudioChannelRoute route,
        Action<long>? beforeActivate)
    {
        if (!Enum.IsDefined(route))
        {
            throw new ArgumentOutOfRangeException(nameof(route));
        }

        lock (gate)
        {
            ThrowIfFailed();
            if (activeParticipants.Count >= capacity)
            {
                throw new InvalidOperationException(
                    $"The shared access-unit broadcast accepts at most {capacity} active receivers.");
            }

            var startAccessUnitIndex = nextAccessUnitIndex;
            beforeActivate?.Invoke(startAccessUnitIndex);
            var participant = new MiPlaySharedBroadcastParticipant(
                ++nextParticipantId,
                route,
                startAccessUnitIndex);
            activeParticipants.Add(
                participant.ParticipantId,
                new ParticipantState(route, startAccessUnitIndex));
            return participant;
        }
    }

    public async Task<IReadOnlyList<MiPlaySharedIndexedAccessUnit>> SnapshotHistoryAsync(
        AudioChannelRoute route,
        int count,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(route))
        {
            throw new ArgumentOutOfRangeException(nameof(route));
        }
        if (count is < 1 or > RetainedAccessUnitCount)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        while (true)
        {
            Task waitTask;
            lock (gate)
            {
                ThrowIfFailed();
                if (history.Count >= count)
                {
                    return history
                        .TakeLast(count)
                        .Select(frame => new MiPlaySharedIndexedAccessUnit(
                            frame.AccessUnitIndex,
                            frame.RouteAccessUnits[route],
                            ParticipatesInSendBarrier: false))
                        .ToArray();
                }
                waitTask = historyPublished.Task;
            }
            await waitTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task PublishAsync(
        IReadOnlyDictionary<AudioChannelRoute, MiPlayEncodedAccessUnit> routeAccessUnits,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(routeAccessUnits);
        if (routeAccessUnits.Count == 0)
        {
            throw new ArgumentException(
                "A shared MiPlay frame must contain at least one channel route.",
                nameof(routeAccessUnits));
        }

        PublishedFrame frame;
        TaskCompletionSource published;
        TaskCompletionSource historyAvailable;
        lock (gate)
        {
            ThrowIfFailed();
            if (currentFrame is not null)
            {
                throw new InvalidOperationException(
                    "The previous shared MiPlay access unit has not been consumed by every active receiver.");
            }
            if (activeParticipants.Count == 0)
            {
                return;
            }

            var missingRoute = activeParticipants.Values
                .Select(value => (AudioChannelRoute?)value.Route)
                .FirstOrDefault(route =>
                    route.HasValue && !routeAccessUnits.ContainsKey(route.Value));
            if (missingRoute is AudioChannelRoute missing)
            {
                throw new InvalidOperationException(
                    $"The shared MiPlay frame did not contain the active route {missing}.");
            }

            frame = new PublishedFrame(
                nextAccessUnitIndex++,
                routeAccessUnits.ToDictionary(pair => pair.Key, pair => pair.Value),
                [.. activeParticipants.Keys],
                new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
            currentFrame = frame;
            history.Enqueue(new PublishedHistoryFrame(
                frame.AccessUnitIndex,
                frame.RouteAccessUnits));
            while (history.Count > RetainedAccessUnitCount)
            {
                history.Dequeue();
            }
            published = framePublished;
            framePublished = new(TaskCreationOptions.RunContinuationsAsynchronously);
            historyAvailable = historyPublished;
            historyPublished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        published.TrySetResult();
        historyAvailable.TrySetResult();
        await frame.Consumed.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<MiPlayEncodedAccessUnit> ReadAsync(
        MiPlaySharedBroadcastParticipant participant,
        CancellationToken cancellationToken) =>
        (await ReadIndexedAsync(participant, cancellationToken).ConfigureAwait(false)).AccessUnit;

    public async ValueTask<MiPlaySharedIndexedAccessUnit> ReadIndexedAsync(
        MiPlaySharedBroadcastParticipant participant,
        CancellationToken cancellationToken)
    {
        var countedStarvation = false;
        while (true)
        {
            Task waitTask;
            TaskCompletionSource? consumed = null;
            MiPlaySharedIndexedAccessUnit accessUnit = default;
            var hasAccessUnit = false;
            lock (gate)
            {
                ThrowIfFailed();
                if (!activeParticipants.TryGetValue(
                        participant.ParticipantId,
                        out var participantState) ||
                    participantState.Route != participant.Route)
                {
                    throw new OperationCanceledException(
                        "The MiPlay receiver is no longer part of the shared access-unit group.");
                }

                if (currentFrame is not null &&
                    participantState.NextAccessUnitIndex == currentFrame.AccessUnitIndex)
                {
                    if (!currentFrame.RemainingParticipants.Remove(participant.ParticipantId))
                    {
                        throw new InvalidOperationException(
                            "A MiPlay receiver read the same shared access unit twice.");
                    }

                    accessUnit = new MiPlaySharedIndexedAccessUnit(
                        currentFrame.AccessUnitIndex,
                        currentFrame.RouteAccessUnits[participantState.Route],
                        ParticipatesInSendBarrier: true);
                    participantState.NextAccessUnitIndex++;
                    hasAccessUnit = true;
                    if (currentFrame.RemainingParticipants.Count == 0)
                    {
                        consumed = currentFrame.Consumed;
                        currentFrame = null;
                    }
                    waitTask = Task.CompletedTask;
                }
                else
                {
                    if (!countedStarvation)
                    {
                        participantState.StarvationCount++;
                        countedStarvation = true;
                    }
                    waitTask = framePublished.Task;
                }
            }

            consumed?.TrySetResult();
            if (hasAccessUnit)
            {
                return accessUnit;
            }
            await waitTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public int BufferedCount(MiPlaySharedBroadcastParticipant participant)
    {
        lock (gate)
        {
            return activeParticipants.TryGetValue(participant.ParticipantId, out var state) &&
                   currentFrame is not null &&
                   state.NextAccessUnitIndex == currentFrame.AccessUnitIndex
                ? 1
                : 0;
        }
    }

    public long StarvationCount(MiPlaySharedBroadcastParticipant participant)
    {
        lock (gate)
        {
            return activeParticipants.TryGetValue(participant.ParticipantId, out var state)
                ? state.StarvationCount
                : 0;
        }
    }

    public void Unregister(MiPlaySharedBroadcastParticipant participant)
    {
        TaskCompletionSource? consumed = null;
        lock (gate)
        {
            if (!activeParticipants.Remove(participant.ParticipantId))
            {
                return;
            }

            if (currentFrame is not null)
            {
                currentFrame.RemainingParticipants.Remove(participant.ParticipantId);
                if (currentFrame.RemainingParticipants.Count == 0)
                {
                    consumed = currentFrame.Consumed;
                    currentFrame = null;
                }
            }
        }
        consumed?.TrySetResult();
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
            framePublished.TrySetException(exception);
            historyPublished.TrySetException(exception);
            currentFrame?.Consumed.TrySetException(exception);
            currentFrame = null;
        }
    }

    private void ThrowIfFailed()
    {
        if (failure is not null)
        {
            throw new InvalidOperationException(
                "Shared MiPlay access-unit broadcasting failed.",
                failure);
        }
    }

    private sealed class ParticipantState(
        AudioChannelRoute route,
        long nextAccessUnitIndex)
    {
        public AudioChannelRoute Route { get; } = route;
        public long NextAccessUnitIndex { get; set; } = nextAccessUnitIndex;
        public long StarvationCount { get; set; }
    }

    private sealed record PublishedFrame(
        long AccessUnitIndex,
        IReadOnlyDictionary<AudioChannelRoute, MiPlayEncodedAccessUnit> RouteAccessUnits,
        HashSet<int> RemainingParticipants,
        TaskCompletionSource Consumed);

    private sealed record PublishedHistoryFrame(
        long AccessUnitIndex,
        IReadOnlyDictionary<AudioChannelRoute, MiPlayEncodedAccessUnit> RouteAccessUnits);
}

internal readonly record struct MiPlaySharedBroadcastParticipant(
    int ParticipantId,
    AudioChannelRoute Route,
    long StartAccessUnitIndex);

internal readonly record struct MiPlaySharedIndexedAccessUnit(
    long AccessUnitIndex,
    MiPlayEncodedAccessUnit AccessUnit,
    bool ParticipatesInSendBarrier);
