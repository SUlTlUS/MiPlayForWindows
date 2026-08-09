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
/// independent reverse endpoints for each session. Cancellation closes owned sockets; it does not invent a Close,
/// Pause, Resume, or AddMirror command.
/// </summary>
public sealed class MiPlayLegacySystemAudioSessionRunner(IAudioSourceCatalog audioSources) :
    IMiPlaySystemAudioSessionRunner,
    IMiPlayReceiverVolumeController,
    IMiPlayAudioCaptureController
{
    private const int OpenEndedMediaDurationMilliseconds = int.MaxValue;
    public const string ValidatedSourceName = "MI PAD 4/Plus";
    public const MiPlayLegacyStatusQueryOrder ValidatedStatusQueryOrder =
        MiPlayLegacyStatusQueryOrder.VolumeStateMediaInfo;

    private readonly IAudioSourceCatalog audioSources = audioSources ?? throw new ArgumentNullException(nameof(audioSources));
    private readonly Lock volumeSync = new();
    private readonly SemaphoreSlim captureGate = new(1, 1);
    private ActiveVolumeControl? activeVolumeControl;
    private SwitchableAudioCaptureSource? activeCapture;
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
        var sharedSubscription = sharedAudioSession?.Subscribe();
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
                $"Connecting to {request.Renderer.FriendlyName} ({targetAddress})…")));
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
        var timerTask = RunTimerResponderAsync(timer, targetAddress, run.Token);

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
            ? new PcmFrameBuffer(request.ChannelRoute)
            : null;
        var pcmBuffer = sharedSubscription?.PcmBuffer ?? localPcmBuffer!;
        await using var localCapture = sharedAudioSession is null
            ? await StartCaptureAsync(pcmBuffer, request.Selection, cancellationToken).ConfigureAwait(false)
            : null;
        var capture = sharedAudioSession?.Capture ?? localCapture!;
        await using var activeCaptureRegistration = localCapture is null
            ? null
            : new ActiveCaptureRegistration(this, localCapture);
        await pcmBuffer.PrepareForPlaybackAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        await using var encoder = MiPlayFfmpegAacEncoder.Start(
            request.FfmpegPath,
            MiPlayProtocolConstants.AacBitRate,
            "aac_mf");
        using var encoderInputLease = new CancellationSourceLease(cancellationToken);
        var encoderInputRun = encoderInputLease.Source;
        var encoderInputReady = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var meter = new MiPlayPcm16SignalMeter();
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
                    var pcm = await pcmBuffer.ReadFrameOrSilenceAsync(encoderInputRun.Token)
                        .ConfigureAwait(false);
                    meter.Add(pcm);
                    await encoder.WritePcmAsync(pcm, encoderInputRun.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (encoderInputRun.IsCancellationRequested)
            {
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
        var beforeTimeOffset = new TaskCompletionSource<ulong>(TaskCreationOptions.RunContinuationsAsynchronously);
        var postOpenContextSent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var rtspReady = new TaskCompletionSource<ulong>(TaskCreationOptions.RunContinuationsAsynchronously);
        var rtspTask = RunRtspControlAsync(
            rtspClient,
            sourceAddress,
            timerUdpPort,
            beforeTimeOffset,
            postOpenContextSent.Task,
            rtspReady,
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

        report(new MiPlayCastDiagnostics(
            MiPlayCastState.AwaitingReceiver,
            SystemLanguage.Select(
                "音频连接已建立，等待播放同步…",
                "Audio connection established. Waiting for playback synchronization…"),
            pcmBuffer.BufferedMilliseconds,
            pcmBuffer.Overruns,
            pcmBuffer.Underruns));
        var pendingTimeOffset = await WaitForStartupPhaseAsync(
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
        var timeOffset = await WaitForStartupPhaseAsync(
                rtspReady.Task,
                TimeSpan.FromSeconds(10),
                "The RTSP channel did not complete post-Open readiness within ten seconds.",
                cancellationToken)
            .ConfigureAwait(false);
        if (timeOffset != pendingTimeOffset)
        {
            throw new InvalidOperationException("The RTSP TIME_OFFSET changed across the control gate.");
        }

        if (request.PairSynchronization is not null)
        {
            await request.PairSynchronization.SynchronizeMediaAsync(cancellationToken).ConfigureAwait(false);
            pcmBuffer.TrimToLatest(1);
            encoderInputReady.TrySetResult();
        }

        var firstMediaSent = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var postOpenControlTask = RunPostOpenControlAsync(
            controlStream,
            postOpen,
            preOpenHeartbeatSentAt.Value,
            firstMediaSent.Task,
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
            run.Token);

        await using var audioStream = audioClient.GetStream();
        var packetizer = new MiPlayWfdAudioPacketizer(
            initialProgramClockReference90Khz: MiPlayWfdMediaClock.CreateInitialProgramClockReference90Khz(
                timeOffset,
                MiPlayProtocolConstants.SystemAudioPlaybackDelayMicroseconds));
        var mediaStartedAt = Stopwatch.GetTimestamp();
        long totalBytes = 0;
        long totalRtpFrames = 0;
        long accessUnitIndex = 0;
        ushort expectedRtpSequence = 0;
        long previousMediaWriteCompletedAt = 0;
        var minimumMediaSendGapMilliseconds = double.PositiveInfinity;
        double maximumMediaSendGapMilliseconds = 0;
        long lateMediaSends = 0;
        long catchUpMediaSends = 0;

        try
        {
            while (true)
            {
                ThrowIfBackgroundFailed(rtspTask, timerTask, postOpenControlTask);
                var accessUnit = await encoder.ReadAccessUnitAsync(cancellationToken).ConfigureAwait(false) ??
                    throw new EndOfStreamException("FFmpeg ended while the MiPlay session was active.");
                var packets = packetizer.PacketizeAccessUnit(accessUnit);
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
                await WriteAsync(audioStream, wireWrite, cancellationToken).ConfigureAwait(false);
                var mediaWriteCompletedAt = Stopwatch.GetTimestamp();
                if (previousMediaWriteCompletedAt != 0)
                {
                    var sendGapMilliseconds =
                        (mediaWriteCompletedAt - previousMediaWriteCompletedAt) * 1_000d /
                        Stopwatch.Frequency;
                    minimumMediaSendGapMilliseconds = Math.Min(
                        minimumMediaSendGapMilliseconds,
                        sendGapMilliseconds);
                    maximumMediaSendGapMilliseconds = Math.Max(
                        maximumMediaSendGapMilliseconds,
                        sendGapMilliseconds);
                }
                previousMediaWriteCompletedAt = mediaWriteCompletedAt;
                if (accessUnitIndex == 0)
                {
                    firstMediaSent.TrySetResult();
                }
                totalBytes += wireWrite.Length;
                totalRtpFrames += packetCount;

                var accessUnitsSent = accessUnitIndex + 1;
                var nextDueMilliseconds = MiPlayWfdStartupPacingPlan.GetDueAfterMilliseconds(accessUnitsSent);
                var nextSend = mediaStartedAt + checked((long)Math.Round(
                    nextDueMilliseconds * Stopwatch.Frequency / 1_000d));
                var remaining = nextSend - Stopwatch.GetTimestamp();
                if (accessUnitsSent >= MiPlayWfdStartupPacingPlan.CapturedAccessUnitCount && remaining <= 0)
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
                    string? protocolEvidence = null;
                    if (accessUnitIndex == 0)
                    {
                        protocolEvidence = MiPlayRuntimeWireEvidence.DescribeFirstMediaBatch(
                            accessUnit,
                            wireWrite,
                            packetCount);
                    }
                    else if (accessUnitIndex % 250 == 0)
                    {
                        var signal = meter.Snapshot();
                        var captureHealth = capture.Health;
                        protocolEvidence =
                            $"pcmSignal route={request.ChannelRoute},capturePackets={captureHealth.PacketCount}," +
                            $"captureLastAudible={captureHealth.LastAudiblePacketAt?.ToString("O") ?? "none"}," +
                            $"routedSamples={signal.SampleCount},routedNonZero={signal.NonZeroSampleCount}," +
                            $"peak={signal.PeakNormalized:F6},rms={signal.RmsNormalized:F6}," +
                            $"rmsDbfs={signal.RmsDecibelsFullScale:F2},audible={signal.ContainsAudibleSignal}";
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
                        accessUnitIndex + 1,
                        totalRtpFrames,
                        totalBytes,
                        ProtocolEvidence: protocolEvidence,
                        MinimumMediaSendGapMilliseconds:
                            double.IsPositiveInfinity(minimumMediaSendGapMilliseconds)
                                ? 0
                                : minimumMediaSendGapMilliseconds,
                        MaximumMediaSendGapMilliseconds: maximumMediaSendGapMilliseconds,
                        LateMediaSends: lateMediaSends,
                        CatchUpMediaSends: catchUpMediaSends));
                    minimumMediaSendGapMilliseconds = double.PositiveInfinity;
                    maximumMediaSendGapMilliseconds = 0;
                    lateMediaSends = 0;
                    catchUpMediaSends = 0;
                }

                accessUnitIndex = accessUnitsSent;
                if (remaining > 0)
                {
                    await Task.Delay(
                        TimeSpan.FromSeconds(remaining / (double)Stopwatch.Frequency),
                        cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            encoderInputRun.Cancel();
            try
            {
                await encoderWriterTask.ConfigureAwait(false);
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

    private async ValueTask UnregisterCaptureAsync(SwitchableAudioCaptureSource capture)
    {
        await captureGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (ReferenceEquals(activeCapture, capture)) activeCapture = null;
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
        SwitchableAudioCaptureSource capture) : IAsyncDisposable
    {
        private int disposed;

        public ValueTask DisposeAsync() =>
            Interlocked.Exchange(ref disposed, 1) == 0
                ? owner.UnregisterCaptureAsync(capture)
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
        Action receiverReady,
        Action<string> reportEvidence,
        CancellationToken cancellationToken)
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
        ActivateVolumeControl(volumeControl);
        Exception? failure = null;
        try
        {
            receiverReady();
            await RunRuntimeControlsAsync(
                controlStream,
                preOpenHeartbeatSentAt,
                volumeControl,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failure = exception;
            throw;
        }
        finally
        {
            DeactivateVolumeControl(
                volumeControl,
                failure ?? new OperationCanceledException("The MiPlay runtime control session ended."));
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
        CancellationToken cancellationToken)
    {
        var sequence = new MiPlayLegacyRuntimeControlSequence();
        var heartbeatDueAt = MiPlayLegacyStreamingHeartbeatPlan.CalculateDueTimestamp(
            preOpenHeartbeatSentAt,
            MiPlayLegacyStreamingHeartbeatPlan.IntervalMilliseconds,
            Stopwatch.Frequency);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Stopwatch.GetTimestamp() >= heartbeatDueAt)
            {
                await ExecuteRuntimeControlAsync(
                    stream,
                    sequence.PrepareHeartbeat(),
                    cancellationToken).ConfigureAwait(false);
                heartbeatDueAt = MiPlayLegacyStreamingHeartbeatPlan.CalculateDueTimestamp(
                    heartbeatDueAt,
                    MiPlayLegacyStreamingHeartbeatPlan.IntervalMilliseconds,
                    Stopwatch.Frequency);
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
                    await ExecuteRuntimeControlAsync(
                        stream,
                        sequence.PrepareSetVolume(request.Volume),
                        cancellationToken).ConfigureAwait(false);
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
            var heartbeatDelay = Task.Delay(
                TimeSpan.FromSeconds(remaining / (double)Stopwatch.Frequency),
                wait.Token);
            var completed = await Task.WhenAny(volumeReady, heartbeatDelay).ConfigureAwait(false);
            wait.Cancel();
            if (ReferenceEquals(completed, volumeReady))
            {
                if (!await volumeReady.ConfigureAwait(false))
                {
                    throw new InvalidOperationException("The MiPlay volume command queue closed unexpectedly.");
                }
            }
            else
            {
                try
                {
                    await volumeReady.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (
                    wait.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                }
            }
        }
    }

    private static async Task ExecuteRuntimeControlAsync(
        NetworkStream stream,
        MiPlayLegacyRuntimeControlCommand command,
        CancellationToken cancellationToken)
    {
        await WriteAsync(stream, command.CommandFrame, cancellationToken).ConfigureAwait(false);
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
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var received = await timer.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            if (!received.RemoteEndPoint.Address.Equals(receiver) ||
                received.Buffer.Length != MiPlayWfdTimerPacketCodec.PacketLength)
            {
                throw new InvalidOperationException("The MiPlay timer received an unexpected peer or packet.");
            }
            var receiveTime = GetMonotonicMicroseconds();
            var response = MiPlayWfdTimerPacketCodec.CreateResponse(
                received.Buffer,
                receiveTime,
                GetMonotonicMicroseconds());
            await timer.SendAsync(response, received.RemoteEndPoint, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task RunRtspControlAsync(
        TcpClient client,
        IPAddress sourceAddress,
        int timerPort,
        TaskCompletionSource<ulong> beforeTimeOffset,
        Task postOpenContextSent,
        TaskCompletionSource<ulong> ready,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = client.GetStream();
            var sessionId = RandomNumberGenerator.GetInt32(100_000_000, 1_000_000_000).ToString();
            var session = new MiPlayWfdSourceRtspSession(sourceAddress, timerPort, sessionId);
            await SendRtspMessagesAsync(
                stream,
                session.Start(DateTimeOffset.UtcNow).OutboundMessages,
                cancellationToken).ConfigureAwait(false);

            var pending = new byte[64 * 1024];
            var pendingCount = 0;
            var readBuffer = new byte[8 * 1024];
            while (!cancellationToken.IsCancellationRequested)
            {
                var read = await stream.ReadAsync(readBuffer, cancellationToken).ConfigureAwait(false);
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

                while (MiPlayRtspWireMessageCodec.TryDecode(
                           pending.AsSpan(0, pendingCount),
                           out _,
                           out var consumed))
                {
                    var monotonicMicroseconds = GetMonotonicMicroseconds();
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
                        if (session.TimeOffsetMicroseconds is not ulong pendingTimeOffset)
                        {
                            throw new InvalidOperationException("RTSP PLAY did not establish TIME_OFFSET.");
                        }
                        beforeTimeOffset.TrySetResult(pendingTimeOffset);
                        await postOpenContextSent.WaitAsync(cancellationToken).ConfigureAwait(false);
                        await SendRtspMessagesAsync(
                            stream,
                            [transition.OutboundMessages[1]],
                            cancellationToken).ConfigureAwait(false);
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
                }
            }
        }
        catch (Exception exception)
        {
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
