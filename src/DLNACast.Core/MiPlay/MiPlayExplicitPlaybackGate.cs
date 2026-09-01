namespace DLNACast.Core.MiPlay;

/// <summary>
/// Stops one capture, encoder, or media-output stage only for an explicit user
/// Pause. Ordinary capture underruns remain outside these gates and continue
/// to produce AAC silence.
/// </summary>
internal sealed class MiPlayExplicitPlaybackGate
{
    private readonly Lock gate = new();
    private TaskCompletionSource resumed = CreateCompletedSource();
    private bool paused;

    internal bool IsPaused
    {
        get
        {
            lock (gate)
            {
                return paused;
            }
        }
    }

    internal void Pause()
    {
        lock (gate)
        {
            if (paused)
            {
                return;
            }

            paused = true;
            resumed = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    internal void Resume()
    {
        TaskCompletionSource? release = null;
        lock (gate)
        {
            if (!paused)
            {
                return;
            }

            paused = false;
            release = resumed;
        }
        release.TrySetResult();
    }

    internal Task WaitUntilResumedAsync(CancellationToken cancellationToken)
    {
        Task wait;
        lock (gate)
        {
            wait = resumed.Task;
        }
        return wait.WaitAsync(cancellationToken);
    }

    private static TaskCompletionSource CreateCompletedSource()
    {
        var source = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        source.SetResult();
        return source;
    }
}
