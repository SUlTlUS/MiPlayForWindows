using DLNACast.Core.Abstractions;
using DLNACast.Core.Audio;
using DLNACast.Core.Models;

namespace DLNACast.Core.MiPlay;

/// <summary>
/// Owns one Windows loopback capture for a multi-receiver MiPlay pair and
/// fans its PCM frames out to independent per-receiver AAC/media pipelines.
/// Sharing capture avoids opening two loopback clients on the virtual speaker;
/// keeping the encoders and packetizers separate preserves the audible path.
/// </summary>
public sealed class MiPlaySharedAudioSession : IAsyncDisposable
{
    private readonly Lock gate = new();
    private readonly int participantCount;
    private readonly CancellationTokenSource lifetime = new();
    private readonly Dictionary<int, PcmFrameBuffer> subscribers = [];
    private readonly TaskCompletionSource openReady =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int nextSubscriberId;
    private int openArrivals;
    private Exception? startupFailure;
    private Task fanOutTask = Task.CompletedTask;
    private int disposed;

    private MiPlaySharedAudioSession(
        int participantCount,
        PcmFrameBuffer sourceBuffer,
        SwitchableAudioCaptureSource capture)
    {
        this.participantCount = participantCount;
        SourceBuffer = sourceBuffer;
        Capture = capture;
    }

    private PcmFrameBuffer SourceBuffer { get; }
    internal SwitchableAudioCaptureSource Capture { get; }

    public static async Task<MiPlaySharedAudioSession> StartAsync(
        IAudioSourceCatalog audioSources,
        CaptureSelection selection,
        int participantCount = 2,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(audioSources);
        ArgumentNullException.ThrowIfNull(selection);
        if (participantCount < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(participantCount));
        }

        var sourceBuffer = new PcmFrameBuffer(AudioChannelRoute.Stereo);
        var capture = new SwitchableAudioCaptureSource(audioSources, selection);
        try
        {
            await capture.StartAsync(sourceBuffer, cancellationToken).ConfigureAwait(false);
            await sourceBuffer.PrepareForPlaybackAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return new MiPlaySharedAudioSession(participantCount, sourceBuffer, capture);
        }
        catch
        {
            await capture.StopAsync().ConfigureAwait(false);
            await capture.DisposeAsync().ConfigureAwait(false);
            await sourceBuffer.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    internal MiPlaySharedAudioSubscription Subscribe()
    {
        lock (gate)
        {
            ThrowIfDisposed();
            if (startupFailure is not null)
            {
                throw new InvalidOperationException("The shared MiPlay capture has failed.", startupFailure);
            }
            if (nextSubscriberId >= participantCount)
            {
                throw new InvalidOperationException(
                    $"The shared MiPlay capture accepts exactly {participantCount} receivers.");
            }

            var id = ++nextSubscriberId;
            var buffer = new PcmFrameBuffer(AudioChannelRoute.Stereo);
            subscribers.Add(id, buffer);
            if (subscribers.Count == participantCount)
            {
                SourceBuffer.TrimToLatest(1);
                fanOutTask = Task.Run(() => RunFanOutAsync(lifetime.Token));
            }
            return new MiPlaySharedAudioSubscription(this, id, buffer);
        }
    }

    public async Task SetCaptureSelectionAsync(
        CaptureSelection selection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ThrowIfDisposed();
        if (!Equals(Capture.Selection, selection))
        {
            await Capture.SwitchAsync(selection, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RunFanOutAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var frame = await SourceBuffer.ReadFrameOrSilenceAsync(cancellationToken)
                    .ConfigureAwait(false);
                PcmFrameBuffer[] destinations;
                lock (gate)
                {
                    destinations = [.. subscribers.Values];
                }
                if (destinations.Length == 0)
                {
                    return;
                }
                FanOutFrame(frame, destinations);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Fail(exception);
            throw;
        }
    }

    internal static void FanOutFrame(
        ReadOnlySpan<byte> frame,
        IReadOnlyList<PcmFrameBuffer> destinations)
    {
        ArgumentNullException.ThrowIfNull(destinations);
        foreach (var destination in destinations)
        {
            destination.Write(frame);
        }
    }

    private async Task SynchronizeOpenAsync(CancellationToken cancellationToken)
    {
        lock (gate)
        {
            ThrowIfDisposed();
            if (startupFailure is not null)
            {
                throw new InvalidOperationException("The shared MiPlay capture failed.", startupFailure);
            }
            openArrivals++;
            if (openArrivals > participantCount)
            {
                throw new InvalidOperationException("A receiver entered shared Open synchronization more than once.");
            }
            if (openArrivals == participantCount)
            {
                openReady.TrySetResult();
            }
        }
        await openReady.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private void Fail(Exception exception)
    {
        lock (gate)
        {
            FailLocked(exception);
        }
    }

    private async ValueTask RemoveSubscriberAsync(int id)
    {
        PcmFrameBuffer? buffer;
        var disposeSession = false;
        lock (gate)
        {
            if (subscribers.Remove(id, out buffer))
            {
                if (!openReady.Task.IsCompleted)
                {
                    FailLocked(new OperationCanceledException(
                        "A receiver left before the shared MiPlay capture became ready."));
                }
                disposeSession = subscribers.Count == 0;
            }
        }
        if (buffer is not null)
        {
            await buffer.DisposeAsync().ConfigureAwait(false);
        }
        if (disposeSession)
        {
            await DisposeAsync().ConfigureAwait(false);
        }
    }

    private void FailLocked(Exception exception)
    {
        if (startupFailure is not null)
        {
            return;
        }
        startupFailure = exception;
        openReady.TrySetException(exception);
        foreach (var buffer in subscribers.Values)
        {
            buffer.Complete();
        }
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        lifetime.Cancel();
        try
        {
            await fanOutTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        catch
        {
            // The same capture failure was already surfaced to the subscribers.
        }

        PcmFrameBuffer[] buffers;
        lock (gate)
        {
            buffers = [.. subscribers.Values];
            subscribers.Clear();
        }
        foreach (var buffer in buffers)
        {
            await buffer.DisposeAsync().ConfigureAwait(false);
        }
        await Capture.StopAsync().ConfigureAwait(false);
        await Capture.DisposeAsync().ConfigureAwait(false);
        await SourceBuffer.DisposeAsync().ConfigureAwait(false);
        lifetime.Dispose();
    }

    internal sealed class MiPlaySharedAudioSubscription : IAsyncDisposable
    {
        private readonly MiPlaySharedAudioSession owner;
        private readonly int id;
        private int openSynchronized;
        private int disposed;

        internal MiPlaySharedAudioSubscription(
            MiPlaySharedAudioSession owner,
            int id,
            PcmFrameBuffer pcmBuffer)
        {
            this.owner = owner;
            this.id = id;
            PcmBuffer = pcmBuffer;
        }

        internal PcmFrameBuffer PcmBuffer { get; }

        public Task SynchronizeOpenAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref openSynchronized, 1) != 0)
            {
                throw new InvalidOperationException("Open synchronization is single-use per receiver.");
            }
            return owner.SynchronizeOpenAsync(cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
            {
                await owner.RemoveSubscriberAsync(id).ConfigureAwait(false);
            }
        }
    }
}
