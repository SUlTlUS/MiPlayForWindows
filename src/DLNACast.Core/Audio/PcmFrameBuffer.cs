using System.Diagnostics;

namespace DLNACast.Core.Audio;

public sealed class PcmFrameBuffer : IAsyncDisposable
{
    public const int SampleRate = 44_100;
    public const int Channels = 2;
    public const int BitsPerSample = 16;
    public const int FrameMilliseconds = 20;
    public const int TargetBufferMilliseconds = 60;
    public const int MaximumBufferMilliseconds = 100;
    public const int BytesPerSample = BitsPerSample / 8;
    public const int BytesPerFrame = SampleRate * Channels * BytesPerSample * FrameMilliseconds / 1000;
    public const int TargetBufferFrames = TargetBufferMilliseconds / FrameMilliseconds;

    private static readonly byte[] SilenceFrame = new byte[BytesPerFrame];
    private static readonly long FrameTimestampTicks = Math.Max(
        1,
        Stopwatch.Frequency * FrameMilliseconds / 1000);

    private readonly object _queueGate = new();
    private readonly Queue<byte[]> _queue = new();
    private readonly SemaphoreSlim _readClockGate = new(1, 1);
    private readonly int _capacityFrames;
    private readonly AudioChannelRoute _channelRoute;
    private long _overruns;
    private long _underruns;
    private long _nextReadTimestamp;
    private bool _completed;

    public PcmFrameBuffer(int maximumMilliseconds = MaximumBufferMilliseconds)
        : this(AudioChannelRoute.Stereo, maximumMilliseconds)
    {
    }

    public PcmFrameBuffer(
        AudioChannelRoute channelRoute,
        int maximumMilliseconds = MaximumBufferMilliseconds)
    {
        if (!Enum.IsDefined(channelRoute)) throw new ArgumentOutOfRangeException(nameof(channelRoute));
        _channelRoute = channelRoute;
        _capacityFrames = Math.Max(1, maximumMilliseconds / FrameMilliseconds);
    }

    public int BufferedMilliseconds
    {
        get
        {
            lock (_queueGate)
            {
                return _queue.Count * FrameMilliseconds;
            }
        }
    }

    public long Overruns => Interlocked.Read(ref _overruns);
    public long Underruns => Interlocked.Read(ref _underruns);

    public void ResetStatistics()
    {
        Interlocked.Exchange(ref _overruns, 0);
        Interlocked.Exchange(ref _underruns, 0);
    }

    public int TrimToLatest(int framesToKeep = 1)
    {
        framesToKeep = Math.Clamp(framesToKeep, 0, _capacityFrames);
        lock (_queueGate)
        {
            var removed = 0;
            while (_queue.Count > framesToKeep)
            {
                _queue.Dequeue();
                removed++;
            }
            return removed;
        }
    }

    public void Write(ReadOnlySpan<byte> pcmFrame)
    {
        if (pcmFrame.Length != BytesPerFrame) return;

        var copy = pcmFrame.ToArray();
        RouteChannelInPlace(copy);
        lock (_queueGate)
        {
            if (_completed) return;
            if (_queue.Count == _capacityFrames)
            {
                _queue.Dequeue();
                Interlocked.Increment(ref _overruns);
            }
            _queue.Enqueue(copy);
        }
    }

    private void RouteChannelInPlace(Span<byte> pcmFrame)
    {
        if (_channelRoute == AudioChannelRoute.Stereo) return;

        var sourceChannelOffset = _channelRoute == AudioChannelRoute.LeftAsMono
            ? 0
            : BytesPerSample;
        var sampleStride = Channels * BytesPerSample;
        for (var offset = 0; offset < pcmFrame.Length; offset += sampleStride)
        {
            var source = offset + sourceChannelOffset;
            var low = pcmFrame[source];
            var high = pcmFrame[source + 1];
            pcmFrame[offset] = low;
            pcmFrame[offset + 1] = high;
            pcmFrame[offset + BytesPerSample] = low;
            pcmFrame[offset + BytesPerSample + 1] = high;
        }
    }

    public async ValueTask PrepareForPlaybackAsync(
        int targetMilliseconds = TargetBufferMilliseconds,
        int maximumWaitMilliseconds = MaximumBufferMilliseconds,
        CancellationToken cancellationToken = default)
    {
        var targetFrames = Math.Clamp(
            (int)Math.Ceiling(targetMilliseconds / (double)FrameMilliseconds),
            1,
            _capacityFrames);
        var startedAt = Stopwatch.GetTimestamp();
        var maximumWaitTicks = Stopwatch.Frequency * Math.Max(0, maximumWaitMilliseconds) / 1000;

        while (GetQueuedFrames() < targetFrames &&
               Stopwatch.GetTimestamp() - startedAt < maximumWaitTicks &&
               !IsCompleted())
        {
            await Task.Delay(2, cancellationToken).ConfigureAwait(false);
        }

        Interlocked.Exchange(ref _nextReadTimestamp, 0);
    }

    public async ValueTask<byte[]> ReadFrameOrSilenceAsync(CancellationToken cancellationToken)
    {
        await _readClockGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = Stopwatch.GetTimestamp();
            var next = Interlocked.Read(ref _nextReadTimestamp);
            if (next == 0)
            {
                next = now;
            }
            else if (next > now)
            {
                await Task.Delay(
                    TimeSpan.FromSeconds((next - now) / (double)Stopwatch.Frequency),
                    cancellationToken).ConfigureAwait(false);
                now = Stopwatch.GetTimestamp();
            }

            // Do not burst several frames after a temporarily blocked socket write.
            if (now - next > FrameTimestampTicks)
            {
                next = now;
            }
            Interlocked.Exchange(ref _nextReadTimestamp, next + FrameTimestampTicks);

            lock (_queueGate)
            {
                if (_queue.Count > 0)
                {
                    return _queue.Dequeue();
                }
            }

            Interlocked.Increment(ref _underruns);
            return SilenceFrame;
        }
        finally
        {
            _readClockGate.Release();
        }
    }

    public void Complete()
    {
        lock (_queueGate)
        {
            _completed = true;
            _queue.Clear();
        }
    }

    private int GetQueuedFrames()
    {
        lock (_queueGate) return _queue.Count;
    }

    private bool IsCompleted()
    {
        lock (_queueGate) return _completed;
    }

    public ValueTask DisposeAsync()
    {
        Complete();
        return ValueTask.CompletedTask;
    }
}
