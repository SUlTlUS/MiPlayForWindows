using DLNACast.Core.Localization;
using DLNACast.Core.Models;
using System.Net.Sockets;

namespace DLNACast.Core.MiPlay;

public sealed class MiPlaySystemAudioTransmitter : IAsyncDisposable
{
    private readonly IMiPlaySystemAudioSessionRunner runner;
    private readonly SemaphoreSlim gate = new(1, 1);
    private CancellationTokenSource? lifetime;
    private Task? sessionTask;
    private TaskCompletionSource? ready;
    private MiPlayCastDiagnostics diagnostics = new(
        MiPlayCastState.Idle,
        SystemLanguage.Select("空闲", "Idle"));

    public MiPlaySystemAudioTransmitter(IMiPlaySystemAudioSessionRunner runner)
    {
        this.runner = runner ?? throw new ArgumentNullException(nameof(runner));
        if (runner is IMiPlayReceiverVolumeController volumeController)
        {
            volumeController.ReceiverVolumeChanged += OnReceiverVolumeChanged;
        }
    }

    public MiPlayCastDiagnostics Diagnostics => diagnostics;
    public bool IsActive => diagnostics.State is not (MiPlayCastState.Idle or MiPlayCastState.Error);
    public int? ReceiverVolume =>
        (runner as IMiPlayReceiverVolumeController)?.ReceiverVolume;
    public event EventHandler<MiPlayCastDiagnostics>? DiagnosticsChanged;
    public event EventHandler<MiPlayReceiverVolumeChangedEventArgs>? ReceiverVolumeChanged;

    public async Task StartAsync(
        MiPlaySystemAudioRequest request,
        CancellationToken cancellationToken = default)
    {
        await BeginStartAsync(request, cancellationToken).ConfigureAwait(false);
        await ready!.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task BeginStartAsync(
        MiPlaySystemAudioRequest request,
        CancellationToken cancellationToken = default)
    {
        request.Validate();
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsActive || sessionTask is { IsCompleted: false })
            {
                throw new InvalidOperationException(SystemLanguage.Select(
                    "已有 MiPlay 投送会话正在运行。",
                    "A MiPlay cast session is already active."));
            }

            lifetime?.Dispose();
            lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Update(new MiPlayCastDiagnostics(
                MiPlayCastState.Connecting,
                SystemLanguage.Select("正在连接 MiPlay 音箱…", "Connecting to the MiPlay speaker…")));
            sessionTask = RunManagedAsync(request, lifetime.Token);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task StopAsync()
    {
        Task? activeTask;
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (sessionTask is null)
            {
                Update(new MiPlayCastDiagnostics(MiPlayCastState.Idle, SystemLanguage.Select("空闲", "Idle")));
                return;
            }

            if (sessionTask.IsCompleted)
            {
                sessionTask = null;
                lifetime?.Dispose();
                lifetime = null;
                Update(new MiPlayCastDiagnostics(MiPlayCastState.Idle, SystemLanguage.Select("空闲", "Idle")));
                return;
            }

            Update(diagnostics with
            {
                State = MiPlayCastState.Stopping,
                Message = SystemLanguage.Select("正在停止 MiPlay…", "Stopping MiPlay…")
            });
            lifetime?.Cancel();
            activeTask = sessionTask;
        }
        finally
        {
            gate.Release();
        }

        try
        {
            await activeTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    public async Task SetReceiverVolumeAsync(
        int volume,
        CancellationToken cancellationToken = default)
    {
        if (volume is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(volume));
        }

        IMiPlayReceiverVolumeController volumeController;
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (diagnostics.State != MiPlayCastState.Streaming)
            {
                throw new InvalidOperationException(
                    SystemLanguage.Select(
                        "只能在 MiPlay 正在投送时调节音箱音量。",
                        "Speaker volume can be changed only while MiPlay is casting."));
            }
            volumeController = runner as IMiPlayReceiverVolumeController ??
                throw new NotSupportedException(SystemLanguage.Select(
                    "当前 MiPlay 会话不支持调节音箱音量。",
                    "The current MiPlay session does not support speaker volume control."));
        }
        finally
        {
            gate.Release();
        }

        await volumeController.SetReceiverVolumeAsync(volume, cancellationToken).ConfigureAwait(false);
    }

    public async Task SetCaptureSelectionAsync(
        CaptureSelection selection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selection);
        IMiPlayAudioCaptureController captureController;
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!IsActive)
            {
                throw new InvalidOperationException(SystemLanguage.Select(
                    "只能在 MiPlay 投送期间切换音频捕获。",
                    "Audio capture can be changed only while MiPlay is casting."));
            }
            captureController = runner as IMiPlayAudioCaptureController ??
                throw new NotSupportedException(SystemLanguage.Select(
                    "当前 MiPlay 会话不支持热切换音频捕获。",
                    "The current MiPlay session does not support live audio-capture switching."));
        }
        finally
        {
            gate.Release();
        }

        await captureController.SetCaptureSelectionAsync(selection, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task RunManagedAsync(
        MiPlaySystemAudioRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            await runner.RunAsync(
                request,
                () =>
                {
                    Update(diagnostics with
                    {
                        State = MiPlayCastState.Streaming,
                        Message = SystemLanguage.Select(
                            "MiPlay 音频正在投送",
                            "MiPlay audio is casting"),
                    });
                    ready?.TrySetResult();
                },
                Update,
                cancellationToken).ConfigureAwait(false);
            ready?.TrySetResult();
            Update(new MiPlayCastDiagnostics(
                MiPlayCastState.Idle,
                SystemLanguage.Select("MiPlay 会话已结束", "The MiPlay session has ended")));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            ready?.TrySetCanceled(cancellationToken);
            Update(new MiPlayCastDiagnostics(
                MiPlayCastState.Idle,
                SystemLanguage.Select("MiPlay 已停止", "MiPlay stopped")));
        }
        catch (Exception exception)
        {
            var failureKind = ClassifyFailure(exception);
            ready?.TrySetException(exception);
            Update(diagnostics with
            {
                State = MiPlayCastState.Error,
                Message = SystemLanguage.Select("MiPlay 会话失败", "MiPlay session failed"),
                LastError = failureKind == MiPlayCastFailureKind.ReceiverBusy
                    ? SystemLanguage.Select("音箱被其他设备占用", "The speaker is in use by another device.")
                    : exception.Message,
                FailureKind = failureKind,
            });
        }
    }

    private static MiPlayCastFailureKind ClassifyFailure(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is SocketException { SocketErrorCode: SocketError.ConnectionAborted })
            {
                return MiPlayCastFailureKind.ReceiverBusy;
            }
        }

        var details = exception.ToString();
        var isTransportWriteFailure = details.Contains(
            "Unable to write data to the transport connection",
            StringComparison.OrdinalIgnoreCase);
        var isHostAbort = details.Contains(
                              "An established connection was aborted by the software in your host machine",
                              StringComparison.OrdinalIgnoreCase) ||
                          details.Contains("你的主机中的软件中止了一个已建立的连接", StringComparison.Ordinal);
        return isTransportWriteFailure && isHostAbort
            ? MiPlayCastFailureKind.ReceiverBusy
            : MiPlayCastFailureKind.None;
    }

    private void Update(MiPlayCastDiagnostics value)
    {
        diagnostics = value;
        DiagnosticsChanged?.Invoke(this, value);
    }

    private void OnReceiverVolumeChanged(
        object? sender,
        MiPlayReceiverVolumeChangedEventArgs eventArgs) =>
        ReceiverVolumeChanged?.Invoke(this, eventArgs);

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        if (runner is IMiPlayReceiverVolumeController volumeController)
        {
            volumeController.ReceiverVolumeChanged -= OnReceiverVolumeChanged;
        }
        lifetime?.Dispose();
        gate.Dispose();
    }
}
