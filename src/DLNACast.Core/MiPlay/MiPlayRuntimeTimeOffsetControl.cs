using System.Threading.Channels;

namespace DLNACast.Core.MiPlay;

/// <summary>
/// Serializes source-initiated runtime TIME_OFFSET requests onto the single
/// reverse RTSP connection. The owner completes shared requests after writing;
/// single-receiver requests still require the matching acknowledgement.
/// </summary>
internal sealed class MiPlayRuntimeTimeOffsetControl
{
    private static readonly TimeSpan CompletionTimeout = TimeSpan.FromSeconds(3);
    private readonly Channel<MiPlayRuntimeTimeOffsetRequest> requests =
        Channel.CreateUnbounded<MiPlayRuntimeTimeOffsetRequest>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false,
            });

    public MiPlayRuntimeTimeOffsetControl(bool waitForAcknowledgement = true) =>
        WaitForAcknowledgement = waitForAcknowledgement;

    public bool WaitForAcknowledgement { get; }

    public async Task UpdateAsync(
        ulong timeOffsetMicroseconds,
        CancellationToken cancellationToken)
    {
        var request = new MiPlayRuntimeTimeOffsetRequest(timeOffsetMicroseconds);
        await requests.Writer.WriteAsync(request, cancellationToken).ConfigureAwait(false);
        using var cancellationRegistration = cancellationToken.Register(
            static state => ((MiPlayRuntimeTimeOffsetRequest)state!).CancelIfPending(),
            request);
        try
        {
            await request.Completion
                .WaitAsync(CompletionTimeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException exception)
        {
            var timeout = new TimeoutException(
                WaitForAcknowledgement
                    ? "The receiver did not acknowledge the runtime MiPlay TIME_OFFSET within three seconds."
                    : "The source did not write the runtime MiPlay TIME_OFFSET within three seconds.",
                exception);
            request.Fail(timeout);
            throw timeout;
        }
    }

    public bool TryRead(out MiPlayRuntimeTimeOffsetRequest request) =>
        requests.Reader.TryRead(out request!);

    public ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken) =>
        requests.Reader.WaitToReadAsync(cancellationToken);

    public void Complete(Exception reason)
    {
        ArgumentNullException.ThrowIfNull(reason);
        requests.Writer.TryComplete(reason);
        while (requests.Reader.TryRead(out var request))
        {
            request.Fail(reason);
        }
    }
}

internal sealed class MiPlayRuntimeTimeOffsetRequest(ulong timeOffsetMicroseconds)
{
    private readonly TaskCompletionSource completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int state;

    public ulong TimeOffsetMicroseconds { get; } = timeOffsetMicroseconds;
    public Task Completion => completion.Task;

    public bool TryStart() => Interlocked.CompareExchange(ref state, 1, 0) == 0;

    public void CancelIfPending()
    {
        if (Interlocked.CompareExchange(ref state, 2, 0) == 0)
        {
            completion.TrySetCanceled();
        }
    }

    public void Succeed()
    {
        if (Interlocked.CompareExchange(ref state, 2, 1) == 1)
        {
            completion.TrySetResult();
        }
    }

    public void Fail(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var previous = Interlocked.Exchange(ref state, 2);
        if (previous != 2)
        {
            completion.TrySetException(exception);
        }
    }
}
