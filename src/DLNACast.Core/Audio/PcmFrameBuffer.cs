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

    private readonly Lock _queueGate = new();
    private readonly Queue<PcmFrameRead> _queue = new();
    private readonly SemaphoreSlim _readClockGate = new(1, 1);
    private TaskCompletionSource _frameAvailable = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly int _capacityFrames;
    private readonly AudioChannelRoute _channelRoute;
    private readonly byte[] _silenceFrame;
    private readonly long _frameTimestampTicks;
    private readonly bool _preserveFrameSequence;
    private readonly SemaphoreSlim? _writableSlots;
    private PendingLatencyProbe? _latencyProbe;
    private long _overruns;
    private long _underruns;
    private long _nextReadTimestamp;
    private bool _completed;

    public PcmFrameBuffer(int maximumMilliseconds = MaximumBufferMilliseconds)
        : this(AudioChannelRoute.Stereo, PcmFrameFormat.Default, maximumMilliseconds)
    {
    }

    public PcmFrameBuffer(
        AudioChannelRoute channelRoute,
        int maximumMilliseconds = MaximumBufferMilliseconds)
        : this(channelRoute, PcmFrameFormat.Default, maximumMilliseconds)
    {
    }

    public PcmFrameBuffer(
        AudioChannelRoute channelRoute,
        PcmFrameFormat format,
        int maximumMilliseconds = MaximumBufferMilliseconds,
        bool preserveFrameSequence = false)
    {
        if (!Enum.IsDefined(channelRoute)) throw new ArgumentOutOfRangeException(nameof(channelRoute));
        ArgumentNullException.ThrowIfNull(format);
        if (format.Channels != Channels ||
            format.BitsPerSample != BitsPerSample)
        {
            throw new ArgumentException("PCM frame buffers require stereo signed-16 samples.", nameof(format));
        }
        if (maximumMilliseconds <= 0) throw new ArgumentOutOfRangeException(nameof(maximumMilliseconds));

        _channelRoute = channelRoute;
        Format = format;
        _preserveFrameSequence = preserveFrameSequence;
        _capacityFrames = Math.Max(
            1,
            (int)Math.Floor(maximumMilliseconds / format.FrameDurationMilliseconds));
        _writableSlots = preserveFrameSequence
            ? new SemaphoreSlim(_capacityFrames, _capacityFrames)
            : null;
        _silenceFrame = new byte[format.BytesPerFrame];
        _frameTimestampTicks = Math.Max(
            1,
            checked((long)Math.Round(
                Stopwatch.Frequency * format.SamplesPerFrame / (double)format.SampleRate)));
    }

    public PcmFrameFormat Format { get; }
    public int SampleRateHz => Format.SampleRate;
    public int SamplesPerFrame => Format.SamplesPerFrame;
    public int FrameByteCount => Format.BytesPerFrame;
    public double FrameDurationMilliseconds => Format.FrameDurationMilliseconds;

    public int BufferedMilliseconds
    {
        get
        {
            lock (_queueGate)
            {
                return checked((int)Math.Round(_queue.Count * FrameDurationMilliseconds));
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
                _writableSlots?.Release();
                removed++;
            }
            return removed;
        }
    }

    public void Write(ReadOnlySpan<byte> pcmFrame)
    {
        if (pcmFrame.Length != FrameByteCount) return;
        Write(
            pcmFrame,
            PcmPresentationTimestamp.CalculateMicroseconds(
                PcmPresentationTimestamp.GetElapsedRealtimeNanoseconds(),
                pcmFrame.Length,
                Format));
    }

    public void Write(
        ReadOnlySpan<byte> pcmFrame,
        ulong presentationTimeMicroseconds,
        bool reanchorPresentationTimeline = false,
        bool captureDiscontinuity = false)
    {
        if (pcmFrame.Length != FrameByteCount) return;
        if (_preserveFrameSequence)
        {
            throw new InvalidOperationException(
                "Sequence-preserving PCM buffers require asynchronous writes.");
        }

        var frame = CreateRoutedFrame(
            pcmFrame,
            presentationTimeMicroseconds,
            reanchorPresentationTimeline,
            captureDiscontinuity);
        lock (_queueGate)
        {
            if (_completed) return;
            if (_queue.Count == _capacityFrames)
            {
                _queue.Dequeue();
                Interlocked.Increment(ref _overruns);
            }
            _queue.Enqueue(frame);
        }
    }

    internal async ValueTask WritePreservingSequenceAsync(
        PcmFrameRead frame,
        CancellationToken cancellationToken)
    {
        if (!_preserveFrameSequence || _writableSlots is null)
        {
            Write(
                frame.Bytes,
                frame.PresentationTimeMicroseconds,
                frame.ReanchorPresentationTimeline,
                frame.CaptureDiscontinuity);
            return;
        }
        if (frame.Bytes.Length != FrameByteCount)
        {
            throw new ArgumentException(
                $"PCM frames must contain exactly {FrameByteCount} bytes.",
                nameof(frame));
        }

        await _writableSlots.WaitAsync(cancellationToken).ConfigureAwait(false);
        var releaseSlot = true;
        try
        {
            var routed = CreateRoutedFrame(
                frame.Bytes,
                frame.PresentationTimeMicroseconds,
                frame.ReanchorPresentationTimeline,
                frame.CaptureDiscontinuity);
            lock (_queueGate)
            {
                if (_completed)
                {
                    return;
                }
                _queue.Enqueue(routed);
                _frameAvailable.TrySetResult();
                releaseSlot = false;
            }
        }
        finally
        {
            if (releaseSlot)
            {
                _writableSlots.Release();
            }
        }
    }

    private PcmFrameRead CreateRoutedFrame(
        ReadOnlySpan<byte> pcmFrame,
        ulong presentationTimeMicroseconds,
        bool reanchorPresentationTimeline,
        bool captureDiscontinuity)
    {
        var copy = pcmFrame.ToArray();
        RouteChannelInPlace(copy);
        return new PcmFrameRead(
            copy,
            presentationTimeMicroseconds,
            IsBufferUnderrunSilence: false,
            CaptureDiscontinuity: captureDiscontinuity,
            ReanchorPresentationTimeline: reanchorPresentationTimeline);
    }

    public LatencyProbePlayback ScheduleLatencyProbe()
    {
        lock (_queueGate)
        {
            ObjectDisposedException.ThrowIf(_completed, this);
            if (_latencyProbe is not null)
            {
                throw new InvalidOperationException("A latency probe is already active.");
            }

            var playback = new LatencyProbePlayback(CancelLatencyProbe);
            _latencyProbe = new PendingLatencyProbe(
                AcousticLatencyProbeSignal.CreateStereoFrames(Format),
                playback);
            return playback;
        }
    }

    private void CancelLatencyProbe(LatencyProbePlayback playback)
    {
        lock (_queueGate)
        {
            if (!ReferenceEquals(_latencyProbe?.Playback, playback)) return;
            _latencyProbe = null;
            playback.MarkFailed(new OperationCanceledException("The latency probe was canceled."));
        }
    }

    private void RouteChannelInPlace(Span<byte> pcmFrame)
    {
        if (_channelRoute == AudioChannelRoute.Stereo) return;

        var sourceChannelOffset = _channelRoute == AudioChannelRoute.LeftAsMono
            ? 0
            : Format.BytesPerSample;
        var sampleStride = Format.Channels * Format.BytesPerSample;
        for (var offset = 0; offset < pcmFrame.Length; offset += sampleStride)
        {
            var source = offset + sourceChannelOffset;
            var low = pcmFrame[source];
            var high = pcmFrame[source + 1];
            pcmFrame[offset] = low;
            pcmFrame[offset + 1] = high;
            pcmFrame[offset + Format.BytesPerSample] = low;
            pcmFrame[offset + Format.BytesPerSample + 1] = high;
        }
    }

    public async ValueTask PrepareForPlaybackAsync(
        int targetMilliseconds = TargetBufferMilliseconds,
        int maximumWaitMilliseconds = MaximumBufferMilliseconds,
        CancellationToken cancellationToken = default)
    {
        var targetFrames = Math.Clamp(
            (int)Math.Ceiling(targetMilliseconds / FrameDurationMilliseconds),
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

    /// <summary>
    /// Reads exactly the next frame from a shared producer. The producer owns
    /// cadence and recovery silence; a route must not add its own timed zero
    /// frame or pacing clock when another route temporarily runs late.
    /// </summary>
    internal async ValueTask<PcmFrameRead> ReadFramePreservingSequenceAsync(CancellationToken cancellationToken)
    {
        if (!_preserveFrameSequence)
        {
            throw new InvalidOperationException("An unpaced read requires a sequence-preserving PCM buffer.");
        }
        await _readClockGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            while (true)
            {
                Task available;
                lock (_queueGate)
                {
                    if (_queue.Count > 0)
                    {
                        var frame = _queue.Dequeue();
                        _writableSlots!.Release();
                        return frame;
                    }
                    if (_completed)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        throw new EndOfStreamException("The shared PCM producer completed.");
                    }
                    if (_frameAvailable.Task.IsCompleted)
                    {
                        _frameAvailable = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    }
                    available = _frameAvailable.Task;
                }
                await available.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _readClockGate.Release();
        }
    }

    public async ValueTask<byte[]> ReadFrameOrSilenceAsync(CancellationToken cancellationToken)
    {
        var frame = await ReadFrameWithTimingOrSilenceAsync(cancellationToken).ConfigureAwait(false);
        return frame.Bytes;
    }

    public async ValueTask<PcmFrameRead> ReadFrameWithTimingOrSilenceAsync(
        CancellationToken cancellationToken)
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
            if (now - next > _frameTimestampTicks)
            {
                next = now;
            }
            Interlocked.Exchange(ref _nextReadTimestamp, next + _frameTimestampTicks);

            lock (_queueGate)
            {
                var capturedFrame = _queue.Count > 0
                    ? _queue.Dequeue()
                    : (PcmFrameRead?)null;
                if (_latencyProbe is { } probe)
                {
                    var frameIndex = probe.FrameIndex++;
                    if (frameIndex == AcousticLatencyProbeSignal.ProbeStartFrame)
                    {
                        probe.Playback.MarkStarted(Stopwatch.GetTimestamp());
                    }
                    var probeFrame = probe.Frames[frameIndex];
                    if (probe.FrameIndex == probe.Frames.Count)
                    {
                        _latencyProbe = null;
                    }
                    return CreateLiveFrame(probeFrame);
                }
                if (capturedFrame is PcmFrameRead captured)
                {
                    _writableSlots?.Release();
                    return captured;
                }
            }

            Interlocked.Increment(ref _underruns);
            return CreateLiveFrame(_silenceFrame, isBufferUnderrunSilence: true);
        }
        finally
        {
            _readClockGate.Release();
        }
    }

    private PcmFrameRead CreateLiveFrame(
        byte[] bytes,
        bool isBufferUnderrunSilence = false) => new(
        bytes,
        PcmPresentationTimestamp.CalculateMicroseconds(
            PcmPresentationTimestamp.GetElapsedRealtimeNanoseconds(),
            bytes.Length,
            Format),
        isBufferUnderrunSilence);

    public void Complete()
    {
        lock (_queueGate)
        {
            _completed = true;
            _frameAvailable.TrySetResult();
            var discardedFrames = _queue.Count;
            _queue.Clear();
            if (_writableSlots is not null && discardedFrames > 0)
            {
                _writableSlots.Release(discardedFrames);
            }
            _latencyProbe?.Playback.MarkFailed(
                new ObjectDisposedException(nameof(PcmFrameBuffer)));
            _latencyProbe = null;
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

    private sealed class PendingLatencyProbe(
        IReadOnlyList<byte[]> frames,
        LatencyProbePlayback playback)
    {
        public IReadOnlyList<byte[]> Frames { get; } = frames;
        public LatencyProbePlayback Playback { get; } = playback;
        public int FrameIndex { get; set; }
    }
}

public sealed class LatencyProbePlayback
{
    private readonly Action<LatencyProbePlayback> _cancel;
    private readonly TaskCompletionSource<long> _startedAt =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal LatencyProbePlayback(Action<LatencyProbePlayback> cancel)
    {
        _cancel = cancel;
    }

    public Task<long> StartedAt => _startedAt.Task;

    internal void MarkStarted(long timestamp) => _startedAt.TrySetResult(timestamp);

    internal void MarkFailed(Exception exception) => _startedAt.TrySetException(exception);

    internal void Cancel() => _cancel(this);
}
