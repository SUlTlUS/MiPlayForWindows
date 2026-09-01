namespace DLNACast.Core.MiPlay;

/// <summary>
/// Mirrors MultiMirrorDisplay's group Pause/Resume fan-out. A command receives
/// one group generation before it is queued to every currently active sender.
/// </summary>
internal sealed class MiPlaySharedPlaybackControlCoordinator
{
    private readonly Lock gate = new();
    private readonly Dictionary<int, Func<MiPlaySharedPlaybackControlRequest, CancellationToken, Task>>
        controls = [];
    private long nextGeneration;

    public void Register(
        int participantId,
        Func<MiPlaySharedPlaybackControlRequest, CancellationToken, Task> enqueue)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(participantId);
        ArgumentNullException.ThrowIfNull(enqueue);
        lock (gate)
        {
            if (!controls.TryAdd(participantId, enqueue))
            {
                throw new InvalidOperationException(
                    "The MiPlay receiver playback control is already registered.");
            }
        }
    }

    public void Unregister(int participantId)
    {
        lock (gate)
        {
            controls.Remove(participantId);
        }
    }

    public Task BroadcastAsync(
        MiPlaySharedPlaybackControlCommand command,
        CancellationToken cancellationToken,
        bool preserveMediaTimeline = false)
    {
        Func<MiPlaySharedPlaybackControlRequest, CancellationToken, Task>[] snapshot;
        MiPlaySharedPlaybackControlRequest request;
        lock (gate)
        {
            if (controls.Count == 0)
            {
                throw new InvalidOperationException(
                    "No shared MiPlay receiver is ready for playback control.");
            }
            snapshot = controls.Values.ToArray();
            request = new MiPlaySharedPlaybackControlRequest(
                command,
                checked(++nextGeneration),
                preserveMediaTimeline);
        }

        return Task.WhenAll(snapshot.Select(control => control(request, cancellationToken)));
    }
}

internal enum MiPlaySharedPlaybackControlCommand
{
    Pause,
    Resume,
}

internal readonly record struct MiPlaySharedPlaybackControlRequest(
    MiPlaySharedPlaybackControlCommand Command,
    long Generation,
    bool PreserveMediaTimeline = false);
