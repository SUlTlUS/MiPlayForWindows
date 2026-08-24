using System.Runtime.ExceptionServices;
using System.Threading.Channels;

namespace DLNACast.Core.MiPlay;

/// <summary>
/// Smooths bursty encoder output without dropping, replacing, or reordering
/// any AAC access unit. Backpressure is applied when the bounded FIFO is full.
/// </summary>
internal sealed class MiPlayOrderedAccessUnitBuffer
{
    public const int DefaultCapacity = 16;

    private readonly Channel<byte[]> channel;
    private long starvationCount;

    public MiPlayOrderedAccessUnitBuffer(int capacity = DefaultCapacity)
    {
        if (capacity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        channel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = false,
        });
    }

    public int BufferedCount => channel.Reader.Count;

    public long StarvationCount => Interlocked.Read(ref starvationCount);

    public ValueTask WriteAsync(
        byte[] accessUnit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(accessUnit);
        if (accessUnit.Length == 0)
        {
            throw new ArgumentException("An AAC access unit cannot be empty.", nameof(accessUnit));
        }
        return channel.Writer.WriteAsync(accessUnit, cancellationToken);
    }

    public async ValueTask<byte[]> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (channel.Reader.TryRead(out var buffered))
        {
            return buffered;
        }

        Interlocked.Increment(ref starvationCount);
        try
        {
            return await channel.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ChannelClosedException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
        catch (ChannelClosedException exception)
        {
            throw new EndOfStreamException(
                "FFmpeg ended while the MiPlay media session was active.",
                exception);
        }
    }

    public void Complete(Exception? exception = null) =>
        channel.Writer.TryComplete(exception);
}
