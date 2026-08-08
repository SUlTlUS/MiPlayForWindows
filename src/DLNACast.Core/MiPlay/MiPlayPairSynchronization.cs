namespace DLNACast.Core.MiPlay;

/// <summary>
/// Coordinates the two independently authenticated legacy MiPlay sessions used
/// for a left/right speaker pair. The first gate aligns Open; the second aligns
/// the beginning of media delivery after both reverse channels are ready.
/// </summary>
public sealed class MiPlayPairSynchronization
{
    private readonly PhaseBarrier openBarrier = new(2);
    private readonly PhaseBarrier mediaBarrier = new(2);

    public Task SynchronizeOpenAsync(CancellationToken cancellationToken = default) =>
        openBarrier.SignalAndWaitAsync(cancellationToken);

    public Task SynchronizeMediaAsync(CancellationToken cancellationToken = default) =>
        mediaBarrier.SignalAndWaitAsync(cancellationToken);

    public void Break(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        openBarrier.Break(exception);
        mediaBarrier.Break(exception);
    }

    private sealed class PhaseBarrier(int participantCount)
    {
        private readonly object gate = new();
        private readonly TaskCompletionSource completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int arrivals;
        private Exception? failure;

        public Task SignalAndWaitAsync(CancellationToken cancellationToken)
        {
            lock (gate)
            {
                if (failure is not null)
                {
                    return Task.FromException(failure);
                }
                arrivals++;
                if (arrivals > participantCount)
                {
                    return Task.FromException(new InvalidOperationException(
                        "A MiPlay pair synchronization phase accepts exactly two sessions."));
                }
                if (arrivals == participantCount)
                {
                    completion.TrySetResult();
                }
            }
            return completion.Task.WaitAsync(cancellationToken);
        }

        public void Break(Exception exception)
        {
            lock (gate)
            {
                if (failure is not null) return;
                failure = exception;
                if (arrivals > 0)
                {
                    completion.TrySetException(exception);
                }
            }
        }
    }
}
