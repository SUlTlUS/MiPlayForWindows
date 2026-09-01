namespace DLNACast.Core.MiPlay;

/// <summary>
/// Lets aac_mf stay warm while pre-play output is continuously drained. Only
/// access units produced after every receiver reaches media readiness are
/// published to the live session.
/// </summary>
internal sealed class MiPlayEncoderOutputGate
{
    private long minimumPresentationTimeMicroseconds;
    private int armed;

    public bool IsArmed => Volatile.Read(ref armed) != 0;

    public bool TryArm(ulong minimumPresentationTimeMicroseconds)
    {
        if (minimumPresentationTimeMicroseconds > long.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumPresentationTimeMicroseconds));
        }
        if (Interlocked.CompareExchange(ref armed, -1, 0) != 0)
        {
            return false;
        }

        Volatile.Write(
            ref this.minimumPresentationTimeMicroseconds,
            checked((long)minimumPresentationTimeMicroseconds));
        Volatile.Write(ref armed, 1);
        return true;
    }

    public bool ShouldPublish(ulong presentationTimeMicroseconds) =>
        Volatile.Read(ref armed) == 1 &&
        presentationTimeMicroseconds >= checked((ulong)Volatile.Read(
            ref minimumPresentationTimeMicroseconds));
}
