using DLNACast.Core.MiPlay;
using DLNACast.Core.Localization;
using DLNACast.Core.Models;
using System.Net.Sockets;

namespace DLNACast.Tests;

public sealed class MiPlaySystemAudioTransmitterTests
{
    [Fact]
    public void SharedBroadcastErrorIncludesTheUnderlyingFailure()
    {
        var cause = new TimeoutException("MiPlay command 0x001A sequence 0x0022 was not acknowledged.");
        var wrapped = new InvalidOperationException("Shared MiPlay access-unit broadcasting failed.",
            new IOException("route stopped", cause));

        var description = MiPlaySystemAudioTransmitter.DescribeFailure(wrapped);
        Assert.Contains(wrapped.Message, description);
        Assert.Contains(cause.Message, description);
        Assert.Equal(cause.Message, MiPlaySystemAudioTransmitter.DescribeFailure(cause));
    }

    [Fact]
    public void RuntimeTransportFailuresRejoinButEquivalentStartupFailuresDoNot()
    {
        var heartbeat = new TimeoutException(
            "MiPlay received no heartbeat activity for more than fifteen seconds.");
        var disconnected = new IOException(
            "The receiver closed the runtime command connection.");
        var hostAbort = new IOException(
            "Unable to write data to the transport connection.",
            new SocketException((int)SocketError.ConnectionAborted));

        Assert.Equal(
            MiPlayCastFailureKind.TransportStall,
            MiPlaySystemAudioTransmitter.ClassifyFailure(heartbeat, wasStreaming: true));
        Assert.Equal(
            MiPlayCastFailureKind.TransportStall,
            MiPlaySystemAudioTransmitter.ClassifyFailure(disconnected, wasStreaming: true));
        Assert.Equal(
            MiPlayCastFailureKind.TransportStall,
            MiPlaySystemAudioTransmitter.ClassifyFailure(hostAbort, wasStreaming: true));
        Assert.Equal(
            MiPlayCastFailureKind.None,
            MiPlaySystemAudioTransmitter.ClassifyFailure(heartbeat, wasStreaming: false));
        Assert.Equal(
            MiPlayCastFailureKind.ReceiverBusy,
            MiPlaySystemAudioTransmitter.ClassifyFailure(hostAbort, wasStreaming: false));
    }

    [Fact]
    public async Task StartWaitsForReceiverReadyAndStopCancelsTheOwnedSession()
    {
        var runner = new BlockingRunner();
        await using var transmitter = new MiPlaySystemAudioTransmitter(runner);
        var states = new List<MiPlayCastState>();
        transmitter.DiagnosticsChanged += (_, diagnostics) => states.Add(diagnostics.State);
        var request = MiPlayCastContractsTests.CreateRequest(
            new CaptureSelection.SystemMix("default", "Default output"));

        var start = transmitter.StartAsync(request);
        await runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(start.IsCompleted);
        runner.MarkReady();
        await start.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(transmitter.IsActive);
        Assert.Equal(MiPlayCastState.Streaming, transmitter.Diagnostics.State);
        Assert.Equal(SystemLanguage.Select(
            "MiPlay 音频正在投送",
            "MiPlay audio is casting"), transmitter.Diagnostics.Message);
        await transmitter.StopAsync().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(transmitter.IsActive);
        Assert.Equal(MiPlayCastState.Idle, transmitter.Diagnostics.State);
        Assert.Contains(MiPlayCastState.Stopping, states);
        Assert.True(runner.CancellationObserved);
    }

    [Fact]
    public async Task BeginStartReturnsBeforeReceiverReadySoTheUiCanOfferStop()
    {
        var runner = new BlockingRunner();
        await using var transmitter = new MiPlaySystemAudioTransmitter(runner);
        var request = MiPlayCastContractsTests.CreateRequest(
            new CaptureSelection.SystemMix("default", "Default output"));

        await transmitter.BeginStartAsync(request).WaitAsync(TimeSpan.FromSeconds(2));
        await runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(transmitter.IsActive);
        Assert.Equal(MiPlayCastState.Connecting, transmitter.Diagnostics.State);
        Assert.Equal(SystemLanguage.Select(
            "正在连接 MiPlay 音箱…",
            "Connecting to the MiPlay speaker…"), transmitter.Diagnostics.Message);
        await transmitter.StopAsync().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(transmitter.IsActive);
        Assert.True(runner.CancellationObserved);
    }

    [Fact]
    public async Task ForwardsProcessCaptureSelectionToTheSessionRunner()
    {
        var runner = new BlockingRunner();
        await using var transmitter = new MiPlaySystemAudioTransmitter(runner);
        var selection = new CaptureSelection.Process(42, "Player", true);
        var request = MiPlayCastContractsTests.CreateRequest(selection);

        await transmitter.BeginStartAsync(request).WaitAsync(TimeSpan.FromSeconds(2));
        await runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Same(selection, runner.Request!.Selection);
        await transmitter.StopAsync().WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task ForwardsLiveCaptureSelectionWhileTheSessionRemainsActive()
    {
        var runner = new BlockingRunner();
        await using var transmitter = new MiPlaySystemAudioTransmitter(runner);
        var request = MiPlayCastContractsTests.CreateRequest(
            new CaptureSelection.SystemMix("physical", "Physical output"));
        var replacement = new CaptureSelection.SystemMix("virtual", "Virtual output");
        await transmitter.BeginStartAsync(request);
        await runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await transmitter.SetCaptureSelectionAsync(replacement);

        Assert.Equal(replacement, runner.LiveSelection);
        Assert.True(transmitter.IsActive);
        await transmitter.StopAsync().WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task SurfacesRunnerFailureWithoutLeavingAnActiveSession()
    {
        await using var transmitter = new MiPlaySystemAudioTransmitter(
            new FailingRunner(new IOException("receiver failed")));
        var request = MiPlayCastContractsTests.CreateRequest(
            new CaptureSelection.SystemMix("default", "Default output"));

        var exception = await Assert.ThrowsAsync<IOException>(() => transmitter.StartAsync(request));

        Assert.Equal("receiver failed", exception.Message);
        Assert.False(transmitter.IsActive);
        Assert.Equal(MiPlayCastState.Error, transmitter.Diagnostics.State);
        Assert.Equal(SystemLanguage.Select(
            "MiPlay 会话失败",
            "MiPlay session failed"), transmitter.Diagnostics.Message);
        Assert.Equal("receiver failed", transmitter.Diagnostics.LastError);
    }

    [Fact]
    public async Task MapsHostAbortedTransportWriteToReceiverBusy()
    {
        var failure = new IOException(
            "Unable to write data to the transport connection.",
            new SocketException((int)SocketError.ConnectionAborted));
        await using var transmitter = new MiPlaySystemAudioTransmitter(new FailingRunner(failure));
        var request = MiPlayCastContractsTests.CreateRequest(
            new CaptureSelection.SystemMix("default", "Default output"));

        await Assert.ThrowsAsync<IOException>(() => transmitter.StartAsync(request));

        Assert.False(transmitter.IsActive);
        Assert.Equal(MiPlayCastState.Error, transmitter.Diagnostics.State);
        Assert.Equal(MiPlayCastFailureKind.ReceiverBusy, transmitter.Diagnostics.FailureKind);
        Assert.Equal(
            SystemLanguage.Select("音箱被其他设备占用", "The speaker is in use by another device."),
            transmitter.Diagnostics.LastError);
    }

    [Fact]
    public async Task StopAfterNaturalCompletionLeavesTheTransmitterIdle()
    {
        await using var transmitter = new MiPlaySystemAudioTransmitter(new CompletingRunner());
        var request = MiPlayCastContractsTests.CreateRequest(
            new CaptureSelection.SystemMix("default", "Default output"));

        await transmitter.StartAsync(request).WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(10);
        await transmitter.StopAsync();

        Assert.False(transmitter.IsActive);
        Assert.Equal(MiPlayCastState.Idle, transmitter.Diagnostics.State);
    }

    [Fact]
    public async Task SerializesReceiverVolumeThroughTheActiveRunnerAndForwardsUpdates()
    {
        var runner = new VolumeRunner();
        await using var transmitter = new MiPlaySystemAudioTransmitter(runner);
        var observed = new List<int>();
        transmitter.ReceiverVolumeChanged += (_, args) => observed.Add(args.Volume);
        var request = MiPlayCastContractsTests.CreateRequest(
            new CaptureSelection.SystemMix("default", "Default output"));

        await transmitter.BeginStartAsync(request);
        await runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        runner.MarkReady();
        await runner.ReadyReported.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await transmitter.SetReceiverVolumeAsync(37);

        Assert.Equal(37, runner.LastRequestedVolume);
        Assert.Equal(37, transmitter.ReceiverVolume);
        Assert.Equal([37], observed);
        await transmitter.StopAsync();
    }

    [Fact]
    public async Task RejectsReceiverVolumeOutsideStreamingOrRange()
    {
        var runner = new VolumeRunner();
        await using var transmitter = new MiPlaySystemAudioTransmitter(runner);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            transmitter.SetReceiverVolumeAsync(20));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            transmitter.SetReceiverVolumeAsync(101));
    }

    [Fact]
    public async Task ForwardsLatencyProbeWithoutRestartingTheStreamingSession()
    {
        var runner = new BlockingRunner();
        await using var transmitter = new MiPlaySystemAudioTransmitter(runner);
        var request = MiPlayCastContractsTests.CreateRequest(
            new CaptureSelection.SystemMix("default", "Default output"));
        await transmitter.BeginStartAsync(request);
        await runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        runner.MarkReady();

        await transmitter.PrepareLatencyProbeAsync();
        var startedAt = await transmitter.StartLatencyProbeAsync();

        Assert.Equal(BlockingRunner.ProbeTimestamp, startedAt);
        Assert.True(transmitter.IsActive);
        Assert.Equal(1, runner.LatencyProbePreparations);
        Assert.Equal(1, runner.LatencyProbeRequests);
        await transmitter.StopAsync();
    }

    [Fact]
    public async Task ExposesPauseResumeAndGracefulCloseOnTheActiveControlPlane()
    {
        var runner = new PlaybackRunner();
        await using var transmitter = new MiPlaySystemAudioTransmitter(runner);
        var request = MiPlayCastContractsTests.CreateRequest(
            new CaptureSelection.SystemMix("default", "Default output"));
        await transmitter.StartAsync(request).WaitAsync(TimeSpan.FromSeconds(2));

        await transmitter.PauseAsync();
        Assert.True(transmitter.ReceiverPaused);
        Assert.Equal(1, runner.PauseCount);

        await transmitter.ResumeAsync();
        Assert.False(transmitter.ReceiverPaused);
        Assert.Equal(1, runner.ResumeCount);

        await transmitter.StopAsync().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, runner.CloseCount);
        Assert.True(runner.CancellationObserved);
    }

    [Fact]
    public async Task ReceiverOnlyPlaybackControlDoesNotUseTheCapturePausingPath()
    {
        var runner = new PlaybackRunner();
        await using var transmitter = new MiPlaySystemAudioTransmitter(runner);
        var request = MiPlayCastContractsTests.CreateRequest(
            new CaptureSelection.SystemMix("default", "Default output"));
        await transmitter.StartAsync(request).WaitAsync(TimeSpan.FromSeconds(2));

        await transmitter.PauseReceiverAsync();
        Assert.True(transmitter.ReceiverPaused);
        Assert.Equal(1, runner.ReceiverOnlyPauseCount);
        Assert.Equal(0, runner.PauseCount);

        await transmitter.ResumeReceiverAsync();
        Assert.False(transmitter.ReceiverPaused);
        Assert.Equal(1, runner.ReceiverOnlyResumeCount);
        Assert.Equal(0, runner.ResumeCount);

        await transmitter.StopAsync().WaitAsync(TimeSpan.FromSeconds(2));
    }

    private sealed class BlockingRunner :
        IMiPlaySystemAudioSessionRunner,
        IMiPlayAudioCaptureController,
        IMiPlayLatencyProbeController
    {
        public const long ProbeTimestamp = 123_456;
        private Action? ready;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool CancellationObserved { get; private set; }
        public MiPlaySystemAudioRequest? Request { get; private set; }
        public CaptureSelection? LiveSelection { get; private set; }
        public int LatencyProbeRequests { get; private set; }
        public int LatencyProbePreparations { get; private set; }

        public async Task RunAsync(
            MiPlaySystemAudioRequest request,
            Action receiverReady,
            Action<MiPlayCastDiagnostics> report,
            CancellationToken cancellationToken)
        {
            Request = request;
            ready = receiverReady;
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                CancellationObserved = true;
                throw;
            }
        }

        public void MarkReady() => ready!();

        public Task SetCaptureSelectionAsync(
            CaptureSelection selection,
            CancellationToken cancellationToken = default)
        {
            LiveSelection = selection;
            return Task.CompletedTask;
        }

        public Task<long> StartLatencyProbeAsync(CancellationToken cancellationToken = default)
        {
            LatencyProbeRequests++;
            return Task.FromResult(ProbeTimestamp);
        }

        public Task PrepareLatencyProbeAsync(CancellationToken cancellationToken = default)
        {
            LatencyProbePreparations++;
            return Task.CompletedTask;
        }
    }

    private sealed class VolumeRunner : IMiPlaySystemAudioSessionRunner, IMiPlayReceiverVolumeController
    {
        private Action? ready;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReadyReported { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int? ReceiverVolume { get; private set; }
        public int? LastRequestedVolume { get; private set; }
        public event EventHandler<MiPlayReceiverVolumeChangedEventArgs>? ReceiverVolumeChanged;

        public async Task RunAsync(
            MiPlaySystemAudioRequest request,
            Action receiverReady,
            Action<MiPlayCastDiagnostics> report,
            CancellationToken cancellationToken)
        {
            ready = () =>
            {
                receiverReady();
                ReadyReported.TrySetResult();
            };
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        public Task SetReceiverVolumeAsync(int volume, CancellationToken cancellationToken = default)
        {
            LastRequestedVolume = volume;
            ReceiverVolume = volume;
            ReceiverVolumeChanged?.Invoke(this, new(volume));
            return Task.CompletedTask;
        }

        public void MarkReady() => ready!();
    }

    private sealed class PlaybackRunner : IMiPlaySystemAudioSessionRunner, IMiPlayPlaybackController
    {
        public bool ReceiverPaused { get; private set; }
        public int PauseCount { get; private set; }
        public int ResumeCount { get; private set; }
        public int ReceiverOnlyPauseCount { get; private set; }
        public int ReceiverOnlyResumeCount { get; private set; }
        public int CloseCount { get; private set; }
        public bool CancellationObserved { get; private set; }

        public async Task RunAsync(
            MiPlaySystemAudioRequest request,
            Action receiverReady,
            Action<MiPlayCastDiagnostics> report,
            CancellationToken cancellationToken)
        {
            receiverReady();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                CancellationObserved = true;
                throw;
            }
        }

        public Task PauseAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PauseCount++;
            ReceiverPaused = true;
            return Task.CompletedTask;
        }

        public Task ResumeAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ResumeCount++;
            ReceiverPaused = false;
            return Task.CompletedTask;
        }

        public Task PauseReceiverAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReceiverOnlyPauseCount++;
            ReceiverPaused = true;
            return Task.CompletedTask;
        }

        public Task ResumeReceiverAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReceiverOnlyResumeCount++;
            ReceiverPaused = false;
            return Task.CompletedTask;
        }

        public Task CloseAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CloseCount++;
            ReceiverPaused = false;
            return Task.CompletedTask;
        }
    }

    private sealed class FailingRunner(Exception exception) : IMiPlaySystemAudioSessionRunner
    {
        public Task RunAsync(
            MiPlaySystemAudioRequest request,
            Action receiverReady,
            Action<MiPlayCastDiagnostics> report,
            CancellationToken cancellationToken) => Task.FromException(exception);
    }

    private sealed class CompletingRunner : IMiPlaySystemAudioSessionRunner
    {
        public Task RunAsync(
            MiPlaySystemAudioRequest request,
            Action receiverReady,
            Action<MiPlayCastDiagnostics> report,
            CancellationToken cancellationToken)
        {
            receiverReady();
            return Task.CompletedTask;
        }
    }
}
