namespace DLNACast.Core.MiPlay;

/// <summary>
/// Bounds the whole PCM-to-AAC pipeline, including FFmpeg's hidden queues.
/// A slot belongs to an input frame until its AAC output is consumed by the
/// route fan-out, not merely until FFmpeg produces the encoded bytes.
/// </summary>
internal sealed class MiPlayEncoderInputWindow : IDisposable
{
    // A standalone aac_mf process can emit after fewer frames, but the real
    // two-route shared session does not cross both encoders' startup threshold
    // with smaller windows. Keep the twelve-frame capacity proven to start both
    // routes without starving either side of the stereo group.
    internal const int DefaultCapacity = 12;

    private readonly int capacity;
    private readonly SemaphoreSlim slots;

    internal MiPlayEncoderInputWindow(int capacity = DefaultCapacity)
    {
        if (capacity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }
        this.capacity = capacity;
        slots = new SemaphoreSlim(capacity, capacity);
    }

    internal int PendingFrameCount => capacity - slots.CurrentCount;

    internal Task AcquireAsync(CancellationToken cancellationToken) =>
        slots.WaitAsync(cancellationToken);

    internal void ReleaseConsumedFrame() => slots.Release();

    public void Dispose() => slots.Dispose();
}
