using DLNACast.Core.Abstractions;
using DLNACast.Core.Models;

namespace DLNACast.Core.Audio;

/// <summary>
/// Keeps one PCM destination alive while replacing the Windows capture source.
/// Network streams and encoders can continue consuming the same frame buffer.
/// </summary>
public sealed class SwitchableAudioCaptureSource : IAudioCaptureSource
{
    private readonly IAudioSourceCatalog _audioSources;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IAudioCaptureSource? _active;
    private PcmFrameBuffer? _destination;
    private CaptureSelection _selection;
    private int _disposed;

    public SwitchableAudioCaptureSource(
        IAudioSourceCatalog audioSources,
        CaptureSelection selection)
    {
        _audioSources = audioSources ?? throw new ArgumentNullException(nameof(audioSources));
        _selection = selection ?? throw new ArgumentNullException(nameof(selection));
    }

    public CaptureSelection Selection => _active?.Selection ?? _selection;
    public bool IsRunning => _active?.IsRunning == true;
    public CaptureHealth Health => _active?.Health ?? new(DateTimeOffset.UtcNow, null, null, 0);
    public event EventHandler<Exception>? CaptureFailed;

    public async Task StartAsync(PcmFrameBuffer destination, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_active is not null)
            {
                throw new InvalidOperationException("Audio capture is already active.");
            }

            var capture = _audioSources.CreateCapture(_selection);
            capture.CaptureFailed += OnCaptureFailed;
            try
            {
                await capture.StartAsync(destination, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                capture.CaptureFailed -= OnCaptureFailed;
                await capture.DisposeAsync().ConfigureAwait(false);
                throw;
            }

            _destination = destination;
            _active = capture;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SwitchAsync(
        CaptureSelection selection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Equals(Selection, selection)) return;
            if (_active is null || _destination is null)
            {
                _selection = selection;
                return;
            }

            var replacement = _audioSources.CreateCapture(selection);
            replacement.CaptureFailed += OnCaptureFailed;
            try
            {
                await replacement.StartAsync(_destination, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                replacement.CaptureFailed -= OnCaptureFailed;
                await replacement.DisposeAsync().ConfigureAwait(false);
                throw;
            }

            var previous = _active;
            _active = replacement;
            _selection = selection;
            previous.CaptureFailed -= OnCaptureFailed;
            try
            {
                await previous.StopAsync().ConfigureAwait(false);
            }
            finally
            {
                await previous.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_active is not null)
            {
                await _active.StopAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private void OnCaptureFailed(object? sender, Exception exception)
    {
        if (ReferenceEquals(sender, _active)) CaptureFailed?.Invoke(this, exception);
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_active is not null)
            {
                _active.CaptureFailed -= OnCaptureFailed;
                await _active.DisposeAsync().ConfigureAwait(false);
                _active = null;
            }
            _destination = null;
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }
}
