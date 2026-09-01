using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading.Channels;
using DLNACast.Core.Abstractions;
using DLNACast.Core.Audio;
using DLNACast.Core.Localization;
using DLNACast.Core.Models;

namespace DLNACast.Core.MiPlay;

/// <summary>
/// Application-owned form of the audible, bounded legacy MiPlay/WFD path.
/// It intentionally preserves the validated command ledger while allocating
/// independent reverse endpoints for each session. Captured Pause, Resume,
/// Close, heartbeat, and volume operations share one runtime command sequence.
/// Group membership remains a source-side subscription operation, matching the
/// tablet's native addMirror/deleteMirror behavior instead of inventing an
/// unsupported receiver command.
/// </summary>
public sealed class MiPlayLegacySystemAudioSessionRunner(IAudioSourceCatalog audioSources) :
    IMiPlaySystemAudioSessionRunner,
    IMiPlayReceiverVolumeController,
    IMiPlayAudioCaptureController,
    IMiPlayLatencyProbeController,
    IMiPlayPlaybackController
{
    private const int OpenEndedMediaDurationMilliseconds = int.MaxValue;
    internal static readonly TimeSpan SharedMediaWriteTimeout = TimeSpan.FromSeconds(3);
    public const string ValidatedSourceName = "MI PAD 4/Plus";
    public const MiPlayLegacyStatusQueryOrder ValidatedStatusQueryOrder =
        MiPlayLegacyStatusQueryOrder.VolumeStateMediaInfo;

    private readonly IAudioSourceCatalog audioSources = audioSources ?? throw new ArgumentNullException(nameof(audioSources));
    private readonly Lock volumeSync = new();
    private readonly Lock playbackSync = new();
    private readonly SemaphoreSlim captureGate = new(1, 1);
    private ActiveVolumeControl? activeVolumeControl;
    private ActiveSourcePlaybackControl? activeSourcePlaybackControl;
    private MiPlaySharedAudioSession.MiPlaySharedAudioSubscription? activeSharedPlaybackSubscription;
    private SwitchableAudioCaptureSource? activeCapture;
    private PcmFrameBuffer? activePcmBuffer;
    private MiPlaySharedAudioSession? activeSharedAudioSession;
    private CaptureSelection? desiredSelection;
    private int? receiverVolume;

    public int? ReceiverVolume
    {
        get
        {
            lock (volumeSync)
            {
                return receiverVolume;
            }
        }
    }

    public event EventHandler<MiPlayReceiverVolumeChangedEventArgs>? ReceiverVolumeChanged;

    public bool ReceiverPaused
    {
        get
        {
            lock (playbackSync)
            {
                return activeSourcePlaybackControl?.ReceiverPaused ?? false;
            }
        }
    }

    public Task PauseAsync(CancellationToken cancellationToken = default) =>
        EnqueuePlaybackControlAsync(SourcePlaybackCommand.Pause, cancellationToken);

    public Task ResumeAsync(CancellationToken cancellationToken = default) =>
        EnqueuePlaybackControlAsync(SourcePlaybackCommand.Resume, cancellationToken);

    public Task PauseReceiverAsync(CancellationToken cancellationToken = default) =>
        EnqueuePlaybackControlAsync(SourcePlaybackCommand.PauseReceiver, cancellationToken);

    public Task ResumeReceiverAsync(CancellationToken cancellationToken = default) =>
        EnqueuePlaybackControlAsync(SourcePlaybackCommand.ResumeReceiver, cancellationToken);

    public Task CloseAsync(CancellationToken cancellationToken = default) =>
        EnqueuePlaybackControlAsync(SourcePlaybackCommand.Close, cancellationToken);

    private Task EnqueuePlaybackControlAsync(
        SourcePlaybackCommand command,
        CancellationToken cancellationToken)
    {
        ActiveSourcePlaybackControl? control;
        MiPlaySharedAudioSession.MiPlaySharedAudioSubscription? sharedSubscription;
        lock (playbackSync)
        {
            control = activeSourcePlaybackControl;
            sharedSubscription = activeSharedPlaybackSubscription;
        }
        if (control is null)
        {
            return Task.FromException(new InvalidOperationException(
                "Receiver playback can be controlled only while a MiPlay stream is active."));
        }
        if (sharedSubscription is not null && command is not SourcePlaybackCommand.Close)
        {
            var sharedCommand = command switch
            {
                SourcePlaybackCommand.Pause or SourcePlaybackCommand.PauseReceiver =>
                    MiPlaySharedPlaybackControlCommand.Pause,
                SourcePlaybackCommand.Resume or SourcePlaybackCommand.ResumeReceiver =>
                    MiPlaySharedPlaybackControlCommand.Resume,
                _ => throw new ArgumentOutOfRangeException(nameof(command)),
            };
            return command is SourcePlaybackCommand.PauseReceiver or SourcePlaybackCommand.ResumeReceiver
                ? sharedSubscription.BroadcastReceiverPlaybackControlAsync(
                    sharedCommand,
                    cancellationToken)
                : sharedSubscription.BroadcastPlaybackControlAsync(
                    sharedCommand,
                    cancellationToken);
        }
        return control.EnqueueAsync(command, cancellationToken);
    }

    public Task SetReceiverVolumeAsync(
        int volume,
        CancellationToken cancellationToken = default)
    {
        if (volume is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(volume));
        }

        ActiveVolumeControl? control;
        lock (volumeSync)
        {
            control = activeVolumeControl;
        }

        return control is null
            ? Task.FromException(new InvalidOperationException(
                "Receiver volume can be changed only while a MiPlay stream is active."))
            : control.EnqueueAsync(volume, cancellationToken);
    }

    public async Task SetCaptureSelectionAsync(
        CaptureSelection selection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selection);
        await captureGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (activeSharedAudioSession is not null)
            {
                await activeSharedAudioSession.SetCaptureSelectionAsync(selection, cancellationToken)
                    .ConfigureAwait(false);
            }
            else if (activeCapture is not null)
            {
                await activeCapture.SwitchAsync(selection, cancellationToken).ConfigureAwait(false);
            }
            desiredSelection = selection;
        }
        finally
        {
            captureGate.Release();
        }
    }

    public Task PrepareLatencyProbeAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ActiveSourcePlaybackControl? control;
        lock (playbackSync)
        {
            control = activeSourcePlaybackControl;
        }

        if (control is null)
        {
            return Task.FromException(new InvalidOperationException(SystemLanguage.Select(
                "小米妙播音箱尚未进入可控制的播放状态。",
                "The MiPlay receiver is not ready for playback control.")));
        }

        // A streaming receiver is already in state=2. Forcing Resume here
        // rewrites TIME_OFFSET immediately before the probe: the safe native
        // resume buffer adds about one second, while a zero-buffer rewrite can
        // make the probe too late to decode. Validate readiness only.
        return Task.CompletedTask;
    }

    public async Task<long> StartLatencyProbeAsync(
        CancellationToken cancellationToken = default)
    {
        await PrepareLatencyProbeAsync(cancellationToken).ConfigureAwait(false);

        LatencyProbePlayback playback;
        await captureGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            playback = activeSharedAudioSession?.ScheduleLatencyProbe() ??
                activePcmBuffer?.ScheduleLatencyProbe() ??
                throw new InvalidOperationException(
                    "A MiPlay audio stream must be active before starting a latency probe.");
        }
        finally
        {
            captureGate.Release();
        }

        try
        {
            return await playback.StartedAt
                .WaitAsync(TimeSpan.FromSeconds(1), cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            playback.Cancel();
            throw;
        }
    }

    public async Task RunAsync(
        MiPlaySystemAudioRequest request,
        Action receiverReady,
        Action<MiPlayCastDiagnostics> report,
        CancellationToken cancellationToken)
    {
        request.Validate();
        ArgumentNullException.ThrowIfNull(receiverReady);
        ArgumentNullException.ThrowIfNull(report);
        ResetReceiverVolume();
        await ResetCaptureSelectionAsync(request.Selection, cancellationToken).ConfigureAwait(false);
        var sharedAudioSession = request.SharedAudioSession;
        var sharedSubscription = sharedAudioSession?.Subscribe(request.ChannelRoute);
        await using var sharedSubscriptionLease = sharedSubscription;
        await using var sharedCaptureRegistration = sharedAudioSession is null
            ? null
            : await RegisterSharedCaptureAsync(sharedAudioSession, cancellationToken)
                .ConfigureAwait(false);

        var targetAddress = request.Renderer.Address;
        var bootstrapGuard = new MiPlayLegacyAudioSourceBootstrapProbeGuard(
            targetAddress,
            explicitlyAuthorized: true,
            ValidatedStatusQueryOrder);
        var bootstrap = new MiPlayLegacyAudioSourceSession(
            ValidatedSourceName,
            ValidatedStatusQueryOrder);

        report(new MiPlayCastDiagnostics(
            MiPlayCastState.Connecting,
            SystemLanguage.Select(
                $"正在连接 {request.Renderer.FriendlyName}（{targetAddress}）…",
                $"Connecting to {request.Renderer.FriendlyName} ({targetAddress})…"),
            ProtocolEvidence: $"timeOffsetLeadUs={request.PlaybackDelayMicroseconds}"));
        using var controlClient = new TcpClient(AddressFamily.InterNetwork) { NoDelay = true };
        await WaitForStartupPhaseAsync(
                controlClient.ConnectAsync(
                        targetAddress,
                        MiPlayProtocolConstants.DefaultControlPort,
                        cancellationToken)
                    .AsTask(),
                TimeSpan.FromSeconds(5),
                "The MiPlay control connection to port 8899 was not established within five seconds.",
                cancellationToken)
            .ConfigureAwait(false);
        await using var controlStream = controlClient.GetStream();

        report(new MiPlayCastDiagnostics(
            MiPlayCastState.Bootstrapping,
            SystemLanguage.Select(
                "音箱已连接，正在完成 MiPlay 初始化…",
                "Speaker connected. Completing MiPlay initialization…")));
        while (bootstrap.Phase != MiPlayLegacyAudioSourcePhase.BasicBootstrapComplete)
        {
            var progress = bootstrap.Progress;
            var inboundBytes = await WaitForStartupPhaseAsync(
                    ReadCommandFrameAsync(controlStream, cancellationToken),
                    TimeSpan.FromSeconds(10),
                    "The receiver did not advance the legacy bootstrap within ten seconds. " +
                    FormatBootstrapProgress(progress),
                    cancellationToken)
                .ConfigureAwait(false);
            var transition = bootstrap.ProcessInboundFrame(inboundBytes);
            if (!transition.Accepted)
            {
                throw new InvalidOperationException($"Legacy bootstrap stopped: {transition.Boundary}");
            }

            report(new MiPlayCastDiagnostics(
                MiPlayCastState.Bootstrapping,
                SystemLanguage.Select(
                    $"MiPlay 初始化中（命令 0x{transition.ObservedCommand:X4}，序列 {transition.ObservedSequence}）…",
                    $"Initializing MiPlay (command 0x{transition.ObservedCommand:X4}, sequence {transition.ObservedSequence})…")));

            foreach (var write in transition.OutboundWrites)
            {
                var decision = bootstrapGuard.AuthorizeNextWrite(write);
                if (!decision.CanSend)
                {
                    throw new InvalidOperationException($"Bootstrap guard refused a write: {decision.Reason}");
                }
                await WriteAsync(controlStream, write.ToArray(), cancellationToken).ConfigureAwait(false);
            }
        }
        if (bootstrap.CurrentVolume is not uint initialVolume)
        {
            throw new InvalidOperationException("The completed MiPlay bootstrap did not retain receiver volume.");
        }
        UpdateReceiverVolume(checked((int)initialVolume));

        if (controlClient.Client.LocalEndPoint is not IPEndPoint localControlEndPoint)
        {
            throw new InvalidOperationException("The connected MiPlay control socket has no IPv4 local endpoint.");
        }

        var sourceAddress = localControlEndPoint.Address;
        using var reverseEndpoints = new MiPlayReverseEndpointLease(sourceAddress);
        var reverseListener = reverseEndpoints.ReverseListener;
        var timer = reverseEndpoints.Timer;
        var reverseTcpPort = reverseEndpoints.ReverseTcpPort;
        var timerUdpPort = reverseEndpoints.TimerUdpPort;
        using var runLease = new CancellationSourceLease(cancellationToken);
        var run = runLease.Source;
        var timerTask = RunTimerResponderAsync(
            timer, targetAddress, run.Token, useTabletTimerBehavior: sharedSubscription is not null);

        var playback = new MiPlayLegacyPlaybackControlSession(
            bootstrap,
            ValidatedSourceName,
            sourceAddress,
            reverseTcpPort);
        var expectedControl = new Queue<(ushort Command, ushort Sequence)>(
        [
            (MiPlayProtocolConstants.SetLocalDeviceInfoCommand, 8),
            (MiPlayProtocolConstants.GetDeviceInfoCommand, 9),
            (MiPlayProtocolConstants.SetLocalDeviceInfoCommand, 10),
            (MiPlayProtocolConstants.GetMirrorModeCommand, 11),
            (MiPlayProtocolConstants.HeartbeatCommand, 12),
            (MiPlayProtocolConstants.SetPlaySourceCommand, 13),
            (MiPlayProtocolConstants.OpenDeviceCommand, 14),
        ]);
        var sentControlFrames = 0;
        long? preOpenHeartbeatSentAt = null;

        await SendPlaybackWritesAsync(
            controlStream,
            playback.Start().OutboundWrites,
            expectedControl,
            cancellationToken).ConfigureAwait(false);
        sentControlFrames += 2;

        while (playback.Phase != MiPlayLegacyPlaybackControlPhase.AwaitingOpenPrerequisites)
        {
            var inboundBytes = await WaitForStartupPhaseAsync(
                    ReadCommandFrameAsync(controlStream, cancellationToken),
                    TimeSpan.FromSeconds(10),
                    $"The receiver did not advance playback control from {playback.Phase} within ten seconds.",
                    cancellationToken)
                .ConfigureAwait(false);
            var transition = playback.ProcessInbound(inboundBytes);
            if (!transition.Accepted)
            {
                throw new InvalidOperationException($"Playback continuation stopped: {transition.Boundary}");
            }
            var sendsPreOpenHeartbeat = ContainsCommandSequence(
                transition.OutboundWrites,
                MiPlayProtocolConstants.HeartbeatCommand,
                MiPlayLegacyPlaybackControlSession.HeartbeatSequence);
            await SendPlaybackWritesAsync(
                controlStream,
                transition.OutboundWrites,
                expectedControl,
                cancellationToken).ConfigureAwait(false);
            if (sendsPreOpenHeartbeat)
            {
                preOpenHeartbeatSentAt = Stopwatch.GetTimestamp();
            }
            sentControlFrames += transition.OutboundWrites.Sum(write => write.Frames.Count);
        }

        await using var localPcmBuffer = sharedAudioSession is null
            ? new PcmFrameBuffer(request.ChannelRoute, PcmFrameFormat.MiPlayAac)
            : null;
        var pcmBuffer = sharedSubscription?.PcmBuffer ?? localPcmBuffer!;
        await using var localCapture = sharedAudioSession is null
            ? await StartCaptureAsync(pcmBuffer, request.Selection, cancellationToken).ConfigureAwait(false)
            : null;
        var capture = sharedAudioSession?.Capture ?? localCapture!;
        await using var activeCaptureRegistration = localCapture is null
            ? null
            : new ActiveCaptureRegistration(this, localCapture, pcmBuffer);
        await pcmBuffer.PrepareForPlaybackAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        // Shared groups own one encoder per channel route. No receiver owns the
        // encoder, so removing a native multiAudioSender-style member cannot
        // tear down the AAC source used by the remaining speakers.
        var ownsEncoder = sharedSubscription is null;
        var explicitCapturePlaybackGate = ownsEncoder
            ? new MiPlayExplicitPlaybackGate()
            : null;
        var explicitMediaPlaybackGate = ownsEncoder
            ? new MiPlayExplicitPlaybackGate()
            : null;
        await using var encoder = ownsEncoder
            ? MiPlayFfmpegAacEncoder.Start(
                request.FfmpegPath,
                MiPlayProtocolConstants.AacBitRate,
                "aac_mf")
            : null;
        var encodedAccessUnits = encoder is null
            ? null
            : new MiPlayOrderedAccessUnitBuffer();
        var encoderInputPresentationTimes = encoder is null
            ? null
            : Channel.CreateUnbounded<MiPlayEncoderInputTiming>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = true,
                AllowSynchronousContinuations = false,
            });
        using var encoderInputLease = new CancellationSourceLease(cancellationToken);
        var encoderInputRun = encoderInputLease.Source;
        var encoderInputReady = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var meter = new MiPlayPcm16SignalMeter();
        var captureRecovery = new MiPlayCaptureDiscontinuityRecovery(PcmFrameFormat.MiPlayAac);
        var sourcePlaybackControl = new ActiveSourcePlaybackControl();
        var encoderWriterTask = Task.Run(async () =>
        {
            try
            {
                if (request.PairSynchronization is not null)
                {
                    await encoderInputReady.Task.WaitAsync(encoderInputRun.Token).ConfigureAwait(false);
                }
                while (!encoderInputRun.IsCancellationRequested)
                {
                    if (explicitCapturePlaybackGate is not null)
                    {
                        await explicitCapturePlaybackGate
                            .WaitUntilResumedAsync(encoderInputRun.Token)
                            .ConfigureAwait(false);
                    }
                    var pcm = await pcmBuffer.ReadFrameWithTimingOrSilenceAsync(encoderInputRun.Token)
                        .ConfigureAwait(false);
                    // A buffer underrun is already a correctly sized 48 kHz AAC
                    // input frame. Dropping it starves aac_mf while the network
                    // clock keeps advancing, which creates a gap and catch-up burst.
                    if (encoder is null)
                    {
                        meter.Add(pcm.Bytes);
                        continue;
                    }
                    var recovered = sharedSubscription is null
                        ? captureRecovery.Recover(pcm)
                        : pcm;
                    meter.Add(recovered.Bytes);
                    await encoder.WritePcmAsync(
                            recovered.Bytes,
                            encoderInputRun.Token)
                        .ConfigureAwait(false);
                    await encoderInputPresentationTimes!.Writer
                        .WriteAsync(
                            new MiPlayEncoderInputTiming(
                                recovered.PresentationTimeMicroseconds,
                                recovered.ReanchorPresentationTimeline),
                            encoderInputRun.Token)
                        .ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (encoderInputRun.IsCancellationRequested)
            {
            }
            finally
            {
                encoderInputPresentationTimes?.Writer.TryComplete();
            }
        }, encoderInputRun.Token);
        var encoderReaderTask = encoder is null
            ? Task.CompletedTask
            : Task.Run(async () =>
            {
                try
                {
                    while (!encoderInputRun.IsCancellationRequested)
                    {
                        var accessUnit = await encoder.ReadAccessUnitAsync(encoderInputRun.Token)
                            .ConfigureAwait(false);
                        if (accessUnit is null)
                        {
                            encodedAccessUnits!.Complete();
                            return;
                        }
                        var inputTiming =
                            await encoderInputPresentationTimes!.Reader
                                .ReadAsync(encoderInputRun.Token)
                                .ConfigureAwait(false);
                        await encodedAccessUnits!.WriteAsync(
                                new MiPlayEncodedAccessUnit(
                                    accessUnit,
                                    inputTiming.PresentationTimeMicroseconds,
                                    inputTiming.ReanchorPresentationTimeline),
                                encoderInputRun.Token)
                            .ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException exception)
                    when (encoderInputRun.IsCancellationRequested)
                {
                    encodedAccessUnits!.Complete(exception);
                }
                catch (Exception exception)
                {
                    encodedAccessUnits!.Complete(exception);
                    throw;
                }
            }, encoderInputRun.Token);
        report(new MiPlayCastDiagnostics(
            MiPlayCastState.AwaitingReceiver,
            SystemLanguage.Select(
                "音频捕获与 AAC 编码器已就绪，等待音箱回连…",
                "Audio capture and the AAC encoder are ready. Waiting for the speaker to connect back…"),
            pcmBuffer.BufferedMilliseconds,
            pcmBuffer.Overruns,
            pcmBuffer.Underruns));

        if (sharedSubscription is not null)
        {
            await sharedSubscription.SynchronizeOpenAsync(cancellationToken).ConfigureAwait(false);
        }
        else if (request.PairSynchronization is not null)
        {
            await request.PairSynchronization.SynchronizeOpenAsync(cancellationToken).ConfigureAwait(false);
        }
        var acceptControlTask = reverseListener.AcceptTcpClientAsync(cancellationToken).AsTask();
        var open = playback.PrepareOpen(new(
            TcpListenerBound: true,
            UdpTimerResponderBound: true,
            ReverseConnectionCapacity: 3,
            AacMpegTsPipelineReady: true));
        if (!open.Accepted || !open.OpenPrepared)
        {
            throw new InvalidOperationException($"Open prerequisites were not accepted: {open.Boundary}");
        }
        await SendPlaybackWritesAsync(
            controlStream,
            open.OutboundWrites,
            expectedControl,
            cancellationToken).ConfigureAwait(false);
        sentControlFrames++;
        if (sentControlFrames != 7 || expectedControl.Count != 0 || preOpenHeartbeatSentAt is null)
        {
            throw new InvalidOperationException("The validated seven-frame playback-control ledger was not consumed exactly.");
        }

        report(new MiPlayCastDiagnostics(
            MiPlayCastState.AwaitingReceiver,
            SystemLanguage.Select(
                "已发送播放请求，等待音箱建立 RTSP 连接…",
                "Playback request sent. Waiting for the speaker's RTSP connection…"),
            pcmBuffer.BufferedMilliseconds,
            pcmBuffer.Overruns,
            pcmBuffer.Underruns));
        using var rtspClient = await WaitForStartupPhaseAsync(
                acceptControlTask,
                TimeSpan.FromSeconds(10),
                "The receiver did not open the reverse RTSP control connection within ten seconds of Open.",
                cancellationToken)
            .ConfigureAwait(false);
        rtspClient.NoDelay = true;
        var beforeTimeOffset = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var postOpenContextSent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sharedInitialTimeOffset = new TaskCompletionSource<ulong>(TaskCreationOptions.RunContinuationsAsynchronously);
        var rtspReady = new TaskCompletionSource<ulong>(TaskCreationOptions.RunContinuationsAsynchronously);
        var runtimeTimeOffsetControl = new MiPlayRuntimeTimeOffsetControl(
            waitForAcknowledgement: sharedSubscription is null);
        var receiverLatencyTelemetry = new MiPlayReceiverLatencyTelemetry();
        var rtspTask = RunRtspControlAsync(
            rtspClient,
            sourceAddress,
            timerUdpPort,
            beforeTimeOffset,
            postOpenContextSent.Task,
            sharedInitialTimeOffset.Task,
            rtspReady,
            runtimeTimeOffsetControl,
            receiverLatencyTelemetry,
            sharedSubscription,
            run.Token);

        report(new MiPlayCastDiagnostics(
            MiPlayCastState.AwaitingReceiver,
            SystemLanguage.Select(
                "RTSP 已连接，等待音箱辅助连接…",
                "RTSP connected. Waiting for the speaker's auxiliary connection…"),
            pcmBuffer.BufferedMilliseconds,
            pcmBuffer.Overruns,
            pcmBuffer.Underruns));
        using var unusedClient = await WaitForStartupPhaseAsync(
                reverseListener.AcceptTcpClientAsync(cancellationToken).AsTask(),
                TimeSpan.FromSeconds(10),
                "The receiver did not open the expected auxiliary reverse connection within ten seconds.",
                cancellationToken)
            .ConfigureAwait(false);
        report(new MiPlayCastDiagnostics(
            MiPlayCastState.AwaitingReceiver,
            SystemLanguage.Select(
                "辅助连接已建立，等待音箱音频连接…",
                "Auxiliary connection established. Waiting for the speaker's audio connection…"),
            pcmBuffer.BufferedMilliseconds,
            pcmBuffer.Overruns,
            pcmBuffer.Underruns));
        using var audioClient = await WaitForStartupPhaseAsync(
                reverseListener.AcceptTcpClientAsync(cancellationToken).AsTask(),
                TimeSpan.FromSeconds(10),
                "The receiver did not open the reverse audio connection within ten seconds.",
                cancellationToken)
            .ConfigureAwait(false);
        audioClient.NoDelay = true;
        reverseListener.Stop();

        Task<MiPlaySharedStartupAccessUnitBuffer>? sharedStartupAccessUnitsTask = null;
        if (sharedSubscription is not null)
        {
            // The tablet derives its initial offset on the first normalized
            // audio write. Fill the existing ordered runway before taking that
            // clock sample; the encoder remains warm and no AAC is discarded.
            await sharedSubscription.PrepareMediaStartupAsync(cancellationToken)
                .ConfigureAwait(false);
            sharedStartupAccessUnitsTask = MiPlaySharedStartupAccessUnitBuffer.FillAsync(
                sharedSubscription.ReadIndexedAccessUnitAsync,
                sharedSubscription.IsLateJoin
                    ? MiPlaySharedStartupAccessUnitBuffer.LateJoinRequiredAccessUnitCount
                    : MiPlayStartupAccessUnitBuffer.RequiredAccessUnitCount,
                cancellationToken);
        }

        report(new MiPlayCastDiagnostics(
            MiPlayCastState.AwaitingReceiver,
            SystemLanguage.Select(
                "音频连接已建立，等待播放同步…",
                "Audio connection established. Waiting for playback synchronization…"),
            pcmBuffer.BufferedMilliseconds,
            pcmBuffer.Overruns,
            pcmBuffer.Underruns));
        await WaitForStartupPhaseAsync(
                beforeTimeOffset.Task,
                TimeSpan.FromSeconds(10),
                "The RTSP channel did not reach PLAY/TIME_OFFSET within ten seconds.",
                cancellationToken)
            .ConfigureAwait(false);
        var postOpen = new MiPlayLegacyPostOpenPlaybackSession(
            MiPlaySetMediaInfoPayloadCodec.CreateWindowsSystemAudio(
                OpenEndedMediaDurationMilliseconds,
                ValidatedSourceName));
        var postOpenStart = postOpen.Start();
        if (!postOpenStart.Accepted)
        {
            throw new InvalidOperationException($"Post-Open control did not start: {postOpenStart.Boundary}");
        }
        var setMediaInfoWrite = postOpenStart.OutboundWrites.Single();
        var setMediaInfoFrame = setMediaInfoWrite.Frames.Single();
        report(new MiPlayCastDiagnostics(
            MiPlayCastState.AwaitingReceiver,
            SystemLanguage.Select(
                "媒体信息已发送，等待音箱进入播放状态…",
                "Media information sent. Waiting for the speaker to enter playback…"),
            pcmBuffer.BufferedMilliseconds,
            pcmBuffer.Overruns,
            pcmBuffer.Underruns,
            ProtocolEvidence: MiPlayRuntimeWireEvidence.DescribeSetMediaInfo(setMediaInfoFrame)));
        await SendPostOpenWritesAsync(
            controlStream,
            postOpenStart.OutboundWrites,
            cancellationToken).ConfigureAwait(false);
        postOpenContextSent.TrySetResult();
        MiPlaySharedStartupAccessUnitBuffer? sharedStartupAccessUnits = null;
        if (sharedSubscription is not null)
        {
            sharedStartupAccessUnits = await sharedStartupAccessUnitsTask!
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            // MultiMirrorDisplay::addMirror copies the group's stored
            // timeOffset into a new native sender. Only original participants
            // derive it from their normalized first AAC PTS.
            var candidate = sharedSubscription.IsLateJoin
                ? 0UL
                : MiPlayWfdMediaClock.CreateTabletTimeOffsetMicroseconds(
                    GetMonotonicMicroseconds(),
                    MiPlaySharedOutputTimeline.CalculateEncoderPresentationTimeMicroseconds(
                        sharedStartupAccessUnits.FirstAccessUnitIndex),
                    request.PlaybackDelayMicroseconds);
            var synchronized = await sharedSubscription.SynchronizeTimeOffsetAsync(
                    candidate,
                    cancellationToken)
                .ConfigureAwait(false);
            sharedInitialTimeOffset.TrySetResult(synchronized);
        }
        var timeOffset = await WaitForStartupPhaseAsync(
                rtspReady.Task,
                TimeSpan.FromSeconds(10),
                "The RTSP channel did not complete post-Open readiness within ten seconds.",
                cancellationToken)
            .ConfigureAwait(false);
        long? sharedMediaStartedAt = null;
        if (sharedSubscription is not null)
        {
            await sharedSubscription.SynchronizeMediaAsync(
                    timeOffset,
                    cancellationToken)
                .ConfigureAwait(false);
            sharedMediaStartedAt = await sharedSubscription.SynchronizeAccessUnitRunwayAsync(
                    cancellationToken)
                .ConfigureAwait(false);
        }
        else if (request.PairSynchronization is not null)
        {
            await request.PairSynchronization.SynchronizeMediaAsync(cancellationToken).ConfigureAwait(false);
            pcmBuffer.TrimToLatest(1);
            encoderInputReady.TrySetResult();
        }
        var firstMediaSent = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var mediaTimeline = new ActiveMediaTimeline(request.PlaybackDelayMicroseconds);
        // Diagnostics only: bounded independently from every audio/control
        // queue, and drained with the existing media reports. A background
        // heartbeat must not reset the UI to AwaitingReceiver or zero counters.
        var runtimeEvidence = Channel.CreateBounded<string>(new BoundedChannelOptions(32)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest,
        });
        var postOpenControlTask = RunPostOpenControlAsync(
            controlStream,
            postOpen,
            preOpenHeartbeatSentAt.Value,
            firstMediaSent.Task,
            sourcePlaybackControl,
            runtimeTimeOffsetControl,
            mediaTimeline,
            receiverReady,
            evidence => report(new MiPlayCastDiagnostics(
                MiPlayCastState.AwaitingReceiver,
                SystemLanguage.Select(
                    "已收到音箱播放状态，正在准备音频流…",
                    "Speaker playback status received. Preparing the audio stream…"),
                pcmBuffer.BufferedMilliseconds,
                pcmBuffer.Overruns,
                pcmBuffer.Underruns,
                ProtocolEvidence: evidence)),
            run.Token,
            useTabletHeartbeat: sharedSubscription is not null,
            reportRuntimeEvidence: evidence => runtimeEvidence.Writer.TryWrite(evidence),
            sharedSubscription,
            localCapture,
            localPcmBuffer,
            explicitCapturePlaybackGate,
            explicitMediaPlaybackGate);

        await using var audioStream = audioClient.GetStream();
        var initialProgramClockReference90Khz =
            MiPlayWfdMediaClock.CreateInitialProgramClockReference90Khz(
                timeOffset,
                request.PlaybackDelayMicroseconds);
        long accessUnitIndex = sharedStartupAccessUnits?.FirstAccessUnitIndex ?? 0;
        var packetizer = sharedSubscription is null
            ? new MiPlayWfdAudioPacketizer(
                initialProgramClockReference90Khz: initialProgramClockReference90Khz)
            : sharedSubscription.IsLateJoin
                ? new MiPlayWfdAudioPacketizer(
                    initialProgramClockReference90Khz: initialProgramClockReference90Khz)
            : await sharedSubscription.CreatePacketizerAsync(
                    accessUnitIndex,
                    initialProgramClockReference90Khz,
                    cancellationToken)
                .ConfigureAwait(false);
        using var mediaPacingClock = MiPlayMediaPacingClock.Create();
        // Start pacing when a finite AAC runway is actually available. The
        // earlier receiver-ready timestamp predates aac_mf's first live batch
        // and turns every batch into an immediate catch-up burst.
        var mediaStartedAt = sharedMediaStartedAt ?? Stopwatch.GetTimestamp();
        long totalBytes = 0;
        long totalRtpFrames = 0;
        long totalAccessUnitsSent = 0;
        long pacingAccessUnitsSent = 0;
        var firstMediaWasSent = false;
        var expectedRtpSequence = packetizer.NextSequenceNumber;
        long previousMediaWriteCompletedAt = 0;
        var minimumMediaSendGapMilliseconds = double.PositiveInfinity;
        double maximumMediaSendGapMilliseconds = 0;
        long lateMediaSends = 0;
        long catchUpMediaSends = 0;
        long mediaTimelineRebases = 0;
        double maximumInterReceiverSendSkewMilliseconds = 0;
        var minimumEncodedAccessUnitAgeMilliseconds = double.PositiveInfinity;
        double maximumEncodedAccessUnitAgeMilliseconds = 0;

        try
        {
            while (true)
            {
                if (sharedSubscription is not null)
                {
                    await sharedSubscription.WaitUntilMediaResumedAsync(cancellationToken)
                        .ConfigureAwait(false);
                }
                else if (explicitMediaPlaybackGate is not null)
                {
                    await explicitMediaPlaybackGate.WaitUntilResumedAsync(cancellationToken)
                        .ConfigureAwait(false);
                }
                ThrowIfBackgroundFailed(
                    rtspTask,
                    timerTask,
                    postOpenControlTask,
                    encoderWriterTask,
                    encoderReaderTask);
                byte[] accessUnit;
                ulong mediaMonotonicMicroseconds;
                bool reanchorPresentationTimeline;
                var participatesInSharedSendBarrier = sharedSubscription is not null;
                if (sharedSubscription is null)
                {
                    var encodedAccessUnit = await encodedAccessUnits!.ReadAsync(cancellationToken)
                        .ConfigureAwait(false);
                    accessUnit = encodedAccessUnit.Bytes;
                    mediaMonotonicMicroseconds =
                        encodedAccessUnit.PresentationTimeMicroseconds;
                    reanchorPresentationTimeline =
                        encodedAccessUnit.ReanchorPresentationTimeline;
                }
                else
                {
                    MiPlaySharedIndexedAccessUnit synchronizedAccessUnit;
                    if (sharedStartupAccessUnits is not null &&
                        sharedStartupAccessUnits.TryRead(out var startupAccessUnit))
                    {
                        synchronizedAccessUnit = startupAccessUnit;
                    }
                    else
                    {
                        synchronizedAccessUnit = await sharedSubscription.ReadIndexedAccessUnitAsync(
                                cancellationToken)
                            .ConfigureAwait(false);
                    }
                    if (synchronizedAccessUnit.AccessUnitIndex != accessUnitIndex)
                    {
                        if (!sharedSubscription.IsLateJoin ||
                            !synchronizedAccessUnit.ParticipatesInSendBarrier ||
                            synchronizedAccessUnit.AccessUnitIndex < accessUnitIndex)
                        {
                            throw new InvalidOperationException(
                                $"Shared MiPlay media index changed from {accessUnitIndex} to " +
                                $"{synchronizedAccessUnit.AccessUnitIndex} outside a late-join boundary.");
                        }

                        accessUnitIndex = synchronizedAccessUnit.AccessUnitIndex;
                        mediaStartedAt = Stopwatch.GetTimestamp();
                        pacingAccessUnitsSent = 0;
                    }
                    participatesInSharedSendBarrier =
                        synchronizedAccessUnit.ParticipatesInSendBarrier;
                    accessUnit = synchronizedAccessUnit.AccessUnit.Bytes;
                    mediaMonotonicMicroseconds =
                        synchronizedAccessUnit.AccessUnit.PresentationTimeMicroseconds;
                    reanchorPresentationTimeline =
                        synchronizedAccessUnit.AccessUnit.ReanchorPresentationTimeline;
                }
                var encodedAccessUnitAgeMilliseconds = MiPlayEncodedAccessUnitAge.CalculateMilliseconds(
                    MiPlayWfdMediaClock.ConvertStopwatchTicksToMicroseconds(
                        Stopwatch.GetTimestamp(),
                        Stopwatch.Frequency),
                    mediaMonotonicMicroseconds);
                minimumEncodedAccessUnitAgeMilliseconds = Math.Min(
                    minimumEncodedAccessUnitAgeMilliseconds,
                    encodedAccessUnitAgeMilliseconds);
                maximumEncodedAccessUnitAgeMilliseconds = Math.Max(
                    maximumEncodedAccessUnitAgeMilliseconds,
                    encodedAccessUnitAgeMilliseconds);
                // Shared output PTS and the tablet-style live PCR are sampled
                // together once per AU, then reused by every receiver.
                var outputTimestamp = sharedSubscription?.GetOutputTimestamp(accessUnitIndex);
                ulong liveProgramClockReference90Khz;
                ushort liveProgramClockReferenceExtension = 0;
                if (sharedSubscription is null)
                {
                    liveProgramClockReference90Khz = MiPlayWfdMediaClock.CreateInitialProgramClockReference90Khz(
                        mediaMonotonicMicroseconds,
                        request.PlaybackDelayMicroseconds);
                }
                else
                {
                    // TSPacketizer reads ALooper::GetNowUs() at packetization;
                    // it does not use capture age or subtract playback delay.
                    var pcr27Mhz = outputTimestamp!.Value.ProgramClockReference27Mhz;
                    liveProgramClockReference90Khz = pcr27Mhz / 300;
                    liveProgramClockReferenceExtension = (ushort)(pcr27Mhz % 300);
                }
                // A shared group owns one output PTS clock, anchored at its
                // first actual media handoff. Raw-PCM aac_mf PTS is sample based;
                // mediaMonotonicMicroseconds is capture-age metadata, not that
                // encoder PTS. Capture metadata can jump across prewarm backpressure.
                // PCR/TIME_OFFSET use the tablet packetization clock above;
                // capture metadata remains diagnostic and never substitutes
                // for the encoder's normalized media PTS.
                var packets = packetizer.PacketizeAccessUnit(
                    accessUnit,
                    liveProgramClockReference90Khz,
                    outputTimestamp?.PresentationTimestamp90Khz,
                    liveProgramClockReferenceExtension);
                if (packets.Count is < 1 or > 2)
                {
                    throw new InvalidOperationException("An AAC access unit exceeded the validated two-fragment boundary.");
                }
                foreach (var packet in packets)
                {
                    if (packet.SequenceNumber != expectedRtpSequence || packet.WireFrame.Length > 1_500)
                    {
                        throw new InvalidOperationException(
                            "A MiPlay RTP packet violated the validated sequence or size boundary.");
                    }
                    expectedRtpSequence++;
                }

                var wireWrite = new byte[packets.Sum(packet => packet.WireFrame.Length)];
                var offset = 0;
                foreach (var packet in packets)
                {
                    packet.WireFrame.CopyTo(wireWrite, offset);
                    offset += packet.WireFrame.Length;
                }
                var packetCount = packets.Count;
                if (sharedSubscription is null)
                {
                    await WriteAsync(audioStream, wireWrite, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await WriteSharedMediaAsync(
                            audioStream,
                            wireWrite,
                            SharedMediaWriteTimeout,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                var mediaWriteCompletedAt = Stopwatch.GetTimestamp();
                mediaTimeline.Update(packetizer.NextMediaPosition90Khz);
                double sendGapMilliseconds = 0;
                if (previousMediaWriteCompletedAt != 0)
                {
                    sendGapMilliseconds =
                        (mediaWriteCompletedAt - previousMediaWriteCompletedAt) * 1_000d /
                        Stopwatch.Frequency;
                    minimumMediaSendGapMilliseconds = Math.Min(
                        minimumMediaSendGapMilliseconds,
                        sendGapMilliseconds);
                    maximumMediaSendGapMilliseconds = Math.Max(
                        maximumMediaSendGapMilliseconds,
                        sendGapMilliseconds);
                    if (sharedSubscription is null &&
                        MiPlayMediaStallRecoveryPlan.ShouldActivate(
                            accessUnitIndex,
                            sendGapMilliseconds))
                    {
                        var rebasedMediaStartedAt = MiPlayMediaStallRecoveryPlan.RebaseMediaStartedAt(
                            mediaWriteCompletedAt,
                            accessUnitIndex + 1,
                            Stopwatch.Frequency);
                        var timelineAdvance90Khz =
                            MiPlayMediaStallRecoveryPlan.CalculateTimelineAdvance90Khz(
                                 mediaStartedAt,
                                 rebasedMediaStartedAt,
                                 Stopwatch.Frequency,
                                 reanchorPresentationTimeline);
                        packetizer.AdvanceTimeline90Khz(timelineAdvance90Khz);
                        mediaTimeline.Update(packetizer.NextMediaPosition90Khz);
                        mediaStartedAt = rebasedMediaStartedAt;
                        await runtimeTimeOffsetControl.UpdateAsync(
                                MiPlayMediaStallRecoveryPlan.CalculateRuntimeTimeOffsetMicroseconds(
                                    MiPlayWfdMediaClock.ConvertStopwatchTicksToMicroseconds(
                                        mediaWriteCompletedAt,
                                        Stopwatch.Frequency),
                                    packetizer.NextMediaPosition90Khz,
                                    request.PlaybackDelayMicroseconds),
                                cancellationToken)
                            .ConfigureAwait(false);
                        mediaTimelineRebases++;
                    }
                }
                previousMediaWriteCompletedAt = mediaWriteCompletedAt;
                if (sharedSubscription is not null && participatesInSharedSendBarrier)
                {
                    var synchronizedSend = await sharedSubscription.SynchronizeSendCompletionAsync(
                            accessUnitIndex,
                            mediaWriteCompletedAt,
                             sendGapMilliseconds,
                             mediaStartedAt,
                             Stopwatch.Frequency,
                             reanchorPresentationTimeline,
                             cancellationToken)
                        .ConfigureAwait(false);
                    maximumInterReceiverSendSkewMilliseconds = Math.Max(
                        maximumInterReceiverSendSkewMilliseconds,
                        synchronizedSend.SendCompletionSkewMilliseconds);
                    if (synchronizedSend.TimelineRebased)
                    {
                        // The shared output clock accounts for pre-write fixes
                        // and post-write socket stalls against the same origin.
                        var outputAdvance90Khz = sharedSubscription.RecoverOutputAfterSend(
                            accessUnitIndex, synchronizedSend.ReferenceMonotonicMicroseconds);
                        packetizer.AdvanceTimeline90Khz(outputAdvance90Khz);
                        mediaTimeline.Update(packetizer.NextMediaPosition90Khz);
                        mediaStartedAt = synchronizedSend.MediaStartedAt;
                        var updatedTimeOffset =
                            MiPlayMediaStallRecoveryPlan.CalculateRuntimeTimeOffsetMicroseconds(
                                synchronizedSend.ReferenceMonotonicMicroseconds,
                                packetizer.NextMediaPosition90Khz,
                                request.PlaybackDelayMicroseconds);
                        var timeOffsetUpdateKey = new MiPlaySharedTimeOffsetUpdateKey(
                            MiPlaySharedTimeOffsetUpdateKind.TransportRecovery,
                            checked((ulong)accessUnitIndex));
                        updatedTimeOffset = sharedSubscription.PrepareRuntimeTimeOffsetUpdate(
                            timeOffsetUpdateKey,
                            updatedTimeOffset);
                        await runtimeTimeOffsetControl.UpdateAsync(
                                updatedTimeOffset,
                                cancellationToken)
                            .ConfigureAwait(false);
                        sharedSubscription.CommitRuntimeTimeOffsetUpdate(
                            timeOffsetUpdateKey,
                            updatedTimeOffset);
                        mediaTimelineRebases++;
                    }
                    if (!sharedSubscription.IsLateJoin)
                    {
                        sharedSubscription.RecordPacketizerSnapshot(
                            accessUnitIndex + 1,
                            packetizer.CaptureSnapshot());
                    }
                }
                if (!firstMediaWasSent)
                {
                    firstMediaWasSent = true;
                    firstMediaSent.TrySetResult();
                }
                totalBytes += wireWrite.Length;
                totalRtpFrames += packetCount;
                totalAccessUnitsSent++;
                if (participatesInSharedSendBarrier)
                {
                    pacingAccessUnitsSent++;
                }

                var accessUnitsSent = accessUnitIndex + 1;
                var nextDueMilliseconds = MiPlayWfdStartupPacingPlan.GetDueAfterMilliseconds(
                    pacingAccessUnitsSent);
                var nextSend = mediaStartedAt + checked((long)Math.Round(
                    nextDueMilliseconds * Stopwatch.Frequency / 1_000d));
                var remaining = participatesInSharedSendBarrier
                    ? nextSend - Stopwatch.GetTimestamp()
                    : 0;
                var pacingDeadline = nextSend;
                if (pacingAccessUnitsSent >= MiPlayWfdStartupPacingPlan.CapturedAccessUnitCount && remaining <= 0)
                {
                    lateMediaSends++;
                    var nominalAccessUnitTicks =
                        MiPlayWfdStartupPacingPlan.NominalAccessUnitMilliseconds *
                        Stopwatch.Frequency / 1_000d;
                    if (-remaining >= nominalAccessUnitTicks)
                    {
                        catchUpMediaSends++;
                    }
                }

                if (accessUnitIndex % 25 == 0)
                {
                    var signal = meter.SnapshotAndReset();
                    var receiverLatency = receiverLatencyTelemetry.Latest;
                    string? protocolEvidence = null;
                    if (accessUnitIndex == 0)
                    {
                        protocolEvidence = MiPlayRuntimeWireEvidence.DescribeFirstMediaBatch(
                            accessUnit,
                            wireWrite,
                            packetCount);
                        if (outputTimestamp is MiPlayOutputTimestamp sharedClock)
                        {
                            protocolEvidence +=
                                $" | sharedClock timeOffsetUs={timeOffset}," +
                                $"outputPts90Khz={sharedClock.PresentationTimestamp90Khz}," +
                                $"pcr27Mhz={sharedClock.ProgramClockReference27Mhz}," +
                                "pcrSource=packetizationNow";
                        }
                    }
                    else if (accessUnitIndex % 250 == 0)
                    {
                        var captureHealth = capture.Health;
                        protocolEvidence =
                            $"pcmSignal route={request.ChannelRoute},capturePackets={captureHealth.PacketCount}," +
                            $"captureLastAudible={captureHealth.LastAudiblePacketAt?.ToString("O") ?? "none"}," +
                            $"receiverPaused={sourcePlaybackControl.ReceiverPaused}," +
                             $"routedSamples={signal.SampleCount},routedNonZero={signal.NonZeroSampleCount}," +
                             $"peak={signal.PeakNormalized:F6},rms={signal.RmsNormalized:F6}," +
                             $"rmsDbfs={signal.RmsDecibelsFullScale:F2},audible={signal.ContainsAudibleSignal}," +
                             $"captureGaps={sharedSubscription?.CaptureDiscontinuityCount ?? captureRecovery.DiscontinuityCount}," +
                             $"recoverySilenceFrames={sharedSubscription?.RecoverySilenceFrameCount ?? captureRecovery.ReplacedSilenceFrameCount}";
                        if (sharedSubscription is not null)
                        {
                            protocolEvidence +=
                                $",encoderInFlight={sharedSubscription.EncoderPendingInputFrames}," +
                                $"encoderQueuedAac={sharedSubscription.EncoderBufferedAccessUnits}," +
                                $"encoderInputSilenceFrames={sharedSubscription.EncoderInputSilenceFrames}," +
                                $"outputPtsFixes={outputTimestamp?.CorrectionCount}," +
                                $"outputPtsFixUs={outputTimestamp?.TotalCorrectionMicroseconds}";
                        }
                    }
                    if (runtimeEvidence.Reader.TryRead(out var controlEvidence))
                    {
                        var messages = new List<string> { controlEvidence };
                        while (messages.Count < 32 && runtimeEvidence.Reader.TryRead(out controlEvidence))
                        {
                            messages.Add(controlEvidence);
                        }
                        protocolEvidence = (protocolEvidence is null ? "" : protocolEvidence + " | ") +
                            $"runtimeControl receiver={targetAddress}: " + string.Join(" | ", messages);
                    }
                    report(new MiPlayCastDiagnostics(
                        postOpen.Phase == MiPlayLegacyPostOpenPlaybackPhase.Playing
                            ? MiPlayCastState.Streaming
                            : MiPlayCastState.AwaitingReceiver,
                        postOpen.Phase == MiPlayLegacyPostOpenPlaybackPhase.Playing
                            ? SystemLanguage.Select("MiPlay 音频正在投送", "MiPlay audio is casting")
                            : SystemLanguage.Select(
                                "MiPlay 音频流已启动，等待音箱播放…",
                                "The MiPlay audio stream has started. Waiting for speaker playback…"),
                        pcmBuffer.BufferedMilliseconds,
                        pcmBuffer.Overruns,
                        pcmBuffer.Underruns,
                        totalAccessUnitsSent,
                        totalRtpFrames,
                        totalBytes,
                        ProtocolEvidence: protocolEvidence,
                        MinimumMediaSendGapMilliseconds:
                            double.IsPositiveInfinity(minimumMediaSendGapMilliseconds)
                                ? 0
                                : minimumMediaSendGapMilliseconds,
                        MaximumMediaSendGapMilliseconds: maximumMediaSendGapMilliseconds,
                        LateMediaSends: lateMediaSends,
                        CatchUpMediaSends: catchUpMediaSends,
                        MediaTimelineRebases: mediaTimelineRebases,
                        MaximumInterReceiverSendSkewMilliseconds:
                            maximumInterReceiverSendSkewMilliseconds,
                        BufferedAccessUnits:
                            encodedAccessUnits?.BufferedCount ??
                            sharedSubscription?.BufferedAccessUnits ?? -1,
                        AccessUnitBufferStarvations:
                            encodedAccessUnits?.StarvationCount ??
                            sharedSubscription?.AccessUnitStarvationCount ?? 0,
                        ReceiverReportedLatencyMilliseconds:
                            receiverLatency?.LatencyMilliseconds,
                        ReceiverReportedRtpPacketCount:
                            receiverLatency?.RtpPacketCount,
                        ReceiverReportedBitrate:
                            receiverLatency?.Bitrate,
                        MinimumEncodedAccessUnitAgeMilliseconds:
                            double.IsPositiveInfinity(minimumEncodedAccessUnitAgeMilliseconds)
                                ? 0
                                : minimumEncodedAccessUnitAgeMilliseconds,
                        MaximumEncodedAccessUnitAgeMilliseconds:
                            maximumEncodedAccessUnitAgeMilliseconds,
                        ReceiverPaused: sourcePlaybackControl.ReceiverPaused,
                        ContainsAudiblePcm: signal.ContainsAudibleSignal));
                    minimumMediaSendGapMilliseconds = double.PositiveInfinity;
                    maximumMediaSendGapMilliseconds = 0;
                    lateMediaSends = 0;
                    catchUpMediaSends = 0;
                    mediaTimelineRebases = 0;
                    maximumInterReceiverSendSkewMilliseconds = 0;
                    minimumEncodedAccessUnitAgeMilliseconds = double.PositiveInfinity;
                    maximumEncodedAccessUnitAgeMilliseconds = 0;
                }

                accessUnitIndex = accessUnitsSent;
                if (remaining > 0)
                {
                    await mediaPacingClock.WaitUntilAsync(
                            pacingDeadline,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }
        finally
        {
            encoderInputRun.Cancel();
            try
            {
                await Task.WhenAll(encoderWriterTask, encoderReaderTask).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (encoderInputRun.IsCancellationRequested)
            {
            }
            if (localCapture is not null)
            {
                await activeCaptureRegistration!.DisposeAsync().ConfigureAwait(false);
                await localCapture.StopAsync().ConfigureAwait(false);
            }
            run.Cancel();
        }

    }

    private async Task ResetCaptureSelectionAsync(
        CaptureSelection selection,
        CancellationToken cancellationToken)
    {
        await captureGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (activeCapture is not null || activeSharedAudioSession is not null)
            {
                throw new InvalidOperationException("A MiPlay audio capture is already active.");
            }
            desiredSelection = selection;
        }
        finally
        {
            captureGate.Release();
        }
    }

    private async Task<SwitchableAudioCaptureSource> StartCaptureAsync(
        PcmFrameBuffer destination,
        CaptureSelection fallbackSelection,
        CancellationToken cancellationToken)
    {
        await captureGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        SwitchableAudioCaptureSource? capture = null;
        try
        {
            capture = new SwitchableAudioCaptureSource(
                audioSources,
                desiredSelection ?? fallbackSelection);
            await capture.StartAsync(destination, cancellationToken).ConfigureAwait(false);
            activeCapture = capture;
            activePcmBuffer = destination;
            return capture;
        }
        catch
        {
            if (capture is not null) await capture.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        finally
        {
            captureGate.Release();
        }
    }

    private async ValueTask UnregisterCaptureAsync(
        SwitchableAudioCaptureSource capture,
        PcmFrameBuffer pcmBuffer)
    {
        await captureGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (ReferenceEquals(activeCapture, capture)) activeCapture = null;
            if (ReferenceEquals(activePcmBuffer, pcmBuffer)) activePcmBuffer = null;
        }
        finally
        {
            captureGate.Release();
        }
    }

    private async Task<ActiveSharedCaptureRegistration> RegisterSharedCaptureAsync(
        MiPlaySharedAudioSession sharedAudioSession,
        CancellationToken cancellationToken)
    {
        await captureGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (activeCapture is not null || activeSharedAudioSession is not null)
            {
                throw new InvalidOperationException("A MiPlay audio capture is already active.");
            }
            activeSharedAudioSession = sharedAudioSession;
            return new ActiveSharedCaptureRegistration(this, sharedAudioSession);
        }
        finally
        {
            captureGate.Release();
        }
    }

    private async ValueTask UnregisterSharedCaptureAsync(
        MiPlaySharedAudioSession sharedAudioSession)
    {
        await captureGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (ReferenceEquals(activeSharedAudioSession, sharedAudioSession))
            {
                activeSharedAudioSession = null;
            }
        }
        finally
        {
            captureGate.Release();
        }
    }

    private sealed class ActiveCaptureRegistration(
        MiPlayLegacySystemAudioSessionRunner owner,
        SwitchableAudioCaptureSource capture,
        PcmFrameBuffer pcmBuffer) : IAsyncDisposable
    {
        private int disposed;

        public ValueTask DisposeAsync() =>
            Interlocked.Exchange(ref disposed, 1) == 0
                ? owner.UnregisterCaptureAsync(capture, pcmBuffer)
                : ValueTask.CompletedTask;
    }

    private sealed class ActiveSharedCaptureRegistration(
        MiPlayLegacySystemAudioSessionRunner owner,
        MiPlaySharedAudioSession sharedAudioSession) : IAsyncDisposable
    {
        private int disposed;

        public ValueTask DisposeAsync() =>
            Interlocked.Exchange(ref disposed, 1) == 0
                ? owner.UnregisterSharedCaptureAsync(sharedAudioSession)
                : ValueTask.CompletedTask;
    }

    private static async Task SendPlaybackWritesAsync(
        NetworkStream stream,
        IReadOnlyList<MiPlayLegacyAudioSourceWrite> writes,
        Queue<(ushort Command, ushort Sequence)> expected,
        CancellationToken cancellationToken)
    {
        foreach (var write in writes)
        {
            if (write.Frames.Count != 1 || expected.Count == 0 ||
                !MiPlayCommandFrameCodec.TryDecode(write.Frames[0], out var frame, out var consumed) ||
                frame is null || consumed != write.Frames[0].Length)
            {
                throw new InvalidOperationException("Playback control must contain one strict command frame per write.");
            }
            var (Command, Sequence) = expected.Dequeue();
            if (frame.Command != Command || frame.Sequence != Sequence ||
                frame.Command == MiPlayProtocolConstants.AddMirrorCommand)
            {
                throw new InvalidOperationException("The MiPlay playback-control ledger changed.");
            }
            await WriteAsync(stream, write.Frames[0], cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool ContainsCommandSequence(
        IReadOnlyList<MiPlayLegacyAudioSourceWrite> writes,
        ushort command,
        ushort sequence) =>
        writes.SelectMany(write => write.Frames).Any(bytes =>
            MiPlayCommandFrameCodec.TryDecode(bytes, out var frame, out var consumed) &&
            frame is not null && consumed == bytes.Length &&
            frame.Command == command && frame.Sequence == sequence);

    private static async Task SendPostOpenWritesAsync(
        NetworkStream stream,
        IReadOnlyList<MiPlayLegacyAudioSourceWrite> writes,
        CancellationToken cancellationToken)
    {
        foreach (var write in writes)
        {
            if (write.Frames.Count != 1 ||
                !MiPlayCommandFrameCodec.TryDecode(write.Frames[0], out var frame, out var consumed) ||
                frame is null || consumed != write.Frames[0].Length ||
                frame.Command != MiPlayProtocolConstants.SetMediaInfoCommand ||
                frame.Sequence != MiPlayLegacyPostOpenPlaybackSession.SetMediaInfoSequence ||
                !MiPlaySetMediaInfoPayloadCodec.TryDecode(frame.Payload, out var mediaInfo) ||
                mediaInfo is not { Status: 0, DeviceState: 2 })
            {
                throw new InvalidOperationException("Post-Open startup attempted a frame other than the validated SetMediaInfo.");
            }
            await WriteAsync(stream, write.Frames[0], cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RunPostOpenControlAsync(
        NetworkStream controlStream,
        MiPlayLegacyPostOpenPlaybackSession session,
        long preOpenHeartbeatSentAt,
        Task firstMediaSent,
        ActiveSourcePlaybackControl sourcePlaybackControl,
        MiPlayRuntimeTimeOffsetControl runtimeTimeOffsetControl,
        ActiveMediaTimeline mediaTimeline,
        Action receiverReady,
        Action<string> reportEvidence,
        CancellationToken cancellationToken,
        bool useTabletHeartbeat = false,
        Action<string>? reportRuntimeEvidence = null,
        MiPlaySharedAudioSession.MiPlaySharedAudioSubscription? sharedSubscription = null,
        SwitchableAudioCaptureSource? localCapture = null,
        PcmFrameBuffer? localPcmBuffer = null,
        MiPlayExplicitPlaybackGate? explicitCapturePlaybackGate = null,
        MiPlayExplicitPlaybackGate? explicitMediaPlaybackGate = null)
    {
        ArgumentNullException.ThrowIfNull(firstMediaSent);
        ArgumentNullException.ThrowIfNull(reportEvidence);
        await WaitForReceiverReadinessWindowAsync(
            firstMediaSent,
            async readinessToken =>
            {
                while (session.Phase == MiPlayLegacyPostOpenPlaybackPhase.AwaitingReceiverPlaybackReadiness)
                {
                    var inbound = await ReadCommandFrameAsync(controlStream, readinessToken).ConfigureAwait(false);
                    var transition = session.ProcessInbound(inbound);
                    if (!transition.Accepted)
                    {
                        throw new InvalidOperationException($"Post-Open control stopped: {transition.Boundary}");
                    }
                    reportEvidence(MiPlayRuntimeWireEvidence.DescribePostOpenInbound(
                        inbound,
                        transition,
                        session));
                }
            },
            TimeSpan.FromSeconds(5),
            cancellationToken).ConfigureAwait(false);
        if (session.Phase != MiPlayLegacyPostOpenPlaybackPhase.Playing)
        {
            throw new InvalidOperationException("The receiver did not enter the automatic playing state.");
        }
        var volumeControl = new ActiveVolumeControl();
        ActivateSourcePlaybackControl(sourcePlaybackControl, sharedSubscription);
        try
        {
            sharedSubscription?.RegisterPlaybackControl(
                (request, token) => sourcePlaybackControl.EnqueueAsync(
                    (request.Command, request.PreserveMediaTimeline) switch
                    {
                        (MiPlaySharedPlaybackControlCommand.Pause, false) =>
                            SourcePlaybackCommand.Pause,
                        (MiPlaySharedPlaybackControlCommand.Resume, false) =>
                            SourcePlaybackCommand.Resume,
                        (MiPlaySharedPlaybackControlCommand.Pause, true) =>
                            SourcePlaybackCommand.PauseReceiver,
                        (MiPlaySharedPlaybackControlCommand.Resume, true) =>
                            SourcePlaybackCommand.ResumeReceiver,
                        _ => throw new ArgumentOutOfRangeException(nameof(request.Command)),
                    },
                    request.Generation,
                    token));
            ActivateVolumeControl(volumeControl);
        }
        catch
        {
            sharedSubscription?.UnregisterPlaybackControl();
            DeactivateSourcePlaybackControl(
                sourcePlaybackControl,
                new InvalidOperationException("The MiPlay runtime control session could not start."));
            throw;
        }
        Exception? failure = null;
        try
        {
            receiverReady();
            await RunRuntimeControlsAsync(
                controlStream,
                preOpenHeartbeatSentAt,
                volumeControl,
                sourcePlaybackControl,
                runtimeTimeOffsetControl,
                mediaTimeline,
                cancellationToken,
                useTabletHeartbeat,
                reportRuntimeEvidence,
                sharedSubscription,
                localCapture,
                localPcmBuffer,
                explicitCapturePlaybackGate,
                explicitMediaPlaybackGate).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failure = exception;
            throw;
        }
        finally
        {
            var reason = failure ?? new OperationCanceledException("The MiPlay runtime control session ended.");
            sharedSubscription?.UnregisterPlaybackControl();
            DeactivateSourcePlaybackControl(sourcePlaybackControl, reason);
            DeactivateVolumeControl(
                volumeControl,
                reason);
        }
    }

    internal static async Task WaitForReceiverReadinessWindowAsync(
        Task firstMediaSent,
        Func<CancellationToken, Task> readUntilReady,
        TimeSpan readinessTimeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(firstMediaSent);
        ArgumentNullException.ThrowIfNull(readUntilReady);
        if (readinessTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(readinessTimeout));
        }

        // The receiver cannot confirm decoded PCM before any AAC/RTP bytes have
        // reached it. Encoder startup latency must not consume this window.
        await firstMediaSent.WaitAsync(cancellationToken).ConfigureAwait(false);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(readinessTimeout);
        try
        {
            await readUntilReady(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (
            timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                "The receiver did not report first-audiopcm=1 and state=2 within five seconds of the first media batch.");
        }
    }

    private async Task RunRuntimeControlsAsync(
        NetworkStream stream,
        long preOpenHeartbeatSentAt,
        ActiveVolumeControl volumeControl,
        ActiveSourcePlaybackControl sourcePlaybackControl,
        MiPlayRuntimeTimeOffsetControl runtimeTimeOffsetControl,
        ActiveMediaTimeline mediaTimeline,
        CancellationToken cancellationToken,
        bool useTabletHeartbeat = false,
        Action<string>? reportEvidence = null,
        MiPlaySharedAudioSession.MiPlaySharedAudioSubscription? sharedSubscription = null,
        SwitchableAudioCaptureSource? localCapture = null,
        PcmFrameBuffer? localPcmBuffer = null,
        MiPlayExplicitPlaybackGate? explicitCapturePlaybackGate = null,
        MiPlayExplicitPlaybackGate? explicitMediaPlaybackGate = null)
    {
        var sequence = new MiPlayLegacyRuntimeControlSequence();
        await using var sharedControl = useTabletHeartbeat
            ? new MiPlaySharedRuntimeControlChannel(
                token => ReadCommandFrameAsync(stream, token),
                (bytes, token) => WriteAsync(stream, bytes, token),
                reportEvidence ?? (_ => { }),
                cancellationToken,
                notificationReceived: notification =>
                {
                    if (notification.Label == "state" &&
                        notification.IntegerValue is 2 or 3)
                    {
                        sourcePlaybackControl.SetReceiverPaused(notification.IntegerValue == 3);
                    }
                })
            : null;
        Task ExecuteAsync(MiPlayLegacyRuntimeControlCommand command) => sharedControl is null
            ? ExecuteRuntimeControlAsync(stream, command, cancellationToken)
            : sharedControl.ExecuteAsync(command, cancellationToken);
        async Task SendResumePairAsync()
        {
            var resumePair = sequence.PrepareResumePair();
            await ExecuteAsync(resumePair.First).ConfigureAwait(false);
            await Task.Delay(
                MiPlayLegacyRuntimeControlSequence.CapturedResumeRepeatDelayMilliseconds,
                cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(resumePair.Second).ConfigureAwait(false);
        }

        var heartbeatDueAt = MiPlayLegacyStreamingHeartbeatPlan.CalculateDueTimestamp(
            preOpenHeartbeatSentAt,
            MiPlayLegacyStreamingHeartbeatPlan.IntervalMilliseconds,
            Stopwatch.Frequency);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (sharedControl?.Completion.IsCompleted == true)
            {
                await sharedControl.Completion.ConfigureAwait(false);
            }
            if (Stopwatch.GetTimestamp() >= heartbeatDueAt)
            {
                await ExecuteAsync(sequence.PrepareHeartbeat()).ConfigureAwait(false);
                heartbeatDueAt = MiPlayLegacyStreamingHeartbeatPlan.CalculateDueTimestamp(
                    sharedControl is null ? heartbeatDueAt : Stopwatch.GetTimestamp(),
                    MiPlayLegacyStreamingHeartbeatPlan.IntervalMilliseconds,
                    Stopwatch.Frequency);
                continue;
            }

            if (sourcePlaybackControl.TryRead(out var playbackRequest))
            {
                if (!playbackRequest.TryStart())
                {
                    continue;
                }

                try
                {
                    switch (playbackRequest.Command)
                    {
                        case SourcePlaybackCommand.Pause:
                            if (localCapture is not null &&
                                localPcmBuffer is not null &&
                                explicitCapturePlaybackGate is not null)
                            {
                                explicitCapturePlaybackGate.Pause();
                                explicitMediaPlaybackGate?.Pause();
                                await localCapture.PauseAsync(cancellationToken)
                                    .ConfigureAwait(false);
                                localPcmBuffer.TrimToLatest(0);
                            }
                            goto case SourcePlaybackCommand.PauseReceiver;
                        case SourcePlaybackCommand.PauseReceiver:
                            await ExecuteAsync(sequence.PreparePause()).ConfigureAwait(false);
                            sourcePlaybackControl.SetReceiverPaused(true);
                            break;
                        case SourcePlaybackCommand.Resume:
                            if (localCapture is not null &&
                                localPcmBuffer is not null &&
                                explicitCapturePlaybackGate is not null)
                            {
                                localPcmBuffer.TrimToLatest(0);
                                await localCapture.ResumeAsync(cancellationToken)
                                    .ConfigureAwait(false);
                                explicitCapturePlaybackGate.Resume();
                            }
                            if (!mediaTimeline.TryGetNextMediaPosition90Khz(out var mediaPosition90Khz))
                            {
                                throw new InvalidOperationException(
                                    "MiPlay cannot resume before the first media position is available.");
                            }
                            var resumedTimeOffsetCandidate =
                                MiPlayMediaStallRecoveryPlan.CalculateRuntimeTimeOffsetMicroseconds(
                                    GetMonotonicMicroseconds(),
                                    mediaPosition90Khz,
                                    mediaTimeline.PlaybackDelayMicroseconds);
                            var resumedTimeOffset = resumedTimeOffsetCandidate;
                            MiPlaySharedTimeOffsetUpdateKey? timeOffsetUpdateKey = null;
                            if (sharedSubscription is not null)
                            {
                                timeOffsetUpdateKey = playbackRequest.SharedGeneration is { } generation
                                    ? new MiPlaySharedTimeOffsetUpdateKey(
                                        MiPlaySharedTimeOffsetUpdateKind.GroupResume,
                                        checked((ulong)generation))
                                    : new MiPlaySharedTimeOffsetUpdateKey(
                                        MiPlaySharedTimeOffsetUpdateKind.Resume,
                                        mediaPosition90Khz);
                                resumedTimeOffset = sharedSubscription.PrepareRuntimeTimeOffsetUpdate(
                                    timeOffsetUpdateKey.Value,
                                    resumedTimeOffsetCandidate);
                            }
                            await runtimeTimeOffsetControl.UpdateAsync(
                                    resumedTimeOffset,
                                    cancellationToken)
                                .ConfigureAwait(false);
                            if (timeOffsetUpdateKey is { } committedTimeOffsetUpdateKey)
                            {
                                sharedSubscription!.CommitRuntimeTimeOffsetUpdate(
                                    committedTimeOffsetUpdateKey,
                                    resumedTimeOffset);
                            }
                            await SendResumePairAsync().ConfigureAwait(false);
                            sourcePlaybackControl.SetReceiverPaused(false);
                            explicitMediaPlaybackGate?.Resume();
                            break;
                        case SourcePlaybackCommand.ResumeReceiver:
                            await SendResumePairAsync().ConfigureAwait(false);
                            sourcePlaybackControl.SetReceiverPaused(false);
                            break;
                        case SourcePlaybackCommand.Close:
                            await ExecuteAsync(sequence.PrepareClose()).ConfigureAwait(false);
                            sourcePlaybackControl.SetReceiverPaused(false);
                            break;
                        default:
                            throw new ArgumentOutOfRangeException(nameof(playbackRequest.Command));
                    }
                    playbackRequest.Succeed();
                    if (playbackRequest.Command == SourcePlaybackCommand.Close)
                    {
                        return;
                    }
                }
                catch (Exception exception)
                {
                    playbackRequest.Fail(exception);
                    throw;
                }
                continue;
            }

            if (volumeControl.TryRead(out var request))
            {
                if (!request.TryStart())
                {
                    continue;
                }

                try
                {
                    await ExecuteAsync(sequence.PrepareSetVolume(request.Volume)).ConfigureAwait(false);
                    UpdateReceiverVolume(request.Volume);
                    request.Succeed();
                }
                catch (Exception exception)
                {
                    request.Fail(exception);
                    throw;
                }
                continue;
            }

            var remaining = heartbeatDueAt - Stopwatch.GetTimestamp();
            if (remaining <= 0)
            {
                continue;
            }
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var volumeReady = volumeControl.WaitToReadAsync(wait.Token).AsTask();
            var sourcePlaybackReady = sourcePlaybackControl.WaitToReadAsync(wait.Token).AsTask();
            var heartbeatDelay = Task.Delay(
                TimeSpan.FromSeconds(remaining / (double)Stopwatch.Frequency),
                wait.Token);
            var completed = await Task.WhenAny(sharedControl is null
                ? [volumeReady, sourcePlaybackReady, heartbeatDelay]
                : new Task[] { volumeReady, sourcePlaybackReady, heartbeatDelay, sharedControl.Completion })
                .ConfigureAwait(false);
            wait.Cancel();
            foreach (var pending in new Task[] { volumeReady, sourcePlaybackReady, heartbeatDelay })
            {
                try
                {
                    await pending.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (
                    wait.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                }
            }
            if (sharedControl is not null && ReferenceEquals(completed, sharedControl.Completion))
            {
                await sharedControl.Completion.ConfigureAwait(false);
            }
            if (ReferenceEquals(completed, volumeReady) && !volumeReady.Result)
            {
                throw new InvalidOperationException("The MiPlay volume command queue closed unexpectedly.");
            }
            if (ReferenceEquals(completed, sourcePlaybackReady) && !sourcePlaybackReady.Result)
            {
                throw new InvalidOperationException("The MiPlay source-playback command queue closed unexpectedly.");
            }
        }
    }

    private static async Task ExecuteRuntimeControlAsync(
        NetworkStream stream,
        MiPlayLegacyRuntimeControlCommand command,
        CancellationToken cancellationToken)
    {
        await WriteAsync(stream, command.CommandFrame, cancellationToken).ConfigureAwait(false);
        if (!command.WaitForAcknowledgement)
        {
            return;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            for (var index = 0; index < 8; index++)
            {
                var inboundBytes = await ReadCommandFrameAsync(stream, timeout.Token).ConfigureAwait(false);
                if (!MiPlayCommandFrameCodec.TryDecode(inboundBytes, out var inbound, out var consumed) ||
                    inbound is null || consumed != inboundBytes.Length)
                {
                    throw new InvalidDataException("The runtime control reader received a malformed command frame.");
                }
                if (MiPlayLegacyRuntimeControlSequence.IsExpectedAcknowledgement(command, inbound))
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (
            timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"MiPlay command 0x{command.Command:X4} sequence 0x{command.Sequence:X4} was not acknowledged.");
        }

        throw new TimeoutException(
            $"MiPlay command 0x{command.Command:X4} sequence 0x{command.Sequence:X4} was not acknowledged within eight inbound frames.");
    }

    private void ActivateVolumeControl(ActiveVolumeControl control)
    {
        lock (volumeSync)
        {
            if (activeVolumeControl is not null)
            {
                throw new InvalidOperationException("A MiPlay receiver-volume session is already active.");
            }
            activeVolumeControl = control;
        }
    }

    private void DeactivateVolumeControl(ActiveVolumeControl control, Exception reason)
    {
        lock (volumeSync)
        {
            if (ReferenceEquals(activeVolumeControl, control))
            {
                activeVolumeControl = null;
            }
        }
        control.Complete(reason);
    }

    private void ActivateSourcePlaybackControl(
        ActiveSourcePlaybackControl control,
        MiPlaySharedAudioSession.MiPlaySharedAudioSubscription? sharedSubscription)
    {
        lock (playbackSync)
        {
            if (activeSourcePlaybackControl is not null)
            {
                throw new InvalidOperationException("A MiPlay source-playback session is already active.");
            }
            activeSourcePlaybackControl = control;
            activeSharedPlaybackSubscription = sharedSubscription;
        }
    }

    private void DeactivateSourcePlaybackControl(ActiveSourcePlaybackControl control, Exception reason)
    {
        lock (playbackSync)
        {
            if (ReferenceEquals(activeSourcePlaybackControl, control))
            {
                activeSourcePlaybackControl = null;
                activeSharedPlaybackSubscription = null;
            }
        }
        control.Complete(reason);
    }

    private void UpdateReceiverVolume(int volume)
    {
        bool changed;
        lock (volumeSync)
        {
            changed = receiverVolume != volume;
            receiverVolume = volume;
        }
        if (changed)
        {
            ReceiverVolumeChanged?.Invoke(this, new(volume));
        }
    }

    private void ResetReceiverVolume()
    {
        lock (volumeSync)
        {
            receiverVolume = null;
        }
    }

    private static async Task RunTimerResponderAsync(
        UdpClient timer,
        IPAddress receiver,
        CancellationToken cancellationToken,
        bool useTabletTimerBehavior = false)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var received = await timer.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            if (!received.RemoteEndPoint.Address.Equals(receiver))
            {
                if (useTabletTimerBehavior)
                {
                    // Retain the session peer boundary while matching the
                    // native responder's persistent loop. A foreign datagram
                    // must not terminate clock service for the real receiver.
                    continue;
                }
                throw new InvalidOperationException("The MiPlay timer received an unexpected peer or packet.");
            }

            if (useTabletTimerBehavior)
            {
                // Native TimerService::Sync answers every positive recvfrom
                // result with a 40-byte, zero-filled/truncated response. A
                // zero-length UDP datagram is the only packet it ignores.
                if (received.Buffer.Length == 0)
                {
                    continue;
                }
            }
            else if (received.Buffer.Length != MiPlayWfdTimerPacketCodec.PacketLength)
            {
                throw new InvalidOperationException("The MiPlay timer received an unexpected peer or packet.");
            }

            var receiveTime = GetMonotonicMicroseconds();
            var sendTime = GetMonotonicMicroseconds();
            var response = useTabletTimerBehavior
                ? MiPlayWfdTimerPacketCodec.CreateTabletResponse(received.Buffer, receiveTime, sendTime)
                : MiPlayWfdTimerPacketCodec.CreateResponse(received.Buffer, receiveTime, sendTime);
            try
            {
                await timer.SendAsync(response, received.RemoteEndPoint, cancellationToken).ConfigureAwait(false);
            }
            catch (SocketException exception) when (
                ShouldIgnoreTimerSendFailure(
                    exception,
                    useTabletTimerBehavior,
                    cancellationToken))
            {
                // Native TimerService::Sync ignores sendto's return value and
                // returns true to TimerWork::threadLoop. One failed UDP reply
                // therefore must not tear down an otherwise healthy shared
                // media session; the next receiver request gets a fresh pair
                // of monotonic timestamps.
            }
        }
    }

    internal static bool ShouldIgnoreTimerSendFailure(
        Exception exception,
        bool useTabletTimerBehavior,
        CancellationToken cancellationToken) =>
        useTabletTimerBehavior &&
        !cancellationToken.IsCancellationRequested &&
        exception is SocketException;

    private static async Task RunRtspControlAsync(
        TcpClient client,
        IPAddress sourceAddress,
        int timerPort,
        TaskCompletionSource beforeTimeOffset,
        Task postOpenContextSent,
        Task<ulong> sharedInitialTimeOffset,
        TaskCompletionSource<ulong> ready,
        MiPlayRuntimeTimeOffsetControl runtimeTimeOffsetControl,
        MiPlayReceiverLatencyTelemetry receiverLatencyTelemetry,
        MiPlaySharedAudioSession.MiPlaySharedAudioSubscription? sharedSubscription,
        CancellationToken cancellationToken)
    {
        MiPlayRuntimeTimeOffsetRequest? inFlightRuntimeTimeOffset = null;
        try
        {
            await using var stream = client.GetStream();
            var sessionId = RandomNumberGenerator.GetInt32(100_000_000, 1_000_000_000).ToString();
            var session = new MiPlayWfdSourceRtspSession(
                sourceAddress,
                timerPort,
                sessionId,
                waitForInitialTimeOffsetAcknowledgement: sharedSubscription is null,
                waitForRuntimeTimeOffsetAcknowledgement: runtimeTimeOffsetControl.WaitForAcknowledgement);
            await SendRtspMessagesAsync(
                stream,
                session.Start(DateTimeOffset.UtcNow).OutboundMessages,
                cancellationToken).ConfigureAwait(false);

            var pending = new byte[64 * 1024];
            var pendingCount = 0;
            var readBuffer = new byte[8 * 1024];
            Task<int>? pendingRead = null;
            Task<bool>? runtimeRequestReady = null;
            while (!cancellationToken.IsCancellationRequested)
            {
                while (MiPlayRtspWireMessageCodec.TryDecode(
                           pending.AsSpan(0, pendingCount),
                           out var inboundMessage,
                           out var consumed))
                {
                    receiverLatencyTelemetry.TryObserve(inboundMessage!);
                    var monotonicMicroseconds = GetMonotonicMicroseconds();
                    var runtimeTimeOffsetWasPending = session.RuntimeTimeOffsetPending;
                    var transition = session.ProcessInbound(
                        pending.AsSpan(0, consumed),
                        DateTimeOffset.UtcNow,
                        monotonicMicroseconds);
                    if (!transition.Accepted)
                    {
                        throw new InvalidOperationException($"RTSP stopped: {transition.Boundary}");
                    }
                    pending.AsSpan(consumed, pendingCount - consumed).CopyTo(pending);
                    pendingCount -= consumed;
                    if (session.Phase == MiPlayWfdSourceRtspPhase.AwaitingTimeOffsetAcknowledgement &&
                        transition.OutboundMessages.Count == 2 &&
                        !beforeTimeOffset.Task.IsCompleted)
                    {
                        await SendRtspMessagesAsync(
                            stream,
                            [transition.OutboundMessages[0]],
                            cancellationToken).ConfigureAwait(false);
                        beforeTimeOffset.TrySetResult();
                        await postOpenContextSent.WaitAsync(cancellationToken).ConfigureAwait(false);
                        IReadOnlyList<byte[]> initialTimeOffsetMessages = [transition.OutboundMessages[1]];
                        if (sharedSubscription is not null)
                        {
                            var synchronizedTimeOffset = await sharedInitialTimeOffset
                                .WaitAsync(cancellationToken)
                                .ConfigureAwait(false);
                            var replacement = session.ReplacePendingInitialTimeOffset(
                                DateTimeOffset.UtcNow,
                                synchronizedTimeOffset);
                            if (!replacement.Accepted || replacement.OutboundMessages.Count != 1)
                            {
                                throw new InvalidOperationException(
                                    $"RTSP stopped: {replacement.Boundary}");
                            }
                            initialTimeOffsetMessages = replacement.OutboundMessages;
                        }
                        await SendRtspMessagesAsync(
                            stream,
                            initialTimeOffsetMessages,
                            cancellationToken).ConfigureAwait(false);
                        // Native onReceiveTimeOffset does not gate media.
                        // Only release shared startup after the write succeeds;
                        // the separate AAC runway and common start barrier stay.
                        transition = session.MarkInitialTimeOffsetSent();
                        if (!transition.Accepted)
                        {
                            throw new InvalidOperationException($"RTSP stopped: {transition.Boundary}");
                        }
                    }
                    else
                    {
                        await SendRtspMessagesAsync(
                            stream,
                            transition.OutboundMessages,
                            cancellationToken).ConfigureAwait(false);
                    }
                    if (transition.Ready)
                    {
                        if (session.TimeOffsetMicroseconds is not ulong timeOffset)
                        {
                            throw new InvalidOperationException("RTSP became ready without TIME_OFFSET.");
                        }
                        ready.TrySetResult(timeOffset);
                    }
                    if (runtimeTimeOffsetWasPending && !session.RuntimeTimeOffsetPending)
                    {
                        if (inFlightRuntimeTimeOffset is null)
                        {
                            throw new InvalidOperationException(
                                "RTSP acknowledged a runtime TIME_OFFSET without an active request.");
                        }
                        inFlightRuntimeTimeOffset.Succeed();
                        inFlightRuntimeTimeOffset = null;
                    }
                }

                if (session.Phase == MiPlayWfdSourceRtspPhase.Ready &&
                    inFlightRuntimeTimeOffset is null &&
                    runtimeTimeOffsetControl.TryRead(out var runtimeRequest))
                {
                    runtimeRequestReady = null;
                    if (!runtimeRequest.TryStart())
                    {
                        continue;
                    }

                    var transition = session.PrepareRuntimeTimeOffset(
                        DateTimeOffset.UtcNow,
                        runtimeRequest.TimeOffsetMicroseconds);
                    if (!transition.Accepted)
                    {
                        var exception = new InvalidOperationException(
                            $"RTSP runtime TIME_OFFSET stopped: {transition.Boundary}");
                        runtimeRequest.Fail(exception);
                        throw exception;
                    }
                    try
                    {
                        await SendRtspMessagesAsync(
                            stream,
                            transition.OutboundMessages,
                            cancellationToken).ConfigureAwait(false);
                        var sent = session.MarkRuntimeTimeOffsetSent();
                        if (!sent.Accepted)
                        {
                            throw new InvalidOperationException($"RTSP runtime TIME_OFFSET stopped: {sent.Boundary}");
                        }
                    }
                    catch (Exception exception)
                    {
                        runtimeRequest.Fail(exception);
                        throw;
                    }
                    if (session.RuntimeTimeOffsetPending)
                    {
                        inFlightRuntimeTimeOffset = runtimeRequest;
                    }
                    else
                    {
                        runtimeRequest.Succeed();
                    }
                    continue;
                }

                pendingRead ??= stream.ReadAsync(readBuffer, cancellationToken).AsTask();
                if (session.Phase == MiPlayWfdSourceRtspPhase.Ready &&
                    inFlightRuntimeTimeOffset is null)
                {
                    runtimeRequestReady ??=
                        runtimeTimeOffsetControl.WaitToReadAsync(cancellationToken).AsTask();
                    if (ReferenceEquals(
                            await Task.WhenAny(pendingRead, runtimeRequestReady).ConfigureAwait(false),
                            runtimeRequestReady))
                    {
                        if (!await runtimeRequestReady.ConfigureAwait(false))
                        {
                            throw new InvalidOperationException(
                                "The runtime TIME_OFFSET request queue closed unexpectedly.");
                        }
                        runtimeRequestReady = null;
                        continue;
                    }
                }

                var read = await pendingRead.ConfigureAwait(false);
                pendingRead = null;
                if (read == 0)
                {
                    throw new EndOfStreamException("The receiver closed RTSP before the MiPlay session ended.");
                }
                if (pendingCount + read > pending.Length)
                {
                    throw new InvalidOperationException("RTSP input exceeded the bounded buffer.");
                }
                readBuffer.AsSpan(0, read).CopyTo(pending.AsSpan(pendingCount));
                pendingCount += read;
            }
        }
        catch (Exception exception)
        {
            inFlightRuntimeTimeOffset?.Fail(exception);
            runtimeTimeOffsetControl.Complete(exception);
            beforeTimeOffset.TrySetException(exception);
            ready.TrySetException(exception);
            throw;
        }
    }

    private static async Task SendRtspMessagesAsync(
        NetworkStream stream,
        IReadOnlyList<byte[]> messages,
        CancellationToken cancellationToken)
    {
        foreach (var message in messages)
        {
            if (!MiPlayRtspWireMessageCodec.TryDecode(message, out _, out var consumed) ||
                consumed != message.Length)
            {
                throw new InvalidOperationException("The RTSP sender refused an incomplete message.");
            }
            await WriteAsync(stream, message, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<byte[]> ReadCommandFrameAsync(
        NetworkStream stream,
        CancellationToken cancellationToken)
    {
        var header = new byte[MiPlayProtocolConstants.CommandHeaderLength];
        await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        if (header[0] != MiPlayProtocolConstants.CommandFrameMagic)
        {
            throw new InvalidDataException("Unexpected MiPlay command-frame magic.");
        }
        var payloadLength = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(5, 4));
        if (payloadLength > MiPlayCommandFrameCodec.MaximumPayloadLength)
        {
            throw new InvalidDataException("MiPlay command payload exceeds the safety limit.");
        }
        var frame = new byte[header.Length + checked((int)payloadLength)];
        header.CopyTo(frame, 0);
        await stream.ReadExactlyAsync(
            frame.AsMemory(header.Length, checked((int)payloadLength)),
            cancellationToken).ConfigureAwait(false);
        return frame;
    }

    private static async Task WriteAsync(
        NetworkStream stream,
        ReadOnlyMemory<byte> bytes,
        CancellationToken cancellationToken)
    {
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    internal static async Task WriteSharedMediaAsync(
        NetworkStream stream,
        ReadOnlyMemory<byte> bytes,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        using var write = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        write.CancelAfter(timeout);
        try
        {
            await WriteAsync(stream, bytes, write.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (
            write.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"The shared MiPlay audio socket remained blocked for more than " +
                $"{timeout.TotalSeconds:F0} seconds; this receiver will leave the group so the " +
                "remaining speakers can continue.",
                exception);
        }
    }

    internal static void ThrowIfBackgroundFailed(params Task[] tasks)
    {
        var failed = tasks.FirstOrDefault(task => task.IsFaulted);
        if (failed?.Exception is { } exception)
        {
            throw exception.InnerException ?? exception;
        }
        if (tasks.Any(task => task.IsCanceled))
        {
            throw new InvalidOperationException(
                "A MiPlay background channel stopped before the media session was canceled.");
        }
    }

    internal static async Task<T> WaitForStartupPhaseAsync<T>(
        Task<T> task,
        TimeSpan timeout,
        string timeoutMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            return await task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException exception)
        {
            throw new TimeoutException(timeoutMessage, exception);
        }
    }

    internal static async Task WaitForStartupPhaseAsync(
        Task task,
        TimeSpan timeout,
        string timeoutMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            await task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException exception)
        {
            throw new TimeoutException(timeoutMessage, exception);
        }
    }

    internal static string FormatBootstrapProgress(MiPlayLegacyAudioSourceProgress progress) =>
        $"phase={progress.Phase}; " +
        $"deviceInfo={(progress.DeviceInfoAcknowledged ? 1 : 0)}, " +
        $"sourceName={(progress.SourceNameAcknowledged ? 1 : 0)}, " +
        $"account={(progress.AccountAcknowledged ? 1 : 0)}, " +
        $"mirror={(progress.MirrorModeAcknowledged ? 1 : 0)}, " +
        $"queries={(progress.StatusQueriesPrepared ? 1 : 0)}, " +
        $"volume={(progress.VolumeAcknowledged ? 1 : 0)}, " +
        $"state={(progress.StateAcknowledged ? 1 : 0)}, " +
        $"mediaInfo={(progress.MediaInfoObserved ? 1 : 0)}.";

    private static ulong GetMonotonicMicroseconds() =>
        MiPlayWfdMediaClock.ConvertStopwatchTicksToMicroseconds(
            Stopwatch.GetTimestamp(),
            Stopwatch.Frequency);

    private sealed class ActiveMediaTimeline
    {
        private readonly Lock gate = new();
        private ulong nextMediaPosition90Khz;
        private bool available;

        public ActiveMediaTimeline()
            : this(MiPlayProtocolConstants.OtherNetworkPlaybackDelayMicroseconds)
        {
        }

        public ActiveMediaTimeline(int playbackDelayMicroseconds)
        {
            PlaybackDelayMicroseconds = playbackDelayMicroseconds;
        }

        public int PlaybackDelayMicroseconds { get; }

        public void Update(ulong value)
        {
            lock (gate)
            {
                nextMediaPosition90Khz = value;
                available = true;
            }
        }

        public bool TryGetNextMediaPosition90Khz(out ulong value)
        {
            lock (gate)
            {
                value = nextMediaPosition90Khz;
                return available;
            }
        }
    }

    private sealed class ActiveSourcePlaybackControl
    {
        private readonly Channel<SourcePlaybackRequest> requests = Channel.CreateUnbounded<SourcePlaybackRequest>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false,
            });
        private int receiverPaused;
        public bool ReceiverPaused => Volatile.Read(ref receiverPaused) != 0;

        public async Task EnsureResumedAsync(CancellationToken cancellationToken)
        {
            if (!ReceiverPaused)
            {
                return;
            }
            await EnqueueAsync(SourcePlaybackCommand.Resume, cancellationToken).ConfigureAwait(false);
        }

        public async Task EnqueueAsync(
            SourcePlaybackCommand command,
            CancellationToken cancellationToken)
        {
            await EnqueueAsync(command, sharedGeneration: null, cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task EnqueueAsync(
            SourcePlaybackCommand command,
            long? sharedGeneration,
            CancellationToken cancellationToken)
        {
            var request = new SourcePlaybackRequest(command, sharedGeneration);
            await requests.Writer.WriteAsync(request, cancellationToken).ConfigureAwait(false);
            using var cancellationRegistration = cancellationToken.Register(
                static state => ((SourcePlaybackRequest)state!).CancelIfPending(),
                request);
            await request.Completion.ConfigureAwait(false);
        }

        public bool TryRead(out SourcePlaybackRequest request) => requests.Reader.TryRead(out request!);

        public ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken) =>
            requests.Reader.WaitToReadAsync(cancellationToken);

        public void Complete(Exception reason)
        {
            requests.Writer.TryComplete(reason);
            while (requests.Reader.TryRead(out var request))
            {
                request.Fail(reason);
            }
        }

        public void SetReceiverPaused(bool value) =>
            Volatile.Write(ref receiverPaused, value ? 1 : 0);
    }

    private enum SourcePlaybackCommand
    {
        Pause,
        Resume,
        PauseReceiver,
        ResumeReceiver,
        Close,
    }

    private sealed class SourcePlaybackRequest(
        SourcePlaybackCommand command,
        long? sharedGeneration)
    {
        private readonly TaskCompletionSource completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int state;

        public SourcePlaybackCommand Command { get; } = command;
        public long? SharedGeneration { get; } = sharedGeneration;
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
            var previous = Interlocked.Exchange(ref state, 2);
            if (previous != 2)
            {
                completion.TrySetException(exception);
            }
        }
    }

    private sealed class ActiveVolumeControl
    {
        private readonly Channel<VolumeRequest> requests = Channel.CreateUnbounded<VolumeRequest>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false,
            });

        public async Task EnqueueAsync(int volume, CancellationToken cancellationToken)
        {
            var request = new VolumeRequest(volume);
            await requests.Writer.WriteAsync(request, cancellationToken).ConfigureAwait(false);
            using var cancellationRegistration = cancellationToken.Register(
                static state => ((VolumeRequest)state!).CancelIfPending(),
                request);
            await request.Completion.ConfigureAwait(false);
        }

        public bool TryRead(out VolumeRequest request) => requests.Reader.TryRead(out request!);

        public ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken) =>
            requests.Reader.WaitToReadAsync(cancellationToken);

        public void Complete(Exception reason)
        {
            requests.Writer.TryComplete(reason);
            while (requests.Reader.TryRead(out var request))
            {
                request.Fail(reason);
            }
        }
    }

    private sealed class VolumeRequest(int volume)
    {
        private readonly TaskCompletionSource completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int state;

        public int Volume { get; } = volume;
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
            var previous = Interlocked.Exchange(ref state, 2);
            if (previous != 2)
            {
                completion.TrySetException(exception);
            }
        }
    }

    private sealed class CancellationSourceLease(CancellationToken cancellationToken) : IDisposable
    {
        public CancellationTokenSource Source { get; } = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        public void Dispose()
        {
            Source.Cancel();
            Source.Dispose();
        }
    }

}
