using DLNACast.Core.MiPlay;
using DLNACast.Core.Localization;
using DLNACast.Core.Models;
using System.Net.Sockets;

namespace DLNACast.Tests;

public sealed class MiPlaySystemAudioTransmitterTests
{
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

    private sealed class BlockingRunner : IMiPlaySystemAudioSessionRunner, IMiPlayAudioCaptureController
    {
        private Action? ready;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool CancellationObserved { get; private set; }
        public MiPlaySystemAudioRequest? Request { get; private set; }
        public CaptureSelection? LiveSelection { get; private set; }

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
