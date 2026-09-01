namespace DLNACast.Core.MiPlay;

/// <summary>
/// Shared playback keeps receiving while the command writer is idle. Like the
/// tablet CmdSource, heartbeats update connection liveness, not a per-CSeq gate.
/// The existing command framing and ordered writer are supplied by the runner.
/// </summary>
internal sealed class MiPlaySharedRuntimeControlChannel : IAsyncDisposable
{
    internal static readonly TimeSpan HeartbeatInactivityTimeout = TimeSpan.FromSeconds(15);
    internal static readonly TimeSpan HeartbeatCheckInterval = TimeSpan.FromSeconds(1);
    internal static readonly TimeSpan CommandAcknowledgementTimeout = TimeSpan.FromSeconds(3);

    private readonly Func<CancellationToken, Task<byte[]>> readFrame;
    private readonly Func<ReadOnlyMemory<byte>, CancellationToken, Task> writeFrame;
    private readonly Action<string> reportEvidence;
    private readonly Action<MiPlayNotifyPayload>? notificationReceived;
    private readonly TimeProvider clock;
    private readonly CancellationTokenSource lifetime;
    private readonly SemaphoreSlim writeGate = new(1, 1);
    private readonly Lock sync = new();
    private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task reader;
    private readonly Task watchdog;
    private PendingAcknowledgement? pending;
    private long lastHeartbeatTimestamp;

    public MiPlaySharedRuntimeControlChannel(
        Func<CancellationToken, Task<byte[]>> readFrame,
        Func<ReadOnlyMemory<byte>, CancellationToken, Task> writeFrame,
        Action<string> reportEvidence,
        CancellationToken cancellationToken,
        TimeProvider? clock = null,
        Action<MiPlayNotifyPayload>? notificationReceived = null)
    {
        this.readFrame = readFrame ?? throw new ArgumentNullException(nameof(readFrame));
        this.writeFrame = writeFrame ?? throw new ArgumentNullException(nameof(writeFrame));
        this.reportEvidence = reportEvidence ?? throw new ArgumentNullException(nameof(reportEvidence));
        this.notificationReceived = notificationReceived;
        this.clock = clock ?? TimeProvider.System;
        lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lastHeartbeatTimestamp = this.clock.GetTimestamp();
        reader = RunBackgroundAsync(ReadLoopAsync);
        watchdog = RunBackgroundAsync(WatchHeartbeatAsync);
    }

    public Task Completion => completion.Task;

    public async Task ExecuteAsync(MiPlayLegacyRuntimeControlCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (Completion.IsCompleted)
        {
            await Completion.ConfigureAwait(false);
        }

        // Only the heartbeat changes completion policy. Volume still requires
        // an exact command/sequence/payload ACK; Pause/Resume keep their policy.
        PendingAcknowledgement? acknowledgement = null;
        if (command.WaitForAcknowledgement && command.Command != MiPlayProtocolConstants.HeartbeatCommand)
        {
            acknowledgement = new(command);
            lock (sync)
            {
                if (pending is not null)
                {
                    throw new InvalidOperationException("A MiPlay runtime command is already awaiting acknowledgement.");
                }
                pending = acknowledgement;
            }
        }

        try
        {
            using var writeCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                lifetime.Token, cancellationToken);
            await WriteSerializedAsync(command.CommandFrame, writeCancellation.Token).ConfigureAwait(false);
            reportEvidence($"runtimeControlSent command=0x{command.Command:X4}, sequence=0x{command.Sequence:X4}, " +
                $"waitForAck={acknowledgement is not null}, heartbeatAgeMs={HeartbeatAge.TotalMilliseconds:F0}");
            if (acknowledgement is not null)
            {
                try
                {
                    await acknowledgement.Completion.Task.WaitAsync(
                        CommandAcknowledgementTimeout, writeCancellation.Token).ConfigureAwait(false);
                }
                catch (TimeoutException exception)
                {
                    throw new TimeoutException(
                        $"MiPlay command 0x{command.Command:X4} sequence 0x{command.Sequence:X4} was not acknowledged.",
                        exception);
                }
            }
        }
        catch (Exception exception)
        {
            // Preserve a reader/socket failure instead of replacing it with the
            // linked cancellation that stops an in-flight command write.
            if (Completion.IsFaulted)
            {
                await Completion.ConfigureAwait(false);
            }
            Fail(exception);
            throw;
        }
        finally
        {
            lock (sync)
            {
                if (ReferenceEquals(pending, acknowledgement))
                {
                    pending = null;
                }
            }
            // A failed write/timeout may end ExecuteAsync before it can await
            // this TCS; Fail still wakes it to release an active ACK waiter.
            _ = acknowledgement?.Completion.Task.Exception;
        }
    }

    private TimeSpan HeartbeatAge => clock.GetElapsedTime(
        Interlocked.Read(ref lastHeartbeatTimestamp), clock.GetTimestamp());

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            // Never cancel a partially read frame just because a heartbeat or
            // volume ACK is late. This reader lives for the whole connection.
            var bytes = await readFrame(cancellationToken).ConfigureAwait(false);
            if (!MiPlayCommandFrameCodec.TryDecode(bytes, out var frame, out var consumed) ||
                frame is null || consumed != bytes.Length)
            {
                throw new InvalidDataException("The runtime control reader received a malformed command frame.");
            }

            if (frame.Command is MiPlayProtocolConstants.HeartbeatCommand or
                MiPlayProtocolConstants.HeartbeatAcknowledgementCommand)
            {
                if (frame.Payload.Length != 0)
                {
                    throw new InvalidDataException("A legacy MiPlay heartbeat must have an empty payload.");
                }
                var age = HeartbeatAge;
                Interlocked.Exchange(ref lastHeartbeatTimestamp, clock.GetTimestamp());
                reportEvidence($"runtimeHeartbeatReceived command=0x{frame.Command:X4}, " +
                    $"sequence=0x{frame.Sequence:X4}, previousAgeMs={age.TotalMilliseconds:F0}");
                if (frame.Command == MiPlayProtocolConstants.HeartbeatCommand)
                {
                    await WriteSerializedAsync(MiPlayCommandFrameCodec.Encode(
                        MiPlayProtocolConstants.HeartbeatAcknowledgementCommand, frame.Sequence, []),
                        cancellationToken).ConfigureAwait(false);
                }
                continue;
            }

            if (frame.Command == MiPlayProtocolConstants.NotifyCommand &&
                MiPlayNotifyPayloadCodec.TryDecode(
                    frame.Payload,
                    out var notification,
                    out var notificationBytesConsumed) &&
                notification is not null &&
                notificationBytesConsumed == frame.Payload.Length)
            {
                notificationReceived?.Invoke(notification);
                reportEvidence($"runtimeNotifyReceived command=0x{frame.Command:X4}, " +
                    $"sequence=0x{frame.Sequence:X4}, label={notification.Label}, " +
                    $"integerValue={notification.IntegerValue?.ToString() ?? "none"}");
                continue;
            }

            lock (sync)
            {
                if (pending is { } request &&
                    MiPlayLegacyRuntimeControlSequence.IsExpectedAcknowledgement(request.Command, frame))
                {
                    request.Completion.TrySetResult();
                }
            }
            // Notifications/old replies do not satisfy a new command, nor do
            // they refresh the heartbeat watchdog. There is no eight-frame cap.
            reportEvidence($"runtimeControlReceived command=0x{frame.Command:X4}, " +
                $"sequence=0x{frame.Sequence:X4}, payloadLength={frame.Payload.Length}");
        }
    }

    private async Task WatchHeartbeatAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            await Task.Delay(HeartbeatCheckInterval, clock, cancellationToken).ConfigureAwait(false);
            if (HeartbeatAge > HeartbeatInactivityTimeout)
            {
                // ARM64 CmdSource: constructor +0xf0=5s, +0xf4=15s,
                // +0xf8=1s; reaper compares now > lastHeartbeat + 15s.
                throw new TimeoutException("MiPlay received no heartbeat activity for more than fifteen seconds.");
            }
        }
    }

    private async Task WriteSerializedAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
    {
        await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await writeFrame(bytes, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            writeGate.Release();
        }
    }

    private async Task RunBackgroundAsync(Func<CancellationToken, Task> run)
    {
        try
        {
            await run(lifetime.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
            completion.TrySetCanceled(lifetime.Token);
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    private void Fail(Exception exception)
    {
        completion.TrySetException(exception);
        lock (sync)
        {
            pending?.Completion.TrySetException(exception);
        }
        lifetime.Cancel();
    }

    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel();
        await Task.WhenAll(reader, watchdog).ConfigureAwait(false);
        // The owner observes Completion in its command/idle loop. Observe it
        // here too when disposal races a background failure.
        _ = Completion.Exception;
        lifetime.Dispose();
    }

    private sealed class PendingAcknowledgement(MiPlayLegacyRuntimeControlCommand command)
    {
        public MiPlayLegacyRuntimeControlCommand Command { get; } = command;
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
