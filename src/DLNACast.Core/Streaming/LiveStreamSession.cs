using DLNACast.Core.Models;

namespace DLNACast.Core.Streaming;

public sealed class LiveStreamSession : IAsyncDisposable
{
    private readonly CancellationTokenSource _lifetime;
    private readonly Func<ValueTask> _dispose;
    private readonly TaskCompletionSource _clientConnected = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal LiveStreamSession(
        Uri streamUri,
        StreamProfile profile,
        CancellationTokenSource lifetime,
        Func<ValueTask> dispose)
    {
        StreamUri = streamUri;
        Profile = profile;
        _lifetime = lifetime;
        _dispose = dispose;
    }

    public Uri StreamUri { get; }
    public StreamProfile Profile { get; }
    public bool HasClient => _clientConnected.Task.IsCompletedSuccessfully;
    public int EarlyDisconnects { get; internal set; }

    internal void MarkClientConnected() => _clientConnected.TrySetResult();

    public async Task<bool> WaitForClientAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        timeoutSource.CancelAfter(timeout);
        try
        {
            await _clientConnected.Task.WaitAsync(timeoutSource.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && !_lifetime.IsCancellationRequested)
        {
            return false;
        }
    }

    public ValueTask DisposeAsync() => _dispose();
}

