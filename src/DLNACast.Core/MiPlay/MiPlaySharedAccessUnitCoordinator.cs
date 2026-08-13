using DLNACast.Core.Audio;

namespace DLNACast.Core.MiPlay;

internal sealed class MiPlaySharedAccessUnitCoordinator
{
    private readonly Lock gate = new();
    private readonly Dictionary<AudioChannelRoute, GroupState> groups;
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

            var groupParticipantId = ++group.RegisteredCount;
            return new(route, groupParticipantId, IsLeader: groupParticipantId == 1);
        }
    }

    public async Task<byte[]> SynchronizeAsync(
        MiPlaySharedAccessUnitParticipant participant,
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
            if (!groups.TryGetValue(participant.Route, out var group) ||
                participant.GroupParticipantId is < 1 ||
                participant.GroupParticipantId > group.RegisteredCount)
            {
                throw new InvalidOperationException("The shared AAC participant is not registered.");
            }
            if (group.RegisteredCount != group.ParticipantCount)
            {
                throw new InvalidOperationException("The shared AAC group is not fully registered.");
            }
            if (accessUnitIndex != group.NextAccessUnitIndex)
            {
                throw new InvalidOperationException(
                    $"Shared AAC group {participant.Route} expected access unit " +
                    $"{group.NextAccessUnitIndex}, not {accessUnitIndex}.");
            }

            MarkArrival(group, participant.GroupParticipantId);
            if (participant.IsLeader)
            {
                group.SharedAccessUnit = leaderAccessUnit ??
                    throw new InvalidOperationException("The AAC group leader must provide an access unit.");
            }
            else if (leaderAccessUnit is not null)
            {
                throw new InvalidOperationException("Only the AAC group leader may provide an access unit.");
            }

            group.ArrivalCount++;
            waitTask = group.AccessUnitReady.Task;
            if (group.ArrivalCount == group.ParticipantCount)
            {
                completedValue = group.SharedAccessUnit ??
                    throw new InvalidOperationException("The AAC group leader did not provide an access unit.");
                completed = group.AccessUnitReady;
                group.NextAccessUnitIndex++;
                group.Arrivals = 0;
                group.ArrivalCount = 0;
                group.SharedAccessUnit = null;
                group.AccessUnitReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        completed?.TrySetResult(completedValue!);
        try
        {
            return await waitTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Fail(new OperationCanceledException(
                $"A receiver left the shared {participant.Route} AAC group."));
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
                group.AccessUnitReady.TrySetException(exception);
            }
        }
    }

    private static void MarkArrival(GroupState group, int participantId)
    {
        var mask = 1UL << (participantId - 1);
        if ((group.Arrivals & mask) != 0)
        {
            throw new InvalidOperationException("A receiver entered the same shared AAC frame twice.");
        }
        group.Arrivals |= mask;
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
        public long NextAccessUnitIndex { get; set; }
        public ulong Arrivals { get; set; }
        public int ArrivalCount { get; set; }
        public byte[]? SharedAccessUnit { get; set; }
        public TaskCompletionSource<byte[]> AccessUnitReady { get; set; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}

internal readonly record struct MiPlaySharedAccessUnitParticipant(
    AudioChannelRoute Route,
    int GroupParticipantId,
    bool IsLeader);
