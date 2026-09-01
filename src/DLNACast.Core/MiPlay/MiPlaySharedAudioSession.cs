using System.Diagnostics;
using DLNACast.Core.Abstractions;
using DLNACast.Core.Audio;
using DLNACast.Core.Models;

namespace DLNACast.Core.MiPlay;

/// <summary>
/// Owns one Windows loopback capture for a multi-receiver MiPlay group. All
/// receivers share TIME_OFFSET and the RTP media start, while receivers on the
/// same channel route share one encoded AAC stream.
/// </summary>
public sealed class MiPlaySharedAudioSession : IAsyncDisposable
{
    private const int MaximumParticipantCount = 63;
    private readonly Lock gate = new();
    private readonly int startupParticipantCount;
    private readonly MiPlaySharedMediaCoordinator mediaCoordinator;
    private readonly MiPlaySharedAccessUnitBroadcast accessUnitBroadcast;
    private readonly MiPlaySharedSendCompletionCoordinator sendCoordinator;
    private readonly MiPlaySharedPlaybackControlCoordinator playbackControlCoordinator = new();
    private readonly MiPlayExplicitPlaybackGate explicitCapturePlaybackGate = new();
    private readonly MiPlayExplicitPlaybackGate explicitMediaPlaybackGate = new();
    private readonly SemaphoreSlim playbackTransitionGate = new(1, 1);
    private readonly MiPlaySharedOutputTimeline outputTimeline = new();
    private readonly MiPlaySharedTimeOffsetState timeOffsetState = new();
    private readonly IReadOnlyDictionary<AudioChannelRoute, MiPlaySharedRouteEncoder> routeEncoders;
    private readonly MiPlayCaptureDiscontinuityRecovery captureRecovery =
        new(PcmFrameFormat.MiPlayAac);
    private readonly CancellationTokenSource lifetime = new();
    private readonly Dictionary<int, SubscriberState> subscribers = [];
    private readonly Dictionary<AudioChannelRoute, SortedDictionary<long, MiPlayWfdAudioPacketizerSnapshot>>
        packetizerSnapshots = [];
    private TaskCompletionSource packetizerSnapshotChanged =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource openReady =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int nextSubscriberId;
    private int openArrivals;
    private Exception? startupFailure;
    private Task fanOutTask = Task.CompletedTask;
    private Task encodedFanOutTask = Task.CompletedTask;
    private int disposed;

    private MiPlaySharedAudioSession(
        int participantCount,
        IReadOnlyDictionary<AudioChannelRoute, MiPlaySharedRouteEncoder> routeEncoders,
        PcmFrameBuffer sourceBuffer,
        SwitchableAudioCaptureSource capture)
    {
        startupParticipantCount = participantCount;
        mediaCoordinator = new MiPlaySharedMediaCoordinator(participantCount);
        accessUnitBroadcast = new MiPlaySharedAccessUnitBroadcast(MaximumParticipantCount);
        sendCoordinator = new MiPlaySharedSendCompletionCoordinator(MaximumParticipantCount);
        this.routeEncoders = routeEncoders;
        foreach (var route in routeEncoders.Keys)
        {
            packetizerSnapshots.Add(
                route,
                new SortedDictionary<long, MiPlayWfdAudioPacketizerSnapshot>
                {
                    [0] = MiPlayWfdAudioPacketizerSnapshot.Initial,
                });
        }
        SourceBuffer = sourceBuffer;
        Capture = capture;
    }

    private PcmFrameBuffer SourceBuffer { get; }
    internal SwitchableAudioCaptureSource Capture { get; }

    public static async Task<MiPlaySharedAudioSession> StartAsync(
        IAudioSourceCatalog audioSources,
        CaptureSelection selection,
        string ffmpegPath,
        int participantCount = 2,
        CancellationToken cancellationToken = default)
    {
        if (participantCount is < 2 or > 63)
        {
            throw new ArgumentOutOfRangeException(nameof(participantCount));
        }

        return await StartAsync(
            audioSources,
            selection,
            Enumerable.Repeat(AudioChannelRoute.Stereo, participantCount).ToArray(),
            ffmpegPath,
            cancellationToken).ConfigureAwait(false);
    }

    public static async Task<MiPlaySharedAudioSession> StartAsync(
        IAudioSourceCatalog audioSources,
        CaptureSelection selection,
        IReadOnlyList<AudioChannelRoute> participantRoutes,
        string ffmpegPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(audioSources);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(participantRoutes);
        ArgumentException.ThrowIfNullOrWhiteSpace(ffmpegPath);
        if (participantRoutes.Count is < 2 or > 63)
        {
            throw new ArgumentOutOfRangeException(nameof(participantRoutes));
        }
        if (participantRoutes.Any(route => !Enum.IsDefined(route)))
        {
            throw new ArgumentOutOfRangeException(nameof(participantRoutes));
        }

        var sourceBuffer = new PcmFrameBuffer(
            AudioChannelRoute.Stereo,
            PcmFrameFormat.MiPlayAac);
        var capture = new SwitchableAudioCaptureSource(audioSources, selection);
        var routeEncoders = new Dictionary<AudioChannelRoute, MiPlaySharedRouteEncoder>();
        try
        {
            await capture.StartAsync(sourceBuffer, cancellationToken).ConfigureAwait(false);
            await sourceBuffer.PrepareForPlaybackAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            foreach (var route in participantRoutes.Distinct())
            {
                routeEncoders.Add(
                    route,
                    new MiPlaySharedRouteEncoder(
                        route,
                        ffmpegPath,
                        cancellationToken));
            }
            return new MiPlaySharedAudioSession(
                participantRoutes.Count,
                routeEncoders,
                sourceBuffer,
                capture);
        }
        catch
        {
            foreach (var routeEncoder in routeEncoders.Values)
            {
                await routeEncoder.DisposeAsync().ConfigureAwait(false);
            }
            await capture.StopAsync().ConfigureAwait(false);
            await capture.DisposeAsync().ConfigureAwait(false);
            await sourceBuffer.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    internal MiPlaySharedAudioSubscription Subscribe(AudioChannelRoute route)
    {
        lock (gate)
        {
            ThrowIfDisposed();
            if (startupFailure is not null)
            {
                throw new InvalidOperationException("The shared MiPlay capture has failed.", startupFailure);
            }
            if (subscribers.Count >= MaximumParticipantCount)
            {
                throw new InvalidOperationException(
                    $"The shared MiPlay capture accepts at most {MaximumParticipantCount} receivers.");
            }

            if (!routeEncoders.ContainsKey(route))
            {
                throw new InvalidOperationException($"No shared AAC encoder was configured for {route}.");
            }
            var isLateJoin = mediaCoordinator.IsMediaReady;
            MiPlaySharedBroadcastParticipant? accessUnitParticipant = null;
            MiPlaySharedSendParticipant? sendParticipant = null;
            if (!isLateJoin)
            {
                accessUnitParticipant = accessUnitBroadcast.Register(
                    route,
                    startAccessUnitIndex =>
                        sendParticipant = sendCoordinator.Register(startAccessUnitIndex));
            }
            var id = ++nextSubscriberId;
            var buffer = new PcmFrameBuffer(
                route,
                PcmFrameFormat.MiPlayAac,
                maximumMilliseconds: PcmFrameBuffer.MaximumBufferMilliseconds,
                preserveFrameSequence: true);
            var state = new SubscriberState(
                id,
                route,
                buffer,
                accessUnitParticipant,
                sendParticipant,
                CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token),
                isLateJoin);
            subscribers.Add(id, state);
            if (!isLateJoin && subscribers.Count == startupParticipantCount)
            {
                SourceBuffer.TrimToLatest(1);
                fanOutTask = Task.Run(() => RunFanOutAsync(lifetime.Token));
                encodedFanOutTask = Task.Run(() => RunEncodedFanOutAsync(lifetime.Token));
            }
            return new MiPlaySharedAudioSubscription(
                this,
                state);
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

    internal LatencyProbePlayback ScheduleLatencyProbe()
    {
        ThrowIfDisposed();
        return SourceBuffer.ScheduleLatencyProbe();
    }

    private async Task RunFanOutAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await explicitCapturePlaybackGate.WaitUntilResumedAsync(cancellationToken)
                    .ConfigureAwait(false);
                var frame = await SourceBuffer.ReadFrameWithTimingOrSilenceAsync(cancellationToken)
                    .ConfigureAwait(false);
                // The tablet keeps the encoder input frame count continuous during
                // capture recovery. Forward the complete zero frame to every route
                // instead of letting each receiver create a different gap.
                SubscriberState[] activeSubscribers;
                lock (gate)
                {
                    activeSubscribers = [.. subscribers.Values];
                }
                if (activeSubscribers.Length == 0)
                {
                    return;
                }
                var recovered = captureRecovery.Recover(frame);
                await FanOutFramePreservingSequenceAsync(
                    recovered,
                    routeEncoders.Values.Select(value => value.PcmBuffer).ToArray(),
                    cancellationToken).ConfigureAwait(false);
                foreach (var subscriber in activeSubscribers)
                {
                    try
                    {
                        await subscriber.PcmBuffer.WritePreservingSequenceAsync(
                                recovered,
                                subscriber.Lifetime.Token)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (subscriber.Lifetime.IsCancellationRequested)
                    {
                    }
                }
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
        PcmFrameRead frame,
        IReadOnlyList<PcmFrameBuffer> destinations)
    {
        ArgumentNullException.ThrowIfNull(destinations);
        foreach (var destination in destinations)
        {
            destination.Write(
                frame.Bytes,
                frame.PresentationTimeMicroseconds,
                frame.ReanchorPresentationTimeline);
        }
    }

    private async Task RunEncodedFanOutAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var routeAccessUnits = new Dictionary<AudioChannelRoute, MiPlayEncodedAccessUnit>();
                foreach (var pair in routeEncoders)
                {
                    routeAccessUnits.Add(
                        pair.Key,
                        await pair.Value.ReadAsync(cancellationToken).ConfigureAwait(false));
                }
                var presentationTimeMicroseconds = routeAccessUnits.Values
                    .Max(value => value.PresentationTimeMicroseconds);
                var reanchorPresentationTimeline = routeAccessUnits.Values
                    .Select(value => value.ReanchorPresentationTimeline)
                    .Distinct()
                    .Single();

                var synchronizedRouteAccessUnits = routeAccessUnits.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value with
                    {
                        PresentationTimeMicroseconds = presentationTimeMicroseconds,
                        ReanchorPresentationTimeline = reanchorPresentationTimeline,
                    });
                await accessUnitBroadcast.PublishAsync(
                        synchronizedRouteAccessUnits,
                        cancellationToken)
                    .ConfigureAwait(false);
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

    internal static async Task FanOutFramePreservingSequenceAsync(
        PcmFrameRead frame,
        IReadOnlyList<PcmFrameBuffer> destinations,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destinations);
        foreach (var destination in destinations)
        {
            await destination.WritePreservingSequenceAsync(frame, cancellationToken)
                .ConfigureAwait(false);
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
            if (openArrivals > startupParticipantCount)
            {
                throw new InvalidOperationException("A receiver entered shared Open synchronization more than once.");
            }
            if (openArrivals == startupParticipantCount)
            {
                openReady.TrySetResult();
            }
        }
        await openReady.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task PrepareMediaStartupAsync(
        SubscriberState subscriber,
        CancellationToken cancellationToken)
    {
        if (!subscriber.IsLateJoin)
        {
            return;
        }

        // A late sender is not part of the original TIME_OFFSET/media/runway
        // barriers. Wait passively for those participants to finish before
        // reading retained output timestamps. Canceling this late sender must
        // not fail the already-starting shared group.
        await mediaCoordinator.WaitUntilMediaReadyAsync(cancellationToken)
            .ConfigureAwait(false);
        var replay = await accessUnitBroadcast.SnapshotHistoryAsync(
                subscriber.Route,
                MiPlaySharedStartupAccessUnitBuffer.LateJoinRequiredAccessUnitCount,
                cancellationToken)
            .ConfigureAwait(false);
        lock (gate)
        {
            ThrowIfDisposed();
            if (!subscribers.ContainsKey(subscriber.Id))
            {
                throw new OperationCanceledException(
                    "The late MiPlay receiver left before its replay window was prepared.");
            }
            subscriber.SetReplay(replay);
        }
    }

    private ValueTask<MiPlaySharedIndexedAccessUnit> ReadIndexedAccessUnitAsync(
        SubscriberState subscriber,
        CancellationToken cancellationToken)
    {
        MiPlaySharedBroadcastParticipant participant;
        lock (gate)
        {
            if (subscriber.TryTakeReplay(out var replay))
            {
                return ValueTask.FromResult(replay);
            }
            if (subscriber.AccessUnitParticipant is null)
            {
                MiPlaySharedSendParticipant? sendParticipant = null;
                var accessUnitParticipant = accessUnitBroadcast.Register(
                    subscriber.Route,
                    startAccessUnitIndex =>
                        sendParticipant = sendCoordinator.Register(startAccessUnitIndex));
                subscriber.Activate(accessUnitParticipant, sendParticipant!.Value);
            }
            participant = subscriber.AccessUnitParticipant ??
                throw new InvalidOperationException("The MiPlay receiver did not join live media.");
        }
        return accessUnitBroadcast.ReadIndexedAsync(participant, cancellationToken);
    }

    private async Task<MiPlayWfdAudioPacketizer> CreatePacketizerAsync(
        AudioChannelRoute route,
        long accessUnitIndex,
        ulong initialProgramClockReference90Khz,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            Task waitTask;
            lock (gate)
            {
                if (packetizerSnapshots[route].TryGetValue(accessUnitIndex, out var snapshot))
                {
                    return new MiPlayWfdAudioPacketizer(
                        snapshot,
                        initialProgramClockReference90Khz);
                }
                waitTask = packetizerSnapshotChanged.Task;
            }
            await waitTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private void RecordPacketizerSnapshot(
        AudioChannelRoute route,
        long nextAccessUnitIndex,
        MiPlayWfdAudioPacketizerSnapshot snapshot)
    {
        TaskCompletionSource changed;
        lock (gate)
        {
            var snapshots = packetizerSnapshots[route];
            if (snapshots.TryGetValue(nextAccessUnitIndex, out var existing))
            {
                if (existing != snapshot)
                {
                    throw new InvalidOperationException(
                        $"Shared MiPlay receivers on {route} disagreed about packetizer state at " +
                        $"access unit {nextAccessUnitIndex}.");
                }
                return;
            }
            snapshots.Add(nextAccessUnitIndex, snapshot);
            while (snapshots.Count > MiPlaySharedAccessUnitBroadcast.RetainedAccessUnitCount + 1)
            {
                snapshots.Remove(snapshots.Keys.First());
            }
            changed = packetizerSnapshotChanged;
            packetizerSnapshotChanged = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        changed.TrySetResult();
    }

    private void Fail(Exception exception)
    {
        lock (gate)
        {
            FailLocked(exception);
        }
    }

    private void RegisterPlaybackControl(
        SubscriberState subscriber,
        Func<MiPlaySharedPlaybackControlRequest, CancellationToken, Task> enqueue)
    {
        ArgumentNullException.ThrowIfNull(enqueue);
        lock (gate)
        {
            if (!subscribers.TryGetValue(subscriber.Id, out var current) ||
                !ReferenceEquals(current, subscriber))
            {
                throw new InvalidOperationException(
                    "The MiPlay receiver left before its playback control became ready.");
            }
            playbackControlCoordinator.Register(subscriber.Id, enqueue);
        }
    }

    private void UnregisterPlaybackControl(SubscriberState subscriber)
    {
        lock (gate)
        {
            if (subscribers.TryGetValue(subscriber.Id, out var current) &&
                ReferenceEquals(current, subscriber))
            {
                playbackControlCoordinator.Unregister(subscriber.Id);
            }
        }
    }

    private async Task BroadcastPlaybackControlAsync(
        MiPlaySharedPlaybackControlCommand command,
        CancellationToken cancellationToken,
        bool preserveMediaTimeline = false)
    {
        await playbackTransitionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!preserveMediaTimeline)
            {
                switch (command)
                {
                    case MiPlaySharedPlaybackControlCommand.Pause:
                        // Capture stops before the receiver Pause fan-out, matching
                        // CaptureService.PauseMirror -> PauseCaptureAudio -> pause.
                        explicitCapturePlaybackGate.Pause();
                        explicitMediaPlaybackGate.Pause();
                        await Capture.PauseAsync(cancellationToken).ConfigureAwait(false);
                        SourceBuffer.TrimToLatest(0);
                        break;
                    case MiPlaySharedPlaybackControlCommand.Resume:
                        // Drop pre-Pause PCM, restart capture, and only then wake
                        // encoder input and send the receiver Resume pair.
                        SourceBuffer.TrimToLatest(0);
                        await Capture.ResumeAsync(cancellationToken).ConfigureAwait(false);
                        explicitCapturePlaybackGate.Resume();
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(command));
                }
            }

            await playbackControlCoordinator.BroadcastAsync(
                    command,
                    cancellationToken,
                    preserveMediaTimeline)
                .ConfigureAwait(false);
            if (!preserveMediaTimeline &&
                command is MiPlaySharedPlaybackControlCommand.Resume)
            {
                // multiAudioSender remains in state 3 while capture restarts;
                // only state 2 accepts WritePacket after receiver Resume.
                explicitMediaPlaybackGate.Resume();
            }
        }
        finally
        {
            playbackTransitionGate.Release();
        }
    }

    private async ValueTask RemoveSubscriberAsync(int id)
    {
        SubscriberState? subscriber;
        var disposeSession = false;
        var failStartup = false;
        lock (gate)
        {
            if (subscribers.Remove(id, out subscriber))
            {
                if (!openReady.Task.IsCompletedSuccessfully || !mediaCoordinator.IsMediaReady)
                {
                    failStartup = subscribers.Count > 0;
                }
                disposeSession = subscribers.Count == 0;
            }
        }
        if (subscriber is not null)
        {
            playbackControlCoordinator.Unregister(subscriber.Id);
            subscriber.Lifetime.Cancel();
            if (subscriber.AccessUnitParticipant is { } accessUnitParticipant)
            {
                accessUnitBroadcast.Unregister(accessUnitParticipant);
            }
            if (subscriber.SendParticipant is { } sendParticipant)
            {
                sendCoordinator.Unregister(sendParticipant);
            }
        }
        if (failStartup)
        {
            Fail(new OperationCanceledException(
                "A receiver left before shared MiPlay media startup completed."));
        }
        if (subscriber is not null)
        {
            await subscriber.PcmBuffer.DisposeAsync().ConfigureAwait(false);
            subscriber.Lifetime.Dispose();
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
        mediaCoordinator.Fail(exception);
        accessUnitBroadcast.Fail(exception);
        sendCoordinator.Fail(exception);
        openReady.TrySetException(exception);
        packetizerSnapshotChanged.TrySetException(exception);
        foreach (var subscriber in subscribers.Values)
        {
            subscriber.Lifetime.Cancel();
            subscriber.PcmBuffer.Complete();
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
        var disposedException = new OperationCanceledException(
            "The shared MiPlay session was disposed.");
        mediaCoordinator.Fail(disposedException);
        accessUnitBroadcast.Fail(disposedException);
        sendCoordinator.Fail(disposedException);
        try
        {
            await Task.WhenAll(fanOutTask, encodedFanOutTask).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        catch
        {
            // The same capture failure was already surfaced to the subscribers.
        }

        SubscriberState[] activeSubscribers;
        lock (gate)
        {
            activeSubscribers = [.. subscribers.Values];
            subscribers.Clear();
        }
        foreach (var subscriber in activeSubscribers)
        {
            subscriber.Lifetime.Cancel();
            if (subscriber.AccessUnitParticipant is { } accessUnitParticipant)
            {
                accessUnitBroadcast.Unregister(accessUnitParticipant);
            }
            if (subscriber.SendParticipant is { } sendParticipant)
            {
                sendCoordinator.Unregister(sendParticipant);
            }
            await subscriber.PcmBuffer.DisposeAsync().ConfigureAwait(false);
            subscriber.Lifetime.Dispose();
        }
        foreach (var routeEncoder in routeEncoders.Values)
        {
            await routeEncoder.DisposeAsync().ConfigureAwait(false);
        }
        await Capture.StopAsync().ConfigureAwait(false);
        await Capture.DisposeAsync().ConfigureAwait(false);
        await SourceBuffer.DisposeAsync().ConfigureAwait(false);
        playbackTransitionGate.Dispose();
        lifetime.Dispose();
    }

    internal sealed class MiPlaySharedAudioSubscription : IAsyncDisposable
    {
        private readonly MiPlaySharedAudioSession owner;
        private readonly SubscriberState state;
        private int openSynchronized;
        private int disposed;

        internal MiPlaySharedAudioSubscription(
            MiPlaySharedAudioSession owner,
            SubscriberState state)
        {
            this.owner = owner;
            this.state = state;
        }

        internal PcmFrameBuffer PcmBuffer => state.PcmBuffer;
        internal int BufferedAccessUnits =>
            state.ReplayCount + (state.AccessUnitParticipant is { } participant
                ? owner.accessUnitBroadcast.BufferedCount(participant)
                : 0);
        internal long AccessUnitStarvationCount =>
            state.AccessUnitParticipant is { } participant
                ? owner.accessUnitBroadcast.StarvationCount(participant)
                : 0;
        internal bool IsLateJoin => state.IsLateJoin;
        internal long CaptureDiscontinuityCount => owner.captureRecovery.DiscontinuityCount;
        internal long RecoverySilenceFrameCount => owner.captureRecovery.ReplacedSilenceFrameCount;
        internal int EncoderPendingInputFrames => owner.routeEncoders[state.Route].PendingInputFrames;
        internal int EncoderBufferedAccessUnits => owner.routeEncoders[state.Route].BufferedAccessUnits;
        internal long EncoderInputSilenceFrames => owner.routeEncoders[state.Route].PcmBuffer.Underruns;

        public Task SynchronizeOpenAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref openSynchronized, 1) != 0)
            {
                throw new InvalidOperationException("Open synchronization is single-use per receiver.");
            }
            return state.IsLateJoin
                ? Task.CompletedTask
                : owner.SynchronizeOpenAsync(cancellationToken);
        }

        internal Task PrepareMediaStartupAsync(CancellationToken cancellationToken) =>
            owner.PrepareMediaStartupAsync(state, cancellationToken);

        internal long StartupAccessUnitIndex => state.ReplayStartAccessUnitIndex ?? 0;

        public async Task<ulong> SynchronizeTimeOffsetAsync(
            ulong candidate,
            CancellationToken cancellationToken)
        {
            if (state.IsLateJoin)
            {
                return owner.timeOffsetState.GetCurrent();
            }

            var synchronized = await owner.mediaCoordinator.SynchronizeTimeOffsetAsync(
                    state.Id,
                    candidate,
                    cancellationToken)
                .ConfigureAwait(false);
            return owner.timeOffsetState.Initialize(synchronized);
        }

        internal ulong PrepareRuntimeTimeOffsetUpdate(
            MiPlaySharedTimeOffsetUpdateKey key,
            ulong candidate) => owner.timeOffsetState.PrepareRuntimeUpdate(key, candidate);

        internal void CommitRuntimeTimeOffsetUpdate(
            MiPlaySharedTimeOffsetUpdateKey key,
            ulong value) => owner.timeOffsetState.CommitRuntimeUpdate(key, value);

        internal void RegisterPlaybackControl(
            Func<MiPlaySharedPlaybackControlRequest, CancellationToken, Task> enqueue) =>
            owner.RegisterPlaybackControl(state, enqueue);

        internal void UnregisterPlaybackControl() => owner.UnregisterPlaybackControl(state);

        internal Task BroadcastPlaybackControlAsync(
            MiPlaySharedPlaybackControlCommand command,
            CancellationToken cancellationToken) =>
            owner.BroadcastPlaybackControlAsync(command, cancellationToken);

        internal Task BroadcastReceiverPlaybackControlAsync(
            MiPlaySharedPlaybackControlCommand command,
            CancellationToken cancellationToken) =>
            owner.BroadcastPlaybackControlAsync(
                command,
                cancellationToken,
                preserveMediaTimeline: true);

        internal Task WaitUntilMediaResumedAsync(CancellationToken cancellationToken) =>
            owner.explicitMediaPlaybackGate.WaitUntilResumedAsync(cancellationToken);

        public Task<long> SynchronizeMediaAsync(
            ulong timeOffset,
            CancellationToken cancellationToken) =>
            state.IsLateJoin
                ? Task.FromResult(Stopwatch.GetTimestamp())
                : owner.mediaCoordinator.SynchronizeMediaAsync(
                    state.Id,
                    timeOffset,
                    cancellationToken);

        public Task<long> SynchronizeAccessUnitRunwayAsync(
            CancellationToken cancellationToken) =>
            state.IsLateJoin
                ? Task.FromResult(Stopwatch.GetTimestamp())
                : owner.mediaCoordinator.SynchronizeAccessUnitRunwayAsync(
                    state.Id,
                    cancellationToken);

        internal MiPlayOutputTimestamp GetOutputTimestamp(long accessUnitIndex)
        {
            if (!owner.mediaCoordinator.IsMediaReady)
            {
                throw new InvalidOperationException("The shared media session must be ready before output PTS starts.");
            }
            return owner.outputTimeline.GetTimestamp(
                accessUnitIndex,
                MiPlayWfdMediaClock.ConvertStopwatchTicksToMicroseconds(
                    Stopwatch.GetTimestamp(), Stopwatch.Frequency));
        }

        internal uint RecoverOutputAfterSend(long completedAccessUnitIndex, ulong completedMicroseconds) =>
            owner.outputTimeline.RecoverAfterSend(completedAccessUnitIndex, completedMicroseconds);

        public ValueTask<MiPlayEncodedAccessUnit> ReadAccessUnitAsync(
            CancellationToken cancellationToken) => ReadEncodedAsync(cancellationToken);

        private async ValueTask<MiPlayEncodedAccessUnit> ReadEncodedAsync(
            CancellationToken cancellationToken) =>
            (await ReadIndexedAccessUnitAsync(cancellationToken).ConfigureAwait(false)).AccessUnit;

        internal ValueTask<MiPlaySharedIndexedAccessUnit> ReadIndexedAccessUnitAsync(
            CancellationToken cancellationToken) =>
            owner.ReadIndexedAccessUnitAsync(state, cancellationToken);

        internal Task<MiPlayWfdAudioPacketizer> CreatePacketizerAsync(
            long accessUnitIndex,
            ulong initialProgramClockReference90Khz,
            CancellationToken cancellationToken) =>
            owner.CreatePacketizerAsync(
                state.Route,
                accessUnitIndex,
                initialProgramClockReference90Khz,
                cancellationToken);

        internal void RecordPacketizerSnapshot(
            long nextAccessUnitIndex,
            MiPlayWfdAudioPacketizerSnapshot snapshot) =>
            owner.RecordPacketizerSnapshot(state.Route, nextAccessUnitIndex, snapshot);

        public Task<MiPlaySharedSendCompletion> SynchronizeSendCompletionAsync(
            long accessUnitIndex,
            long mediaWriteCompletedAt,
            double sendGapMilliseconds,
            long mediaStartedAt,
            long stopwatchFrequency,
            bool captureTimelineReanchored,
            CancellationToken cancellationToken) =>
            state.SendParticipant is { } participant
                ? owner.sendCoordinator.SynchronizeAsync(
                participant,
                accessUnitIndex,
                mediaWriteCompletedAt,
                sendGapMilliseconds,
                mediaStartedAt,
                stopwatchFrequency,
                captureTimelineReanchored,
                cancellationToken)
                : throw new InvalidOperationException(
                    "A late MiPlay receiver cannot enter the send barrier before joining live media.");

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
            {
                await owner.RemoveSubscriberAsync(state.Id).ConfigureAwait(false);
            }
        }
    }

    internal sealed class SubscriberState(
        int id,
        AudioChannelRoute route,
        PcmFrameBuffer pcmBuffer,
        MiPlaySharedBroadcastParticipant? accessUnitParticipant,
        MiPlaySharedSendParticipant? sendParticipant,
        CancellationTokenSource lifetime,
        bool isLateJoin)
    {
        private readonly Queue<MiPlaySharedIndexedAccessUnit> replay = [];
        public int Id { get; } = id;
        public AudioChannelRoute Route { get; } = route;
        public PcmFrameBuffer PcmBuffer { get; } = pcmBuffer;
        public MiPlaySharedBroadcastParticipant? AccessUnitParticipant { get; private set; } =
            accessUnitParticipant;
        public MiPlaySharedSendParticipant? SendParticipant { get; private set; } = sendParticipant;
        public CancellationTokenSource Lifetime { get; } = lifetime;
        public bool IsLateJoin { get; } = isLateJoin;
        public long? ReplayStartAccessUnitIndex { get; private set; }
        public int ReplayCount => replay.Count;

        public void SetReplay(IReadOnlyList<MiPlaySharedIndexedAccessUnit> accessUnits)
        {
            if (!IsLateJoin || replay.Count != 0 || ReplayStartAccessUnitIndex is not null)
            {
                throw new InvalidOperationException("The late MiPlay replay window is single-use.");
            }
            ReplayStartAccessUnitIndex = accessUnits[0].AccessUnitIndex;
            foreach (var accessUnit in accessUnits)
            {
                replay.Enqueue(accessUnit);
            }
        }

        public bool TryTakeReplay(out MiPlaySharedIndexedAccessUnit accessUnit) =>
            replay.TryDequeue(out accessUnit);

        public void Activate(
            MiPlaySharedBroadcastParticipant accessUnitParticipant,
            MiPlaySharedSendParticipant sendParticipant)
        {
            if (AccessUnitParticipant is not null || SendParticipant is not null)
            {
                throw new InvalidOperationException("The MiPlay receiver already joined live media.");
            }
            AccessUnitParticipant = accessUnitParticipant;
            SendParticipant = sendParticipant;
        }
    }
}
