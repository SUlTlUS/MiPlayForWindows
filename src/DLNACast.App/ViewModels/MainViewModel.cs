using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using DLNACast.App.Commands;
using DLNACast.Core.Abstractions;
using DLNACast.Core.Casting;
using DLNACast.Core.Models;
using DLNACast.Core.Platform;
using DLNACast.Core.Storage;
using Microsoft.UI.Dispatching;

namespace DLNACast.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly IAudioSourceCatalog _audioCatalog;
    private readonly IRendererDiscovery _discovery;
    private readonly CastCoordinator _coordinator;
    private readonly IRendererController _controller;
    private readonly AppSettingsStore _settingsStore;
    private readonly AppLogger _logger;
    private readonly DispatcherQueue _dispatcher;
    private readonly NetworkProfileService _networkProfiles = new();
    private readonly SemaphoreSlim _discoveryGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private RendererDevice? _selectedRenderer;
    private AudioSourceItem? _selectedSource;
    private bool _isProcessMode;
    private bool _isBusy;
    private bool _isPrivateNetwork;
    private string _networkSummary = "正在检查网络…";
    private string _statusText = "正在初始化…";
    private string _profileText = "未连接";
    private string _diagnosticsText = string.Empty;
    private string _errorText = string.Empty;
    private double _remoteVolume = 30;
    private bool _updatingVolume;
    private CancellationTokenSource? _volumeDebounce;
    private AppSettings _settings = new();
    private Task? _periodicRefresh;
    private DateTimeOffset _nextDiagnosticsLogAt;

    public MainViewModel(
        IAudioSourceCatalog audioCatalog,
        IRendererDiscovery discovery,
        CastCoordinator coordinator,
        IRendererController controller,
        AppSettingsStore settingsStore,
        AppLogger logger,
        DispatcherQueue dispatcher)
    {
        _audioCatalog = audioCatalog;
        _discovery = discovery;
        _coordinator = coordinator;
        _controller = controller;
        _settingsStore = settingsStore;
        _logger = logger;
        _dispatcher = dispatcher;

        RefreshRenderersCommand = new AsyncRelayCommand(RefreshRenderersAsync, () => !IsBusy);
        RefreshSourcesCommand = new RelayCommand(RefreshSources, () => !IsCasting);
        ToggleCastingCommand = new AsyncRelayCommand(ToggleCastingAsync, CanToggleCasting);
        RefreshNetworkCommand = new RelayCommand(RefreshNetworkStatus);
        OpenNetworkSettingsCommand = new RelayCommand(OpenNetworkSettings);
        _coordinator.DiagnosticsChanged += OnDiagnosticsChanged;
    }

    public ObservableCollection<RendererDevice> Renderers { get; } = [];
    public ObservableCollection<AudioSourceItem> Sources { get; } = [];
    public AsyncRelayCommand RefreshRenderersCommand { get; }
    public RelayCommand RefreshSourcesCommand { get; }
    public AsyncRelayCommand ToggleCastingCommand { get; }
    public RelayCommand RefreshNetworkCommand { get; }
    public RelayCommand OpenNetworkSettingsCommand { get; }

    public RendererDevice? SelectedRenderer
    {
        get => _selectedRenderer;
        set
        {
            if (!SetField(ref _selectedRenderer, value)) return;
            ToggleCastingCommand.RaiseCanExecuteChanged();
            if (value is not null) _ = LoadVolumeAsync(value);
        }
    }

    public AudioSourceItem? SelectedSource
    {
        get => _selectedSource;
        set
        {
            if (SetField(ref _selectedSource, value)) ToggleCastingCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsProcessMode
    {
        get => _isProcessMode;
        set
        {
            if (!SetField(ref _isProcessMode, value)) return;
            OnPropertyChanged(nameof(IsSystemMode));
            RefreshSources();
        }
    }

    public bool IsSystemMode
    {
        get => !IsProcessMode;
        set { if (value) IsProcessMode = false; }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetField(ref _isBusy, value)) return;
            RefreshRenderersCommand.RaiseCanExecuteChanged();
            ToggleCastingCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsCasting => _coordinator.IsCasting;
    public bool IsPrivateNetwork { get => _isPrivateNetwork; private set => SetField(ref _isPrivateNetwork, value); }
    public string NetworkSummary { get => _networkSummary; private set => SetField(ref _networkSummary, value); }
    public string StatusText { get => _statusText; private set => SetField(ref _statusText, value); }
    public string ProfileText { get => _profileText; private set => SetField(ref _profileText, value); }
    public string DiagnosticsText { get => _diagnosticsText; private set => SetField(ref _diagnosticsText, value); }
    public string ErrorText { get => _errorText; private set => SetField(ref _errorText, value); }
    public string CastButtonText => IsCasting ? "停止投送" : "开始投送";
    public string RemoteVolumeText => $"{RemoteVolume:F0}%";

    public double RemoteVolume
    {
        get => _remoteVolume;
        set
        {
            if (!SetField(ref _remoteVolume, Math.Clamp(value, 0, 100))) return;
            OnPropertyChanged(nameof(RemoteVolumeText));
            if (_updatingVolume) return;
            DebounceVolumeUpdate();
        }
    }

    public async Task InitializeAsync()
    {
        _settings = await _settingsStore.LoadAsync(_lifetime.Token);
        IsProcessMode = string.Equals(_settings.CaptureMode, "Process", StringComparison.OrdinalIgnoreCase);
        RefreshNetworkStatus();
        RefreshSources();
        await RefreshRenderersAsync();
        _periodicRefresh = PeriodicRefreshAsync(_lifetime.Token);
    }

    public void ReportInitializationFailure(Exception exception)
    {
        StatusText = "初始化失败";
        ErrorText = exception.Message;
        _logger.Error("应用初始化失败", exception);
    }

    private void RefreshSources()
    {
        if (IsCasting) return;
        var previous = SelectedSource?.Id ?? _settings.LastSourceId;
        IReadOnlyList<AudioSourceItem> items;
        try
        {
            items = IsProcessMode ? _audioCatalog.GetCandidateProcesses() : _audioCatalog.GetOutputDevices();
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
            _logger.Error("枚举音源失败", ex);
            return;
        }

        Sources.Clear();
        foreach (var item in items) Sources.Add(item);
        SelectedSource = Sources.FirstOrDefault(item => item.Id == previous) ?? Sources.FirstOrDefault();
    }

    private async Task RefreshRenderersAsync()
    {
        if (!await _discoveryGate.WaitAsync(0, _lifetime.Token)) return;
        IsBusy = true;
        ErrorText = string.Empty;
        StatusText = "正在搜索局域网音箱…";
        try
        {
            var previousUdn = SelectedRenderer?.Udn ?? _settings.LastRendererUdn;
            var devices = await _discovery.SearchAsync(_lifetime.Token);
            await RunOnUiAsync(() =>
            {
                Renderers.Clear();
                foreach (var device in devices) Renderers.Add(device);
                SelectedRenderer = Renderers.FirstOrDefault(item => item.Udn == previousUdn) ?? Renderers.FirstOrDefault();
                StatusText = Renderers.Count == 0
                    ? "未找到 DLNA MediaRenderer"
                    : $"已发现 {Renderers.Count} 台音箱";
            });
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
            StatusText = "音箱发现失败";
            _logger.Error("SSDP 搜索失败", ex);
        }
        finally
        {
            IsBusy = false;
            _discoveryGate.Release();
        }
    }

    private async Task ToggleCastingAsync()
    {
        if (IsCasting)
        {
            await StopCastingAsync();
            return;
        }

        RefreshNetworkStatus();
        if (!IsPrivateNetwork)
        {
            ErrorText = "请先把家庭 Wi-Fi 设为“专用网络”，再开始投送。";
            return;
        }

        if (SelectedRenderer is null || SelectedSource is null) return;
        IsBusy = true;
        ErrorText = string.Empty;
        try
        {
            CaptureSelection selection = IsProcessMode
                ? new CaptureSelection.Process(SelectedSource.ProcessId!.Value, SelectedSource.DisplayName, true)
                : new CaptureSelection.SystemMix(SelectedSource.Id, SelectedSource.DisplayName);
            await _coordinator.StartAsync(SelectedRenderer, selection, _settings.AllowMp3Fallback, _lifetime.Token);
            _settings = _settings with
            {
                LastRendererUdn = SelectedRenderer.Udn,
                CaptureMode = IsProcessMode ? "Process" : "SystemMix",
                LastSourceId = SelectedSource.Id
            };
            await _settingsStore.SaveAsync(_settings, _lifetime.Token);
            _logger.Info($"开始投送到 {SelectedRenderer.FriendlyName}，音源 {SelectedSource.DisplayName}");
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
            _logger.Error("投送启动失败", ex);
        }
        finally
        {
            IsBusy = false;
            NotifyCastStateChanged();
        }
    }

    public async Task StopCastingAsync()
    {
        try
        {
            await _coordinator.StopAsync();
            _logger.Info("投送已停止");
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
            _logger.Error("停止投送失败", ex);
        }
        finally
        {
            NotifyCastStateChanged();
        }
    }

    private bool CanToggleCasting() => !IsBusy && (IsCasting ||
        (SelectedRenderer is not null && SelectedSource is not null));

    private void RefreshNetworkStatus()
    {
        var status = _networkProfiles.GetStatus();
        IsPrivateNetwork = status.IsPrivate;
        NetworkSummary = status.Summary;
        ToggleCastingCommand.RaiseCanExecuteChanged();
    }

    private static void OpenNetworkSettings() => Process.Start(new ProcessStartInfo("ms-settings:network-status")
    {
        UseShellExecute = true
    });

    private async Task LoadVolumeAsync(RendererDevice renderer)
    {
        try
        {
            var volume = await _controller.GetVolumeAsync(renderer, _lifetime.Token);
            if (volume is null) return;
            _updatingVolume = true;
            RemoteVolume = volume.Value;
        }
        catch
        {
        }
        finally
        {
            _updatingVolume = false;
        }
    }

    private void DebounceVolumeUpdate()
    {
        _volumeDebounce?.Cancel();
        _volumeDebounce?.Dispose();
        _volumeDebounce = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var token = _volumeDebounce.Token;
        var renderer = SelectedRenderer;
        if (renderer is null) return;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(150, token);
                await _controller.SetVolumeAsync(renderer, (int)Math.Round(RemoteVolume), token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger.Error("设置音箱音量失败", ex);
            }
        }, token);
    }

    private void OnDiagnosticsChanged(object? sender, CastDiagnostics diagnostics) =>
        _ = RunOnUiAsync(() =>
        {
            StatusText = diagnostics.Message;
            ProfileText = diagnostics.Profile switch
            {
                StreamProfile.PcmWave => "PCM / WAV",
                StreamProfile.Mp3Cbr320 => "MP3 320 kbps",
                _ => "未连接"
            };
            DiagnosticsText = $"应用缓冲 {diagnostics.BufferedMilliseconds} ms（目标 60 ms） · Overrun {diagnostics.Overruns} · Underrun {diagnostics.Underruns}";
            ErrorText = diagnostics.LastError ?? ErrorText;
            if (diagnostics.State == CastSessionState.Streaming && DateTimeOffset.UtcNow >= _nextDiagnosticsLogAt)
            {
                _logger.Info($"投送诊断：profile={ProfileText}, buffer={diagnostics.BufferedMilliseconds}ms, overruns={diagnostics.Overruns}, underruns={diagnostics.Underruns}");
                _nextDiagnosticsLogAt = DateTimeOffset.UtcNow.AddSeconds(5);
            }
            NotifyCastStateChanged();
        });

    private void NotifyCastStateChanged()
    {
        OnPropertyChanged(nameof(IsCasting));
        OnPropertyChanged(nameof(CastButtonText));
        ToggleCastingCommand.RaiseCanExecuteChanged();
        RefreshSourcesCommand.RaiseCanExecuteChanged();
    }

    private async Task PeriodicRefreshAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                RefreshNetworkStatus();
                if (!IsCasting) await RefreshRenderersAsync();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private Task RunOnUiAsync(Action action)
    {
        if (_dispatcher.HasThreadAccess)
        {
            action();
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_dispatcher.TryEnqueue(() =>
        {
            try
            {
                action();
                completion.TrySetResult();
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        }))
        {
            completion.TrySetException(new InvalidOperationException("WinUI 调度队列已经关闭。"));
        }
        return completion.Task;
    }

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        _volumeDebounce?.Cancel();
        if (_periodicRefresh is not null)
        {
            try { await _periodicRefresh; }
            catch (OperationCanceledException) { }
        }
        await _coordinator.DisposeAsync();
        _coordinator.DiagnosticsChanged -= OnDiagnosticsChanged;
        _volumeDebounce?.Dispose();
        _lifetime.Dispose();
        _discoveryGate.Dispose();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
