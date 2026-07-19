using DLNACast.Core.Abstractions;
using DLNACast.Core.Audio;
using DLNACast.Core.Dlna;
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
    private IAudioCaptureSource? _capture;
    private IAsyncDisposable? _localMuteLease;
    private PcmFrameBuffer? _frames;
    private LiveStreamSession? _streamSession;
    private RendererDevice? _renderer;
    private Task? _monitorTask;
    private StreamProfile? _profile;
    private CastDiagnostics _diagnostics = new(CastSessionState.Idle, Message: "空闲");

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
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var ownsNewSession = false;
        try
        {
            if (IsCasting)
            {
                throw new InvalidOperationException("已有投送会话正在运行。");
            }

            ownsNewSession = true;
            _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _renderer = renderer;
            _frames = new PcmFrameBuffer();
            _capture = _audioSources.CreateCapture(selection);
            _capture.CaptureFailed += OnCaptureFailed;
            Update(CastSessionState.Preparing, null, "正在启动音频捕获…");
            await _capture.StartAsync(_frames, _lifetime.Token).ConfigureAwait(false);
            _localMuteLease = await _localOutputs.MuteForCastAsync(selection, _lifetime.Token).ConfigureAwait(false);
            Update(CastSessionState.Preparing, null, "本机扬声器已静音，正在准备直播…");
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
                    Update(CastSessionState.Recovering, StreamProfile.Mp3Cbr320, "PCM 未建立播放，正在回退到 MP3…", ex.Message);
                }
            }

            throw lastFailure ?? new InvalidOperationException("音箱没有建立直播连接。");
        }
        catch (Exception ex) when (ownsNewSession)
        {
            Update(CastSessionState.Error, _profile, "投送启动失败", ex.Message);
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
            ? "正在连接 PCM/WAV 直播…"
            : "正在连接 320 kbps MP3 直播…");
        _streamSession = await _streamServer.StartSessionAsync(
            _renderer!, _frames!, profile, cancellationToken).ConfigureAwait(false);
        await _controller.SetTransportUriAsync(_renderer!, _streamSession.StreamUri, profile, cancellationToken)
            .ConfigureAwait(false);
        await _controller.PlayAsync(_renderer!, cancellationToken).ConfigureAwait(false);

        var connected = await _streamSession.WaitForClientAsync(TimeSpan.FromSeconds(5), cancellationToken)
            .ConfigureAwait(false);
        if (!connected)
        {
            throw new TimeoutException("音箱在 5 秒内没有请求直播 URL。");
        }

        Update(CastSessionState.Streaming, profile, profile == StreamProfile.PcmWave
            ? "正在以低延迟 PCM/WAV 投送（本机已静音）"
            : "正在以兼容 MP3 投送（本机已静音）");
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
                        await RecoverAsync("音箱意外停止播放", cancellationToken).ConfigureAwait(false);
                    }
                    else if (_streamSession is { EarlyDisconnects: >= 2 } && _profile == StreamProfile.PcmWave)
                    {
                        Update(CastSessionState.Recovering, _profile, "PCM 连接连续中断，正在切换 MP3…");
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
                        await RecoverAsync("与音箱的连接中断", cancellationToken).ConfigureAwait(false);
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
            Update(CastSessionState.Error, _profile, "投送已中断", ex.Message);
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

        throw new InvalidOperationException("三次重连均失败。", lastFailure);
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
            message = "正在投送，但捕获已持续静音；内容可能暂停，或受 DRM 保护而无法捕获";
        }
        Update(CastSessionState.Streaming, _profile, message, _diagnostics.LastError);
    }

    private void OnCaptureFailed(object? sender, Exception exception)
    {
        Update(CastSessionState.Error, _profile, "音频捕获已停止", exception.Message);
        _ = StopAsync();
    }

    public async Task StopAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_lifetime is null && _capture is null)
            {
                Update(CastSessionState.Idle, null, "空闲");
                return;
            }

            Update(CastSessionState.Stopping, _profile, "正在停止投送…");
            await CleanupCoreAsync(sendStop: true).ConfigureAwait(false);
            Update(CastSessionState.Idle, null, "空闲");
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
            var localMuteLease = Interlocked.Exchange(ref _localMuteLease, null);
            if (localMuteLease is not null)
            {
                await localMuteLease.DisposeAsync().ConfigureAwait(false);
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
