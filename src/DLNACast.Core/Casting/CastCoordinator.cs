using DLNACast.Core.Abstractions;
using DLNACast.Core.Audio;
using DLNACast.Core.Dlna;
using DLNACast.Core.Localization;
using DLNACast.Core.Models;
using DLNACast.Core.Streaming;

namespace DLNACast.Core.Casting;

public sealed class CastCoordinator : IAsyncDisposable
{
    private readonly IAudioSourceCatalog _audioSources;
    private readonly IRendererController _controller;
    private readonly ILiveStreamServer _streamServer;
    private readonly ILocalOutputManager _localOutputs;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _lifetime;
    private SwitchableAudioCaptureSource? _capture;
    private ILocalOutputLease? _localOutputLease;
    private PcmFrameBuffer? _frames;
    private LiveStreamSession? _streamSession;
    private RendererDevice? _renderer;
    private Task? _monitorTask;
    private StreamProfile? _profile;
    private CaptureSelection? _selection;
    private bool _muteLocalOutput;
    private CastDiagnostics _diagnostics = new(
        CastSessionState.Idle,
        Message: SystemLanguage.Select("空闲", "Idle"));

    public CastCoordinator(
        IAudioSourceCatalog audioSources,
        IRendererController controller,
        ILiveStreamServer streamServer,
        ILocalOutputManager localOutputs)
    {
        _audioSources = audioSources;
        _controller = controller;
        _streamServer = streamServer;
        _localOutputs = localOutputs;
    }

    public CastDiagnostics Diagnostics => _diagnostics;
    public bool IsCasting => _diagnostics.State is CastSessionState.Preparing
        or CastSessionState.Connecting
        or CastSessionState.Streaming
        or CastSessionState.Recovering;
    public event EventHandler<CastDiagnostics>? DiagnosticsChanged;

    public async Task StartAsync(
        RendererDevice renderer,
        CaptureSelection selection,
        bool allowMp3Fallback,
        bool muteLocalOutput,
        CancellationToken cancellationToken)
    {
        await StartAsync(
            renderer,
            selection,
            allowMp3Fallback,
            muteLocalOutput,
            AudioChannelRoute.Stereo,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task StartAsync(
        RendererDevice renderer,
        CaptureSelection selection,
        bool allowMp3Fallback,
        bool muteLocalOutput,
        AudioChannelRoute channelRoute,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var ownsNewSession = false;
        try
        {
            if (IsCasting)
            {
                throw new InvalidOperationException(SystemLanguage.Select(
                    "已有投送会话正在运行。",
                    "A cast session is already active."));
            }

            ownsNewSession = true;
            _muteLocalOutput = muteLocalOutput;
            _selection = selection;
            _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _renderer = renderer;
            var captureSelection = selection;
            if (muteLocalOutput)
            {
                _localOutputLease = await _localOutputs
                    .RouteForCastAsync(selection, _lifetime.Token)
                    .ConfigureAwait(false);
                captureSelection = _localOutputLease.CaptureSelection;
                Update(CastSessionState.Preparing, null, SystemLanguage.Select(
                    "音频已切换到 DLNA Cast 虚拟扬声器，正在启动捕获…",
                    "Audio is routed to the DLNA Cast virtual speaker. Starting capture…"));
            }

            _frames = new PcmFrameBuffer(channelRoute);
            _capture = new SwitchableAudioCaptureSource(_audioSources, captureSelection);
            _capture.CaptureFailed += OnCaptureFailed;
            Update(CastSessionState.Preparing, null, SystemLanguage.Select(
                "正在启动音频捕获…",
                "Starting audio capture…"));
            await _capture.StartAsync(_frames, _lifetime.Token).ConfigureAwait(false);
            if (!muteLocalOutput)
            {
                Update(CastSessionState.Preparing, null, SystemLanguage.Select(
                    "本机声音保持播放，正在准备直播…",
                    "PC audio remains on. Preparing the live stream…"));
            }
            await Task.Delay(PcmFrameBuffer.FrameMilliseconds, _lifetime.Token).ConfigureAwait(false);

            var profiles = ProtocolInfoMatcher.SelectProfiles(renderer.SinkProtocolInfo, allowMp3Fallback);
            Exception? lastFailure = null;
            foreach (var profile in profiles)
            {
                try
                {
                    await StartProfileAsync(profile, _lifetime.Token).ConfigureAwait(false);
                    _monitorTask = MonitorAsync(_lifetime.Token);
                    return;
                }
                catch (Exception ex) when (profile == StreamProfile.PcmWave && allowMp3Fallback &&
                                           ex is UpnpException or IOException or TimeoutException or HttpRequestException)
                {
                    lastFailure = ex;
                    await DisposeStreamSessionAsync().ConfigureAwait(false);
                    Update(CastSessionState.Recovering, StreamProfile.Mp3Cbr320, SystemLanguage.Select(
                        "PCM 未建立播放，正在回退到 MP3…",
                        "PCM playback did not start. Falling back to MP3…"), ex.Message);
                }
            }

            throw lastFailure ?? new InvalidOperationException(SystemLanguage.Select(
                "音箱没有建立直播连接。",
                "The speaker did not establish the live-stream connection."));
        }
        catch (Exception ex) when (ownsNewSession)
        {
            Update(CastSessionState.Error, _profile, SystemLanguage.Select(
                "投送启动失败",
                "Failed to start casting"), ex.Message);
            await CleanupCoreAsync(sendStop: true).ConfigureAwait(false);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task StartProfileAsync(StreamProfile profile, CancellationToken cancellationToken)
    {
        _profile = profile;
        Update(CastSessionState.Connecting, profile, profile == StreamProfile.PcmWave
            ? SystemLanguage.Select("正在连接 PCM/WAV 直播…", "Connecting the PCM/WAV live stream…")
            : SystemLanguage.Select("正在连接 320 kbps MP3 直播…", "Connecting the 320 kbps MP3 live stream…"));
        _streamSession = await _streamServer.StartSessionAsync(
            _renderer!, _frames!, profile, cancellationToken).ConfigureAwait(false);
        await _controller.SetTransportUriAsync(_renderer!, _streamSession.StreamUri, profile, cancellationToken)
            .ConfigureAwait(false);
        await _controller.PlayAsync(_renderer!, cancellationToken).ConfigureAwait(false);

        var connected = await _streamSession.WaitForClientAsync(TimeSpan.FromSeconds(5), cancellationToken)
            .ConfigureAwait(false);
        if (!connected)
        {
            throw new TimeoutException(SystemLanguage.Select(
                "音箱在 5 秒内没有请求直播 URL。",
                "The speaker did not request the live-stream URL within 5 seconds."));
        }

        var localOutput = _muteLocalOutput
            ? SystemLanguage.Select("本机已静音", "PC muted")
            : SystemLanguage.Select("本机保持播放", "PC audio on");
        Update(CastSessionState.Streaming, profile, profile == StreamProfile.PcmWave
            ? SystemLanguage.Select(
                $"正在以低延迟 PCM/WAV 投送（{localOutput}）",
                $"Casting low-latency PCM/WAV ({localOutput})")
            : SystemLanguage.Select(
                $"正在以兼容 MP3 投送（{localOutput}）",
                $"Casting compatible MP3 ({localOutput})"));
    }

    private async Task MonitorAsync(CancellationToken cancellationToken)
    {
        var consecutiveFailures = 0;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
                try
                {
                    var status = await _controller.GetTransportStatusAsync(_renderer!, cancellationToken).ConfigureAwait(false);
                    consecutiveFailures = 0;
                    if (string.Equals(status.State, "STOPPED", StringComparison.OrdinalIgnoreCase))
                    {
                        await RecoverAsync(SystemLanguage.Select(
                            "音箱意外停止播放",
                            "The speaker stopped playback unexpectedly"), cancellationToken).ConfigureAwait(false);
                    }
                    else if (_streamSession is { EarlyDisconnects: >= 2 } && _profile == StreamProfile.PcmWave)
                    {
                        Update(CastSessionState.Recovering, _profile, SystemLanguage.Select(
                            "PCM 连接连续中断，正在切换 MP3…",
                            "The PCM connection keeps dropping. Switching to MP3…"));
                        await DisposeStreamSessionAsync().ConfigureAwait(false);
                        await StartProfileAsync(StreamProfile.Mp3Cbr320, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        RefreshBufferDiagnostics();
                    }
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException or UpnpException or TimeoutException)
                {
                    consecutiveFailures++;
                    if (consecutiveFailures >= 3)
                    {
                        await RecoverAsync(SystemLanguage.Select(
                            "与音箱的连接中断",
                            "The speaker connection was interrupted"), cancellationToken).ConfigureAwait(false);
                        consecutiveFailures = 0;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Update(CastSessionState.Error, _profile, SystemLanguage.Select(
                "投送已中断",
                "Casting was interrupted"), ex.Message);
            _ = StopAsync();
        }
    }

    private async Task RecoverAsync(string reason, CancellationToken cancellationToken)
    {
        Update(CastSessionState.Recovering, _profile, reason);
        Exception? lastFailure = null;
        var delays = new[] { 500, 1_000, 2_000 };
        foreach (var delay in delays)
        {
            try
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                await DisposeStreamSessionAsync().ConfigureAwait(false);
                await StartProfileAsync(_profile ?? StreamProfile.PcmWave, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or UpnpException or TimeoutException)
            {
                lastFailure = ex;
            }
        }

        throw new InvalidOperationException(SystemLanguage.Select(
            "三次重连均失败。",
            "All three reconnection attempts failed."), lastFailure);
    }

    private void RefreshBufferDiagnostics()
    {
        if (_frames is null)
        {
            return;
        }

        var message = _diagnostics.Message;
        if (_capture?.Health.IsContinuouslySilent(TimeSpan.FromSeconds(5)) == true)
        {
            message = SystemLanguage.Select(
                "正在投送，但捕获已持续静音；内容可能暂停，或受 DRM 保护而无法捕获",
                "Casting is active, but capture has remained silent. Playback may be paused or protected by DRM.");
        }
        Update(CastSessionState.Streaming, _profile, message, _diagnostics.LastError);
    }

    private void OnCaptureFailed(object? sender, Exception exception)
    {
        Update(CastSessionState.Error, _profile, SystemLanguage.Select(
            "音频捕获已停止",
            "Audio capture stopped"), exception.Message);
        _ = StopAsync();
    }

    public async Task SetMuteLocalOutputAsync(
        bool muteLocalOutput,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_muteLocalOutput == muteLocalOutput) return;
            if (!IsCasting || _lifetime is null || _capture is null || _selection is null)
            {
                throw new InvalidOperationException(SystemLanguage.Select(
                    "只能在投送期间切换仅音箱播放。",
                    "Speaker-only playback can be changed only while casting."));
            }

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _lifetime.Token);
            if (muteLocalOutput)
            {
                ILocalOutputLease? lease = null;
                try
                {
                    lease = await _localOutputs.RouteForCastAsync(_selection, linked.Token)
                        .ConfigureAwait(false);
                    await _capture.SwitchAsync(lease.CaptureSelection, linked.Token)
                        .ConfigureAwait(false);
                    _localOutputLease = lease;
                    _muteLocalOutput = true;
                }
                catch
                {
                    if (lease is not null) await lease.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
            }
            else
            {
                await _capture.SwitchAsync(_selection, linked.Token).ConfigureAwait(false);
                var lease = Interlocked.Exchange(ref _localOutputLease, null);
                if (lease is not null) await lease.DisposeAsync().ConfigureAwait(false);
                _muteLocalOutput = false;
            }

            Update(_diagnostics.State, _profile, muteLocalOutput
                ? SystemLanguage.Select("仅音箱播放已开启。", "Speaker-only playback is on.")
                : SystemLanguage.Select("电脑声音已恢复。", "PC playback is restored."));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SetCaptureSelectionAsync(
        CaptureSelection selection,
        CaptureSelection captureSelection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(captureSelection);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!IsCasting || _lifetime is null || _capture is null)
            {
                throw new InvalidOperationException(SystemLanguage.Select(
                    "只能在投送期间切换音频来源。",
                    "The audio source can be changed only while casting."));
            }

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _lifetime.Token);
            await _capture.SwitchAsync(captureSelection, linked.Token).ConfigureAwait(false);
            _selection = selection;
            Update(_diagnostics.State, _profile, SystemLanguage.Select(
                "音频来源已切换。",
                "The audio source has been changed."));
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
            if (_lifetime is null && _capture is null)
            {
                Update(CastSessionState.Idle, null, SystemLanguage.Select("空闲", "Idle"));
                return;
            }

            Update(CastSessionState.Stopping, _profile, SystemLanguage.Select("正在停止投送…", "Stopping the cast…"));
            await CleanupCoreAsync(sendStop: true).ConfigureAwait(false);
            Update(CastSessionState.Idle, null, SystemLanguage.Select("空闲", "Idle"));
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task CleanupCoreAsync(bool sendStop)
    {
        var lifetime = Interlocked.Exchange(ref _lifetime, null);
        lifetime?.Cancel();

        if (sendStop && _renderer is not null)
        {
            using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try { await _controller.StopAsync(_renderer, stopTimeout.Token).ConfigureAwait(false); }
            catch { }
        }

        try
        {
            await DisposeStreamSessionAsync().ConfigureAwait(false);
            if (_capture is not null)
            {
                _capture.CaptureFailed -= OnCaptureFailed;
                await _capture.DisposeAsync().ConfigureAwait(false);
                _capture = null;
            }
        }
        finally
        {
            var localOutputLease = Interlocked.Exchange(ref _localOutputLease, null);
            if (localOutputLease is not null)
            {
                await localOutputLease.DisposeAsync().ConfigureAwait(false);
            }

            if (_frames is not null)
            {
                await _frames.DisposeAsync().ConfigureAwait(false);
                _frames = null;
            }

            lifetime?.Dispose();
            _monitorTask = null;
            _renderer = null;
            _profile = null;
            _selection = null;
        }
    }

    private async Task DisposeStreamSessionAsync()
    {
        if (_streamSession is not null)
        {
            await _streamSession.DisposeAsync().ConfigureAwait(false);
            _streamSession = null;
        }
    }

    private void Update(CastSessionState state, StreamProfile? profile, string message, string? error = null)
    {
        var frames = _frames;
        _diagnostics = new CastDiagnostics(
            state,
            profile,
            frames?.BufferedMilliseconds ?? 0,
            frames?.Overruns ?? 0,
            frames?.Underruns ?? 0,
            message,
            error);
        DiagnosticsChanged?.Invoke(this, _diagnostics);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        await _streamServer.DisposeAsync().ConfigureAwait(false);
        if (_controller is IDisposable disposableController)
        {
            disposableController.Dispose();
        }
        _gate.Dispose();
    }
}
