using System.Runtime.ExceptionServices;
using System.Threading.Channels;

namespace DLNACast.Core.MiPlay;

/// <summary>
/// Smooths bursty encoder output without dropping, replacing, or reordering
/// any AAC access unit. Backpressure is applied when the bounded FIFO is full.
/// </summary>
internal sealed class MiPlayOrderedAccessUnitBuffer
{
    // Retain enough completed AAC output to smooth Media Foundation's bursty
    // delivery. Encoder input has its own smaller bound; coupling the two
    // capacities unnecessarily keeps old PCM in flight after RTSP startup.
    public const int DefaultCapacity = 12;

    private readonly Channel<MiPlayEncodedAccessUnit> channel;
    private long starvationCount;

    public MiPlayOrderedAccessUnitBuffer(int capacity = DefaultCapacity)
    {
        if (capacity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        channel = Channel.CreateBounded<MiPlayEncodedAccessUnit>(new BoundedChannelOptions(capacity)
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
        MiPlayEncodedAccessUnit accessUnit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(accessUnit.Bytes);
        if (accessUnit.Bytes.Length == 0)
        {
            throw new ArgumentException("An AAC access unit cannot be empty.", nameof(accessUnit));
        }
        return channel.Writer.WriteAsync(accessUnit, cancellationToken);
    }

    public async ValueTask<MiPlayEncodedAccessUnit> ReadAsync(
        CancellationToken cancellationToken = default)
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

internal readonly record struct MiPlayEncodedAccessUnit(
    byte[] Bytes,
    ulong PresentationTimeMicroseconds,
    bool ReanchorPresentationTimeline = false);

internal readonly record struct MiPlayEncoderInputTiming(
    ulong PresentationTimeMicroseconds,
    bool ReanchorPresentationTimeline);
