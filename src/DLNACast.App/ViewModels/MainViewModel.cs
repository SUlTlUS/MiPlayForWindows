using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using DLNACast.App.Commands;
using DLNACast.App.Localization;
using DLNACast.Core.Abstractions;
using DLNACast.Core.Audio;
using DLNACast.Core.Casting;
using DLNACast.Core.Localization;
using DLNACast.Core.MiPlay;
using DLNACast.Core.Models;
using DLNACast.Core.Platform;
using DLNACast.Core.Storage;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace DLNACast.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly IAudioSourceCatalog _audioCatalog;
    private readonly IRendererDiscovery _discovery;
    private readonly Func<CastCoordinator> _coordinatorFactory;
    private readonly Func<MiPlaySystemAudioTransmitter> _miPlayTransmitterFactory;
    private readonly ILocalOutputManager _localOutputs;
    private readonly IRendererController _controller;
    private readonly AppSettingsStore _settingsStore;
    private readonly AppLogger _logger;
    private readonly DispatcherQueue _dispatcher;
    private readonly NetworkProfileService _networkProfiles = new();
    private readonly SemaphoreSlim _discoveryGate = new(1, 1);
    private readonly SemaphoreSlim _selectionGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DispatcherQueueTimer _castDurationTimer;
    private readonly Stopwatch _castDurationStopwatch = new();
    private readonly Lock _sessionGate = new();
    private readonly Dictionary<string, DlnaSessionHandle> _dlnaSessions = [];
    private readonly Dictionary<string, MiPlaySessionHandle> _miPlaySessions = [];
    private AudioSourceItem? _selectedSource;
    private bool _isProcessMode;
    private bool _isMiPlayMode;
    private bool _isStereoSplitMode;
    private bool _isSpeakerOnlyPlayback = true;
    private bool _isBusy;
    private bool _isPrivateNetwork;
    private string _networkSummary = SystemLanguage.Select("正在检查网络…", "Checking the network…");
    private string _statusText = SystemLanguage.Select("正在初始化…", "Initializing…");
    private string _profileText = "DLNA";
    private string _castDurationText = "00:00";
    private string _diagnosticsText = string.Empty;
    private string _errorText = string.Empty;
    private AppSettings _settings = new();
    private Task? _periodicRefresh;
    private DateTimeOffset _nextDiagnosticsLogAt;
    private bool _suppressSelectionEvents;
    private bool _updatingStereoMasterVolume;
    private double _stereoMasterVolume = 30;
    private int _stereoSelectionRevision;
    private bool _isInitialized;

    public MainViewModel(
        IAudioSourceCatalog audioCatalog,
        IRendererDiscovery discovery,
        Func<CastCoordinator> coordinatorFactory,
        Func<MiPlaySystemAudioTransmitter> miPlayTransmitterFactory,
        ILocalOutputManager localOutputs,
        IRendererController controller,
        AppSettingsStore settingsStore,
        AppLogger logger,
        DispatcherQueue dispatcher)
    {
        _audioCatalog = audioCatalog;
        _discovery = discovery;
        _coordinatorFactory = coordinatorFactory;
        _miPlayTransmitterFactory = miPlayTransmitterFactory;
        _localOutputs = localOutputs;
        _controller = controller;
        _settingsStore = settingsStore;
        _logger = logger;
        _dispatcher = dispatcher;
        _castDurationTimer = dispatcher.CreateTimer();
        _castDurationTimer.Interval = TimeSpan.FromSeconds(1);
        _castDurationTimer.IsRepeating = true;
        _castDurationTimer.Tick += OnCastDurationTimerTick;

        RefreshRenderersCommand = new AsyncRelayCommand(RefreshRenderersAsync, () => !IsBusy);
        RefreshSourcesCommand = new RelayCommand(RefreshSources, () => !IsBusy);
        RefreshNetworkCommand = new RelayCommand(RefreshNetworkStatus);
        OpenNetworkSettingsCommand = new RelayCommand(OpenNetworkSettings);
    }

    public ObservableCollection<RendererItemViewModel> Renderers { get; } = [];
    public ObservableCollection<AudioSourceItem> Sources { get; } = [];
    public UiText Text { get; } = new();
    public AsyncRelayCommand RefreshRenderersCommand { get; }
    public RelayCommand RefreshSourcesCommand { get; }
    public RelayCommand RefreshNetworkCommand { get; }
    public RelayCommand OpenNetworkSettingsCommand { get; }

    public AudioSourceItem? SelectedSource
    {
        get => _selectedSource;
        set
        {
            if (EqualityComparer<AudioSourceItem?>.Default.Equals(_selectedSource, value) || IsBusy) return;
            if (IsCasting && value is not null)
            {
                _ = SwitchAudioSourceWhileCastingAsync(
                    _isProcessMode,
                    value,
                    () => SetSelectedSource(value));
                return;
            }
            SetSelectedSource(value);
        }
    }

    public bool IsProcessMode
    {
        get => _isProcessMode;
        set
        {
            if (value == _isProcessMode || IsBusy) return;
            if (IsCasting)
            {
                BeginProcessModeSwitch(value);
                return;
            }
            SetProcessMode(value);
        }
    }

    public bool IsMiPlayMode
    {
        get => _isMiPlayMode;
        set
        {
            if (IsCasting) return;
            if (!SetField(ref _isMiPlayMode, value)) return;
            OnPropertyChanged(nameof(IsDlnaMode));
            ProfileText = SelectedTransportProfileText;
            OnPropertyChanged(nameof(CanUseProcessCapture));
            OnPropertyChanged(nameof(CanUseStereoSplit));
        }
    }

    public bool IsDlnaMode
    {
        get => !IsMiPlayMode;
        set { if (value) IsMiPlayMode = false; }
    }

    public bool CanUseProcessCapture => !IsBusy;
    public bool CanChangeTransport => !IsCasting;
    public bool CanUseSpeakerOnlyPlayback => !IsBusy;
    public bool CanUseStereoSplit => !IsBusy;
    public Visibility AudioSourceSelectorVisibility =>
        IsProcessMode ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NormalRendererListVisibility => IsStereoSplitMode ? Visibility.Collapsed : Visibility.Visible;
    public Visibility StereoRendererListsVisibility => IsStereoSplitMode ? Visibility.Visible : Visibility.Collapsed;
    public string LeftChannelText => SystemLanguage.Select("左声道", "Left channel");
    public string RightChannelText => SystemLanguage.Select("右声道", "Right channel");
    public string StereoMasterVolumeText => $"{StereoMasterVolume:F0}%";
    public string StereoMasterVolumeLabel => SystemLanguage.Select("总音量", "Master volume");
    public string StereoMasterVolumeAutomationName => SystemLanguage.Select(
        "双音箱立体声总音量",
        "Two-speaker stereo master volume");
    public bool CanAdjustStereoMasterVolume =>
        IsStereoSplitMode &&
        Renderers.FirstOrDefault(renderer => renderer.IsLeftChannel)?.CanControlVolume == true &&
        Renderers.FirstOrDefault(renderer => renderer.IsRightChannel)?.CanControlVolume == true;

    public double StereoMasterVolume
    {
        get => _stereoMasterVolume;
        set
        {
            var requested = Math.Clamp(value, 0, 100);
            if (_updatingStereoMasterVolume || !CanAdjustStereoMasterVolume)
            {
                SetStereoMasterVolume(requested);
                return;
            }

            var left = Renderers.First(renderer => renderer.IsLeftChannel);
            var right = Renderers.First(renderer => renderer.IsRightChannel);
            var (Left, Right) = StereoVolumeScaler.ScaleToMaster(left.Volume, right.Volume, requested);

            _updatingStereoMasterVolume = true;
            try
            {
                SetStereoMasterVolume(requested);
                left.Volume = Left;
                right.Volume = Right;
            }
            finally
            {
                _updatingStereoMasterVolume = false;
            }
        }
    }

    public bool IsStereoSplitMode
    {
        get => _isStereoSplitMode;
        set
        {
            if (value == _isStereoSplitMode || IsBusy) return;
            if (IsCasting)
            {
                StopCastingThenApply(() => SetStereoSplitMode(value));
                return;
            }
            SetStereoSplitMode(value);
        }
    }

    public bool IsSpeakerOnlyPlayback
    {
        get => _isSpeakerOnlyPlayback;
        set
        {
            if (value == _isSpeakerOnlyPlayback || IsBusy) return;
            if (IsCasting)
            {
                _ = SetSpeakerOnlyPlaybackWhileCastingAsync(value);
                return;
            }
            SetSpeakerOnlyPlayback(value);
        }
    }

    private void SetSpeakerOnlyPlayback(bool value)
    {
        if (!SetField(ref _isSpeakerOnlyPlayback, value, nameof(IsSpeakerOnlyPlayback))) return;
        _settings = _settings with { SpeakerOnlyPlayback = value };
        if (_isInitialized) _ = SaveSpeakerOnlyPlaybackAsync(_settings);
    }

    private async Task SetSpeakerOnlyPlaybackWhileCastingAsync(bool value)
    {
        IsBusy = true;
        ErrorText = string.Empty;
        var changedDlna = new List<DlnaSessionHandle>();
        var changedMiPlay = new List<MiPlaySessionHandle>();
        try
        {
            DlnaSessionHandle[] dlnaSessions;
            MiPlaySessionHandle[] miPlaySessions;
            lock (_sessionGate)
            {
                dlnaSessions = [.. _dlnaSessions.Values];
                miPlaySessions = [.. _miPlaySessions.Values];
            }

            foreach (var session in dlnaSessions)
            {
                await session.Coordinator.SetMuteLocalOutputAsync(value, _lifetime.Token);
                changedDlna.Add(session);
            }
            foreach (var session in miPlaySessions)
            {
                await SetMiPlaySpeakerOnlyPlaybackAsync(session, value, _lifetime.Token);
                changedMiPlay.Add(session);
            }

            SetSpeakerOnlyPlayback(value);
            StatusText = value
                ? SystemLanguage.Select("已开启仅音箱播放", "Speaker-only playback is on")
                : SystemLanguage.Select("已恢复电脑播放", "PC playback is restored");
        }
        catch (Exception exception)
        {
            foreach (var session in changedMiPlay.AsEnumerable().Reverse())
            {
                try { await SetMiPlaySpeakerOnlyPlaybackAsync(session, !value, CancellationToken.None); }
                catch { }
            }
            foreach (var session in changedDlna.AsEnumerable().Reverse())
            {
                try { await session.Coordinator.SetMuteLocalOutputAsync(!value); }
                catch { }
            }
            OnPropertyChanged(nameof(IsSpeakerOnlyPlayback));
            ErrorText = exception.Message;
            _logger.Error(SystemLanguage.Select(
                "切换仅音箱播放失败",
                "Failed to switch speaker-only playback"), exception);
        }
        finally
        {
            IsBusy = false;
            NotifyCastStateChanged();
        }
    }

    public bool IsSystemMode
    {
        get => !IsProcessMode;
        set { if (value) IsProcessMode = false; }
    }

    private void SetSelectedSource(AudioSourceItem? value) =>
        SetField(ref _selectedSource, value, nameof(SelectedSource));

    private void SetProcessMode(bool value)
    {
        if (!SetProcessModeCore(value)) return;
        RefreshSources();
    }

    private bool SetProcessModeCore(bool value)
    {
        if (!SetField(ref _isProcessMode, value, nameof(IsProcessMode))) return false;
        OnPropertyChanged(nameof(IsSystemMode));
        OnPropertyChanged(nameof(AudioSourceSelectorVisibility));
        OnPropertyChanged(nameof(CanUseSpeakerOnlyPlayback));
        return true;
    }

    private void BeginProcessModeSwitch(bool processMode)
    {
        IReadOnlyList<AudioSourceItem> items;
        try
        {
            items = processMode
                ? _audioCatalog.GetCandidateProcesses()
                : _audioCatalog.GetOutputDevices();
        }
        catch (Exception exception)
        {
            ErrorText = exception.Message;
            OnPropertyChanged(nameof(IsProcessMode));
            OnPropertyChanged(nameof(IsSystemMode));
            return;
        }

        var source = items.FirstOrDefault(item => item.Id == _settings.LastSourceId) ??
                     items.FirstOrDefault();
        if (source is null)
        {
            ErrorText = SystemLanguage.Select(
                "当前模式没有可用的音频来源。",
                "The selected mode has no available audio source.");
            OnPropertyChanged(nameof(IsProcessMode));
            OnPropertyChanged(nameof(IsSystemMode));
            return;
        }

        var sourceSnapshot = items.ToArray();
        _ = SwitchAudioSourceWhileCastingAsync(processMode, source, () =>
        {
            SetProcessModeCore(processMode);
            Sources.Clear();
            foreach (var item in sourceSnapshot) Sources.Add(item);
            SetSelectedSource(source);
        });
    }

    private async Task SwitchAudioSourceWhileCastingAsync(
        bool processMode,
        AudioSourceItem source,
        Action commitSelection)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(commitSelection);
        if (_selectedSource is null) return;

        var previousSelection = CreateCaptureSelection(_isProcessMode, _selectedSource);
        var nextSelection = CreateCaptureSelection(processMode, source);
        var speakerOnly = _isSpeakerOnlyPlayback;
        var changedDlna = new List<DlnaSessionHandle>();
        var changedMiPlay = new List<MiPlaySessionHandle>();
        ISwitchableLocalOutputManager? routeController = null;
        var routeChanged = false;
        IsBusy = true;
        ErrorText = string.Empty;
        try
        {
            DlnaSessionHandle[] dlnaSessions;
            MiPlaySessionHandle[] miPlaySessions;
            lock (_sessionGate)
            {
                dlnaSessions = [.. _dlnaSessions.Values];
                miPlaySessions = [.. _miPlaySessions.Values];
            }

            var captureSelection = nextSelection;
            if (speakerOnly)
            {
                routeController = _localOutputs as ISwitchableLocalOutputManager ??
                    throw new NotSupportedException(SystemLanguage.Select(
                        "当前本机音频路由不支持投送中切换来源。",
                        "The current local-output router cannot switch sources while casting."));
                captureSelection = await routeController.SwitchActiveRouteAsync(
                    nextSelection,
                    _lifetime.Token);
                routeChanged = !Equals(previousSelection, nextSelection);
            }

            foreach (var session in dlnaSessions)
            {
                changedDlna.Add(session);
                await session.Coordinator.SetCaptureSelectionAsync(
                    nextSelection,
                    captureSelection,
                    _lifetime.Token);
            }
            foreach (var session in miPlaySessions)
            {
                changedMiPlay.Add(session);
                await session.Transmitter.SetCaptureSelectionAsync(
                    captureSelection,
                    _lifetime.Token);
                session.OriginalSelection = nextSelection;
            }

            commitSelection();
            _settings = _settings with
            {
                CaptureMode = processMode ? "Process" : "SystemMix",
                LastSourceId = source.Id,
            };
            StatusText = SystemLanguage.Select(
                $"音频来源已切换为 {source.DisplayName}，投送保持连接",
                $"Audio source changed to {source.DisplayName}; casting remains connected");
        }
        catch (Exception exception)
        {
            var rollbackCaptureSelection = previousSelection;
            if (speakerOnly && routeChanged && routeController is not null)
            {
                try
                {
                    rollbackCaptureSelection = await routeController.SwitchActiveRouteAsync(
                        previousSelection,
                        CancellationToken.None);
                }
                catch (Exception rollbackException)
                {
                    _logger.Error(SystemLanguage.Select(
                        "恢复原音频路由失败",
                        "Failed to restore the previous audio route"), rollbackException);
                }
            }

            foreach (var session in changedMiPlay.AsEnumerable().Reverse())
            {
                try
                {
                    await session.Transmitter.SetCaptureSelectionAsync(
                        rollbackCaptureSelection,
                        CancellationToken.None);
                    session.OriginalSelection = previousSelection;
                }
                catch { }
            }
            foreach (var session in changedDlna.AsEnumerable().Reverse())
            {
                try
                {
                    await session.Coordinator.SetCaptureSelectionAsync(
                        previousSelection,
                        rollbackCaptureSelection,
                        CancellationToken.None);
                }
                catch { }
            }

            OnPropertyChanged(nameof(SelectedSource));
            OnPropertyChanged(nameof(IsProcessMode));
            OnPropertyChanged(nameof(IsSystemMode));
            ErrorText = exception.Message;
            _logger.Error(SystemLanguage.Select(
                "投送中切换音频来源失败",
                "Failed to switch the audio source while casting"), exception);
        }
        finally
        {
            IsBusy = false;
            NotifyCastStateChanged();
        }
    }

    private static CaptureSelection CreateCaptureSelection(
        bool processMode,
        AudioSourceItem source) => processMode
        ? new CaptureSelection.Process(
            source.ProcessId ?? throw new InvalidOperationException(
                SystemLanguage.Select("所选应用已经退出。", "The selected application has exited.")),
            source.DisplayName,
            true)
        : new CaptureSelection.SystemMix(source.Id, source.DisplayName);

    private void SetStereoSplitMode(bool value)
    {
        if (!SetField(ref _isStereoSplitMode, value, nameof(IsStereoSplitMode))) return;
        OnPropertyChanged(nameof(NormalRendererListVisibility));
        OnPropertyChanged(nameof(StereoRendererListsVisibility));

        _suppressSelectionEvents = true;
        try
        {
            foreach (var renderer in Renderers)
            {
                renderer.IsSelected = false;
                renderer.IsLeftChannel = false;
                renderer.IsRightChannel = false;
                renderer.IsStereoMode = value;
            }
        }
        finally
        {
            _suppressSelectionEvents = false;
        }
        Interlocked.Increment(ref _stereoSelectionRevision);
        NotifyRendererSelectionChanged();
    }

    private void StopCastingThenApply(Action apply) =>
        _ = StopCastingThenApplyAsync(apply);

    private async Task StopCastingThenApplyAsync(Action apply)
    {
        ArgumentNullException.ThrowIfNull(apply);
        try
        {
            await StopCastingAsync();
            if (!_lifetime.IsCancellationRequested) apply();
        }
        catch (Exception exception)
        {
            ErrorText = exception.Message;
            _logger.Error(SystemLanguage.Select(
                "停止投送并切换选择失败",
                "Failed to stop casting and change the selection"), exception);
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetField(ref _isBusy, value)) return;
            RefreshRenderersCommand.RaiseCanExecuteChanged();
            RefreshSourcesCommand.RaiseCanExecuteChanged();
            foreach (var renderer in Renderers) renderer.IsSelectionEnabled = !value;
            OnPropertyChanged(nameof(CanUseProcessCapture));
            OnPropertyChanged(nameof(CanUseSpeakerOnlyPlayback));
            OnPropertyChanged(nameof(CanUseStereoSplit));
        }
    }

    public bool IsCasting
    {
        get
        {
            lock (_sessionGate)
            {
                return _dlnaSessions.Values.Any(session => session.Coordinator.IsCasting) ||
                       _miPlaySessions.Values.Any(session => session.Transmitter.IsActive);
            }
        }
    }
    public bool IsPrivateNetwork { get => _isPrivateNetwork; private set => SetField(ref _isPrivateNetwork, value); }
    public string NetworkSummary { get => _networkSummary; private set => SetField(ref _networkSummary, value); }
    public string StatusText { get => _statusText; private set => SetField(ref _statusText, value); }
    public string ProfileText { get => _profileText; private set => SetField(ref _profileText, value); }
    public string CastDurationText
    {
        get => _castDurationText;
        private set => SetField(ref _castDurationText, value);
    }
    private string SelectedTransportProfileText => IsMiPlayMode
        ? SystemLanguage.Select("小米妙播", "MiPlay")
        : "DLNA";
    public string DiagnosticsText
    {
        get => _diagnosticsText;
        private set
        {
            if (!SetField(ref _diagnosticsText, value)) return;
            OnPropertyChanged(nameof(DiagnosticsVisibility));
        }
    }
    public string ErrorText
    {
        get => _errorText;
        private set
        {
            if (!SetField(ref _errorText, value)) return;
            OnPropertyChanged(nameof(ErrorVisibility));
        }
    }
    public Visibility DiagnosticsVisibility => string.IsNullOrWhiteSpace(DiagnosticsText)
        ? Visibility.Collapsed
        : Visibility.Visible;
    public Visibility ErrorVisibility => string.IsNullOrWhiteSpace(ErrorText)
        ? Visibility.Collapsed
        : Visibility.Visible;
    public Visibility CastingStatusVisibility => IsCasting
        ? Visibility.Visible
        : Visibility.Collapsed;
    public Visibility InactiveStatusVisibility => IsCasting
        ? Visibility.Collapsed
        : Visibility.Visible;
    public int SelectedRendererCount => IsStereoSplitMode
        ? Renderers.Count(renderer => renderer.IsLeftChannel || renderer.IsRightChannel)
        : Renderers.Count(renderer => renderer.IsSelected);
    public bool HasNoRenderers => Renderers.Count == 0;
    public string RendererSelectionText
    {
        get
        {
            if (IsStereoSplitMode)
            {
                var left = Renderers.FirstOrDefault(renderer => renderer.IsLeftChannel);
                var right = Renderers.FirstOrDefault(renderer => renderer.IsRightChannel);
                if (left is not null && right is not null)
                {
                    return SystemLanguage.Select(
                        $"左：{left.FriendlyName} · 右：{right.FriendlyName}",
                        $"Left: {left.FriendlyName} · Right: {right.FriendlyName}");
                }
                if (left is not null)
                {
                    return SystemLanguage.Select("请选择右声道音箱", "Choose the right-channel speaker");
                }
                if (right is not null)
                {
                    return SystemLanguage.Select("请选择左声道音箱", "Choose the left-channel speaker");
                }
                return SystemLanguage.Select(
                    "分别为左、右声道指定一台音箱",
                    "Assign one speaker to each of the left and right channels");
            }

            return SelectedRendererCount == 0
                ? SystemLanguage.Select("勾选音箱即可开始投送", "Select a speaker to start casting")
                : SystemLanguage.Select(
                    $"正在向 {SelectedRendererCount} 台音箱投送",
                    SelectedRendererCount == 1 ? "Casting to 1 speaker" : $"Casting to {SelectedRendererCount} speakers");
        }
    }

    public async Task InitializeAsync()
    {
        _settings = await _settingsStore.LoadAsync(_lifetime.Token);
        _isSpeakerOnlyPlayback = _settings.SpeakerOnlyPlayback;
        OnPropertyChanged(nameof(IsSpeakerOnlyPlayback));
        IsMiPlayMode = string.Equals(_settings.TransportMode, "MiPlay", StringComparison.OrdinalIgnoreCase);
        IsProcessMode = string.Equals(_settings.CaptureMode, "Process", StringComparison.OrdinalIgnoreCase);
        _isInitialized = true;
        RefreshNetworkStatus();
        RefreshSources();
        await RefreshRenderersAsync();
        _periodicRefresh = PeriodicRefreshAsync(_lifetime.Token);
    }

    public void ReportInitializationFailure(Exception exception)
    {
        StatusText = SystemLanguage.Select("初始化失败", "Initialization failed");
        ErrorText = exception.Message;
        _logger.Error(SystemLanguage.Select("应用初始化失败", "App initialization failed"), exception);
    }

    private void RefreshSources()
    {
        var isCasting = IsCasting;
        var previousSource = SelectedSource;
        var previous = previousSource?.Id ?? _settings.LastSourceId;
        IReadOnlyList<AudioSourceItem> items;
        try
        {
            items = IsProcessMode ? _audioCatalog.GetCandidateProcesses() : _audioCatalog.GetOutputDevices();
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
            _logger.Error(SystemLanguage.Select("枚举音源失败", "Failed to enumerate audio sources"), ex);
            return;
        }

        Sources.Clear();
        foreach (var item in items) Sources.Add(item);
        if (isCasting && previousSource is not null && Sources.All(item => item.Id != previousSource.Id))
        {
            Sources.Insert(0, previousSource);
        }

        var refreshedSelection = Sources.FirstOrDefault(item => item.Id == previous) ?? Sources.FirstOrDefault();
        if (isCasting)
        {
            SetSelectedSource(refreshedSelection ?? previousSource);
        }
        else
        {
            SelectedSource = refreshedSelection;
        }
    }

    private async Task RefreshRenderersAsync()
    {
        if (!await _discoveryGate.WaitAsync(0, _lifetime.Token)) return;
        var updateDiscoveryStatus = !IsCasting;
        IsBusy = true;
        if (updateDiscoveryStatus)
        {
            ErrorText = string.Empty;
            StatusText = SystemLanguage.Select("正在搜索局域网音箱…", "Searching for speakers on the local network…");
        }
        try
        {
            var selectedUdns = Renderers
                .Where(renderer => renderer.IsSelected)
                .Select(renderer => renderer.Udn)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var leftChannelUdn = Renderers.FirstOrDefault(renderer => renderer.IsLeftChannel)?.Udn;
            var rightChannelUdn = Renderers.FirstOrDefault(renderer => renderer.IsRightChannel)?.Udn;
            var devices = await _discovery.SearchAsync(_lifetime.Token);
            RendererItemViewModel[] items = [];
            await RunOnUiAsync(() =>
            {
                foreach (var renderer in Renderers)
                {
                    renderer.SelectionChanged -= OnRendererSelectionChanged;
                    renderer.ChannelAssignmentChanged -= OnRendererChannelAssignmentChanged;
                    renderer.VolumeChanged -= OnRendererVolumeChanged;
                    renderer.Dispose();
                }
                Renderers.Clear();
                foreach (var device in devices)
                {
                    var item = new RendererItemViewModel(device, _controller, _logger, _lifetime.Token)
                    {
                        IsSelected = !IsStereoSplitMode && selectedUdns.Contains(device.Udn),
                        IsLeftChannel = IsStereoSplitMode && string.Equals(device.Udn, leftChannelUdn, StringComparison.OrdinalIgnoreCase),
                        IsRightChannel = IsStereoSplitMode && string.Equals(device.Udn, rightChannelUdn, StringComparison.OrdinalIgnoreCase),
                        IsStereoMode = IsStereoSplitMode,
                    };
                    item.SelectionChanged += OnRendererSelectionChanged;
                    item.ChannelAssignmentChanged += OnRendererChannelAssignmentChanged;
                    item.VolumeChanged += OnRendererVolumeChanged;
                    Renderers.Add(item);
                }
                items = [.. Renderers];
                NotifyRendererSelectionChanged();
                if (updateDiscoveryStatus)
                {
                    StatusText = Renderers.Count == 0
                        ? SystemLanguage.Select("未找到 DLNA MediaRenderer", "No DLNA MediaRenderer found")
                        : SystemLanguage.Select(
                            $"已发现 {Renderers.Count} 台音箱",
                            Renderers.Count == 1 ? "Found 1 speaker" : $"Found {Renderers.Count} speakers");
                }
            });
            await LoadRendererVolumesAsync(items);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (updateDiscoveryStatus)
            {
                ErrorText = ex.Message;
                StatusText = SystemLanguage.Select("音箱发现失败", "Speaker discovery failed");
            }
            _logger.Error(SystemLanguage.Select("SSDP 搜索失败", "SSDP search failed"), ex);
        }
        finally
        {
            IsBusy = false;
            _discoveryGate.Release();
        }
    }

    private async Task ApplyRendererSelectionAsync(RendererItemViewModel renderer)
    {
        try
        {
            await _selectionGate.WaitAsync(_lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return;
        }

        try
        {
            if (renderer.IsSelected)
            {
                await StartRendererAsync(renderer);
            }
            else
            {
                await StopRendererAsync(renderer);
            }
        }
        finally
        {
            _selectionGate.Release();
        }
    }

    private async Task ApplyStereoPairAsync(int revision)
    {
        try
        {
            await _selectionGate.WaitAsync(_lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return;
        }

        try
        {
            if (revision != Volatile.Read(ref _stereoSelectionRevision) || !IsStereoSplitMode) return;

            if (ActiveSessionCount > 0)
            {
                IsBusy = true;
                await StopActiveSessionsPreservingAssignmentsAsync();
            }

            var left = Renderers.FirstOrDefault(renderer => renderer.IsLeftChannel);
            var right = Renderers.FirstOrDefault(renderer => renderer.IsRightChannel);
            if (left is null || right is null)
            {
                StatusText = SystemLanguage.Select(
                    "等待指定左右声道音箱",
                    "Waiting for left and right speakers");
                return;
            }

            RefreshNetworkStatus();
            if (!IsPrivateNetwork)
            {
                ErrorText = SystemLanguage.Select(
                    "请先把家庭 Wi-Fi 设为“专用网络”，再开始投送。",
                    "Set your home Wi-Fi to a Private network before casting.");
                return;
            }
            if (SelectedSource is null)
            {
                ErrorText = SystemLanguage.Select(
                    "当前没有可用的音频来源。",
                    "No audio source is currently available.");
                return;
            }

            IsBusy = true;
            ErrorText = string.Empty;
            StatusText = IsMiPlayMode
                ? SystemLanguage.Select("正在启动双音箱同步播放…", "Starting synchronized dual-speaker playback…")
                : SystemLanguage.Select("正在启动左右声道投送…", "Starting left/right channel casting…");
            CaptureSelection selection = IsProcessMode
                ? new CaptureSelection.Process(SelectedSource.ProcessId!.Value, SelectedSource.DisplayName, true)
                : new CaptureSelection.SystemMix(SelectedSource.Id, SelectedSource.DisplayName);
            StartResult[] results;
            if (IsMiPlayMode)
            {
                results = await StartMiPlayPairAsync(left, right, selection);
            }
            else
            {
                results = await Task.WhenAll(
                    StartDlnaTargetAsync(left, selection, AudioChannelRoute.LeftAsMono),
                    StartDlnaTargetAsync(right, selection, AudioChannelRoute.RightAsMono));
            }
            var failures = results.Where(result => result.Error is not null).ToArray();
            if (failures.Length > 0)
            {
                await StopActiveSessionsPreservingAssignmentsAsync();
                throw new AggregateException(failures.Select(result => result.Error!));
            }

            _settings = _settings with
            {
                LastRendererUdn = left.Udn,
                CaptureMode = IsProcessMode ? "Process" : "SystemMix",
                LastSourceId = SelectedSource.Id,
                TransportMode = IsMiPlayMode ? "MiPlay" : "DLNA"
            };
            await _settingsStore.SaveAsync(_settings, _lifetime.Token);
            StatusText = SystemLanguage.Select(
                $"左 {left.FriendlyName} · 右 {right.FriendlyName}",
                $"L {left.FriendlyName} · R {right.FriendlyName}");
            _logger.Info(IsMiPlayMode
                ? SystemLanguage.Select(
                    $"已开始双音箱同步播放：{left.FriendlyName}，{right.FriendlyName}",
                    $"Started synchronized dual-speaker playback: {left.FriendlyName}, {right.FriendlyName}")
                : SystemLanguage.Select(
                    $"已开始左右声道投送：左 {left.FriendlyName}，右 {right.FriendlyName}",
                    $"Started left/right casting: left {left.FriendlyName}, right {right.FriendlyName}"));
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ErrorText = exception is AggregateException aggregate
                ? string.Join(Environment.NewLine, aggregate.InnerExceptions.Select(error => error.Message))
                : exception.Message;
            StatusText = SystemLanguage.Select("左右声道投送启动失败", "Failed to start left/right casting");
            _logger.Error(SystemLanguage.Select(
                "左右声道投送启动失败",
                "Failed to start left/right casting"), exception);
        }
        finally
        {
            IsBusy = false;
            NotifyCastStateChanged();
            _selectionGate.Release();
        }
    }

    private async Task<StartResult[]> StartMiPlayPairAsync(
        RendererItemViewModel first,
        RendererItemViewModel second,
        CaptureSelection originalSelection)
    {
        ILocalOutputLease? firstLocalOutputLease = null;
        ILocalOutputLease? secondLocalOutputLease = null;
        MiPlaySharedAudioSession? sharedCapture = null;
        try
        {
            var captureSelection = originalSelection;
            if (IsSpeakerOnlyPlayback)
            {
                firstLocalOutputLease = await _localOutputs.RouteForCastAsync(
                    originalSelection,
                    _lifetime.Token);
                secondLocalOutputLease = await _localOutputs.RouteForCastAsync(
                    originalSelection,
                    _lifetime.Token);
                if (!Equals(
                        firstLocalOutputLease.CaptureSelection,
                        secondLocalOutputLease.CaptureSelection))
                {
                    throw new InvalidOperationException(
                        "A MiPlay pair must capture one common routed output.");
                }
                captureSelection = firstLocalOutputLease.CaptureSelection;
            }

            sharedCapture = await MiPlaySharedAudioSession.StartAsync(
                _audioCatalog,
                captureSelection,
                participantCount: 2,
                _lifetime.Token);

            var firstTask = StartMiPlayTargetAsync(
                first,
                captureSelection,
                AudioChannelRoute.Stereo,
                sharedAudioSession: sharedCapture,
                preAcquiredLocalOutputLease: firstLocalOutputLease,
                originalSelection: originalSelection,
                waitUntilReady: true);
            var secondTask = StartMiPlayTargetAsync(
                second,
                captureSelection,
                AudioChannelRoute.Stereo,
                sharedAudioSession: sharedCapture,
                preAcquiredLocalOutputLease: secondLocalOutputLease,
                originalSelection: originalSelection,
                waitUntilReady: true);
            firstLocalOutputLease = null;
            secondLocalOutputLease = null;

            var results = await Task.WhenAll(firstTask, secondTask);
            if (results.Any(result => result.Error is not null))
            {
                await sharedCapture.DisposeAsync();
            }
            return results;
        }
        catch
        {
            if (sharedCapture is not null)
            {
                await sharedCapture.DisposeAsync();
            }
            if (firstLocalOutputLease is not null)
            {
                await firstLocalOutputLease.DisposeAsync();
            }
            if (secondLocalOutputLease is not null)
            {
                await secondLocalOutputLease.DisposeAsync();
            }
            throw;
        }
    }

    private async Task StartRendererAsync(RendererItemViewModel renderer)
    {
        if (HasSession(renderer.Udn)) return;

        RefreshNetworkStatus();
        if (!IsPrivateNetwork)
        {
            ErrorText = SystemLanguage.Select(
                "请先把家庭 Wi-Fi 设为“专用网络”，再开始投送。",
                "Set your home Wi-Fi to a Private network before casting.");
            SetRendererSelected(renderer, false);
            return;
        }
        if (SelectedSource is null)
        {
            ErrorText = SystemLanguage.Select("当前没有可用的音频来源。", "No audio source is currently available.");
            SetRendererSelected(renderer, false);
            return;
        }

        IsBusy = true;
        ErrorText = string.Empty;
        try
        {
            CaptureSelection selection = IsProcessMode
                ? new CaptureSelection.Process(SelectedSource.ProcessId!.Value, SelectedSource.DisplayName, true)
                : new CaptureSelection.SystemMix(SelectedSource.Id, SelectedSource.DisplayName);
            var result = IsMiPlayMode
                ? await StartMiPlayTargetAsync(renderer, selection)
                : await StartDlnaTargetAsync(renderer, selection);
            if (result.Error is not null) throw result.Error;

            _settings = _settings with
            {
                LastRendererUdn = renderer.Udn,
                CaptureMode = IsProcessMode ? "Process" : "SystemMix",
                LastSourceId = SelectedSource.Id,
                TransportMode = IsMiPlayMode ? "MiPlay" : "DLNA"
            };
            await _settingsStore.SaveAsync(_settings, _lifetime.Token);
            StatusText = ActiveSessionCount == 1
                ? GetSingleRendererStatusText(renderer)
                : SystemLanguage.Select(
                    $"正在向 {ActiveSessionCount} 台音箱投送",
                    $"Casting to {ActiveSessionCount} speakers");
            _logger.Info(SystemLanguage.Select(
                $"开始投送到 {renderer.FriendlyName}，音源 {SelectedSource.DisplayName}",
                $"Started casting {SelectedSource.DisplayName} to {renderer.FriendlyName}"));
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
            SetRendererSelected(renderer, false);
            _logger.Error(SystemLanguage.Select(
                $"投送到 {renderer.FriendlyName} 启动失败",
                $"Failed to start casting to {renderer.FriendlyName}"), ex);
        }
        finally
        {
            IsBusy = false;
            NotifyCastStateChanged();
        }
    }

    private async Task StopRendererAsync(RendererItemViewModel renderer)
    {
        IsBusy = true;
        try
        {
            DlnaSessionHandle? dlnaSession;
            MiPlaySessionHandle? miPlaySession;
            lock (_sessionGate)
            {
                _dlnaSessions.Remove(renderer.Udn, out dlnaSession);
                _miPlaySessions.Remove(renderer.Udn, out miPlaySession);
            }

            var stopTasks = new List<Task>(2);
            if (dlnaSession is not null) stopTasks.Add(StopAndDisposeAsync(dlnaSession));
            if (miPlaySession is not null) stopTasks.Add(StopAndDisposeAsync(miPlaySession));
            await Task.WhenAll(stopTasks);

            StatusText = ActiveSessionCount == 0
                ? SystemLanguage.Select("投送已停止", "Casting stopped")
                : ActiveSessionCount == 1
                    ? GetSingleRendererStatusText()
                    : SystemLanguage.Select(
                        $"正在向 {ActiveSessionCount} 台音箱投送",
                        $"Casting to {ActiveSessionCount} speakers");
            _logger.Info(SystemLanguage.Select(
                $"已停止向 {renderer.FriendlyName} 投送",
                $"Stopped casting to {renderer.FriendlyName}"));
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
            _logger.Error(SystemLanguage.Select(
                $"停止向 {renderer.FriendlyName} 投送失败",
                $"Failed to stop casting to {renderer.FriendlyName}"), ex);
        }
        finally
        {
            IsBusy = false;
            NotifyCastStateChanged();
        }
    }

    public async Task StopCastingAsync()
    {
        IsBusy = true;
        try
        {
            DlnaSessionHandle[] dlnaSessions;
            MiPlaySessionHandle[] miPlaySessions;
            lock (_sessionGate)
            {
                dlnaSessions = [.. _dlnaSessions.Values];
                miPlaySessions = [.. _miPlaySessions.Values];
            }

            await Task.WhenAll(
                dlnaSessions.Select(StopAndDisposeAsync)
                    .Concat(miPlaySessions.Select(StopAndDisposeAsync)));
            StatusText = SystemLanguage.Select("投送已停止", "Casting stopped");
            _logger.Info(SystemLanguage.Select("投送已停止", "Casting stopped"));
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
            _logger.Error(SystemLanguage.Select("停止投送失败", "Failed to stop casting"), ex);
        }
        finally
        {
            lock (_sessionGate)
            {
                _dlnaSessions.Clear();
                _miPlaySessions.Clear();
            }
            ClearRendererSelection();
            IsBusy = false;
            NotifyCastStateChanged();
        }
    }

    private void RefreshNetworkStatus()
    {
        var status = _networkProfiles.GetStatus();
        IsPrivateNetwork = status.IsPrivate;
        NetworkSummary = status.Summary;
    }

    private static void OpenNetworkSettings() => Process.Start(new ProcessStartInfo("ms-settings:network-status")
    {
        UseShellExecute = true
    });

    private void OnRendererSelectionChanged(object? sender, EventArgs eventArgs)
    {
        if (_suppressSelectionEvents || IsStereoSplitMode || sender is not RendererItemViewModel renderer) return;
        NotifyRendererSelectionChanged();
        _ = ApplyRendererSelectionAsync(renderer);
    }

    private void OnRendererChannelAssignmentChanged(object? sender, EventArgs eventArgs)
    {
        if (_suppressSelectionEvents || !IsStereoSplitMode || sender is not RendererItemViewModel renderer) return;

        _suppressSelectionEvents = true;
        try
        {
            renderer.IsSelected = false;
            if (renderer.IsLeftChannel)
            {
                renderer.IsRightChannel = false;
                foreach (var other in Renderers.Where(item => !ReferenceEquals(item, renderer)))
                {
                    other.IsLeftChannel = false;
                }
            }
            else if (renderer.IsRightChannel)
            {
                renderer.IsLeftChannel = false;
                foreach (var other in Renderers.Where(item => !ReferenceEquals(item, renderer)))
                {
                    other.IsRightChannel = false;
                }
            }
        }
        finally
        {
            _suppressSelectionEvents = false;
        }

        NotifyRendererSelectionChanged();
        var revision = Interlocked.Increment(ref _stereoSelectionRevision);
        _ = ApplyStereoPairAsync(revision);
    }

    private void OnRendererVolumeChanged(object? sender, EventArgs eventArgs)
    {
        if (_updatingStereoMasterVolume || !IsStereoSplitMode) return;
        UpdateStereoMasterVolumeFromChannels();
    }

    private void NotifyRendererSelectionChanged()
    {
        OnPropertyChanged(nameof(HasNoRenderers));
        OnPropertyChanged(nameof(SelectedRendererCount));
        OnPropertyChanged(nameof(RendererSelectionText));
        OnPropertyChanged(nameof(CanAdjustStereoMasterVolume));
        UpdateStereoMasterVolumeFromChannels();
    }

    private void UpdateStereoMasterVolumeFromChannels()
    {
        var left = Renderers.FirstOrDefault(renderer => renderer.IsLeftChannel && renderer.CanControlVolume);
        var right = Renderers.FirstOrDefault(renderer => renderer.IsRightChannel && renderer.CanControlVolume);
        if (left is null || right is null) return;
        SetStereoMasterVolume(StereoVolumeScaler.GetMasterVolume(left.Volume, right.Volume));
    }

    private void SetStereoMasterVolume(double value)
    {
        if (!SetField(ref _stereoMasterVolume, Math.Clamp(value, 0, 100), nameof(StereoMasterVolume))) return;
        OnPropertyChanged(nameof(StereoMasterVolumeText));
    }

    private bool HasSession(string udn)
    {
        lock (_sessionGate)
        {
            return _dlnaSessions.ContainsKey(udn) || _miPlaySessions.ContainsKey(udn);
        }
    }

    private void SetRendererSelected(RendererItemViewModel renderer, bool isSelected)
    {
        _suppressSelectionEvents = true;
        try
        {
            renderer.IsSelected = isSelected;
        }
        finally
        {
            _suppressSelectionEvents = false;
        }
        NotifyRendererSelectionChanged();
    }

    private void ClearRendererSelection()
    {
        _suppressSelectionEvents = true;
        try
        {
            foreach (var renderer in Renderers)
            {
                renderer.IsSelected = false;
                renderer.IsLeftChannel = false;
                renderer.IsRightChannel = false;
            }
        }
        finally
        {
            _suppressSelectionEvents = false;
        }
        NotifyRendererSelectionChanged();
    }

    private void ClearRendererAssignment(RendererItemViewModel renderer)
    {
        _suppressSelectionEvents = true;
        try
        {
            renderer.IsSelected = false;
            renderer.IsLeftChannel = false;
            renderer.IsRightChannel = false;
        }
        finally
        {
            _suppressSelectionEvents = false;
        }
        NotifyRendererSelectionChanged();
    }

    private async Task StopActiveSessionsPreservingAssignmentsAsync()
    {
        DlnaSessionHandle[] dlnaSessions;
        MiPlaySessionHandle[] miPlaySessions;
        lock (_sessionGate)
        {
            dlnaSessions = [.. _dlnaSessions.Values];
            miPlaySessions = [.. _miPlaySessions.Values];
            _dlnaSessions.Clear();
            _miPlaySessions.Clear();
        }

        await Task.WhenAll(
            dlnaSessions.Select(StopAndDisposeAsync)
                .Concat(miPlaySessions.Select(StopAndDisposeAsync)));
    }

    private async Task LoadRendererVolumesAsync(IEnumerable<RendererItemViewModel> renderers)
    {
        var results = await Task.WhenAll(renderers.Select(async renderer =>
        {
            if (!renderer.CanControlVolume) return (Renderer: renderer, Volume: (int?)null);
            try
            {
                var volume = await _controller.GetVolumeAsync(renderer.Device, _lifetime.Token);
                return (Renderer: renderer, Volume: volume);
            }
            catch (Exception exception)
            {
                _logger.Error(SystemLanguage.Select(
                    $"读取 {renderer.FriendlyName} 音量失败",
                    $"Failed to read the volume for {renderer.FriendlyName}"), exception);
                return (Renderer: renderer, Volume: (int?)null);
            }
        }));
        await RunOnUiAsync(() =>
        {
            foreach (var (Renderer, Volume) in results) Renderer.SetInitialVolume(Volume);
            UpdateStereoMasterVolumeFromChannels();
        });
    }

    private async Task<StartResult> StartDlnaTargetAsync(
        RendererItemViewModel renderer,
        CaptureSelection selection,
        AudioChannelRoute channelRoute = AudioChannelRoute.Stereo)
    {
        var coordinator = _coordinatorFactory();
        void handler(object? _, CastDiagnostics diagnostics) =>
            OnDiagnosticsChanged(renderer, diagnostics);
        coordinator.DiagnosticsChanged += handler;
        var handle = new DlnaSessionHandle(renderer, coordinator, handler);
        lock (_sessionGate) _dlnaSessions[renderer.Udn] = handle;
        try
        {
            await coordinator.StartAsync(
                renderer.Device,
                selection,
                _settings.AllowMp3Fallback,
                IsSpeakerOnlyPlayback,
                channelRoute,
                _lifetime.Token);
            return new StartResult(renderer.Udn, renderer.FriendlyName, null);
        }
        catch (Exception exception)
        {
            lock (_sessionGate) _dlnaSessions.Remove(renderer.Udn);
            coordinator.DiagnosticsChanged -= handler;
            await coordinator.DisposeAsync();
            return new StartResult(renderer.Udn, renderer.FriendlyName, exception);
        }
    }

    private async Task<StartResult> StartMiPlayTargetAsync(
        RendererItemViewModel renderer,
        CaptureSelection selection,
        AudioChannelRoute channelRoute = AudioChannelRoute.Stereo,
        MiPlayPairSynchronization? pairSynchronization = null,
        MiPlaySharedAudioSession? sharedAudioSession = null,
        ILocalOutputLease? preAcquiredLocalOutputLease = null,
        CaptureSelection? originalSelection = null,
        bool waitUntilReady = false)
    {
        var transmitter = _miPlayTransmitterFactory();
        void handler(object? _, MiPlayCastDiagnostics diagnostics) =>
            OnMiPlayDiagnosticsChanged(renderer, diagnostics);
        transmitter.DiagnosticsChanged += handler;
        var sessionOriginalSelection = originalSelection ?? selection;
        var localOutputLease = preAcquiredLocalOutputLease;
        try
        {
            if (IsSpeakerOnlyPlayback && localOutputLease is null)
            {
                localOutputLease = await _localOutputs.RouteForCastAsync(selection, _lifetime.Token);
                selection = localOutputLease.CaptureSelection;
            }
            var handle = new MiPlaySessionHandle(
                renderer,
                transmitter,
                handler,
                sessionOriginalSelection,
                localOutputLease);
            lock (_sessionGate) _miPlaySessions[renderer.Udn] = handle;
            var request = new MiPlaySystemAudioRequest(
                renderer.Device,
                selection,
                MiPlayFfmpegLocator.RequireExecutable(),
                channelRoute,
                pairSynchronization,
                sharedAudioSession);
            if (waitUntilReady)
            {
                await transmitter.StartAsync(request, _lifetime.Token);
            }
            else
            {
                await transmitter.BeginStartAsync(request, _lifetime.Token);
            }
            return new StartResult(renderer.Udn, renderer.FriendlyName, null);
        }
        catch (Exception exception)
        {
            pairSynchronization?.Break(exception);
            lock (_sessionGate) _miPlaySessions.Remove(renderer.Udn);
            transmitter.DiagnosticsChanged -= handler;
            try { await transmitter.DisposeAsync(); }
            finally
            {
                if (localOutputLease is not null) await localOutputLease.DisposeAsync();
            }
            var surfacedException = transmitter.Diagnostics.FailureKind == MiPlayCastFailureKind.ReceiverBusy
                ? new InvalidOperationException(
                    transmitter.Diagnostics.LastError ??
                    SystemLanguage.Select("音箱被其他设备占用", "The speaker is in use by another device."),
                    exception)
                : exception;
            return new StartResult(renderer.Udn, renderer.FriendlyName, surfacedException);
        }
    }

    private static async Task StopAndDisposeAsync(DlnaSessionHandle session)
    {
        session.Coordinator.DiagnosticsChanged -= session.Handler;
        try { await session.Coordinator.StopAsync(); }
        finally { await session.Coordinator.DisposeAsync(); }
    }

    private static async Task StopAndDisposeAsync(MiPlaySessionHandle session)
    {
        session.Transmitter.DiagnosticsChanged -= session.Handler;
        try { await session.Transmitter.StopAsync(); }
        finally
        {
            try { await session.Transmitter.DisposeAsync(); }
            finally
            {
                var lease = session.LocalMuteLease;
                session.LocalMuteLease = null;
                if (lease is not null) await lease.DisposeAsync();
            }
        }
    }

    private async Task CleanupFailedMiPlaySessionAsync(RendererItemViewModel renderer)
    {
        try
        {
            await _selectionGate.WaitAsync(_lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return;
        }

        try
        {
            MiPlaySessionHandle? session;
            lock (_sessionGate) _miPlaySessions.Remove(renderer.Udn, out session);
            if (session is not null)
            {
                try
                {
                    await StopAndDisposeAsync(session);
                }
                catch (Exception exception)
                {
                    _logger.Error(SystemLanguage.Select(
                        $"清理 {renderer.FriendlyName} 的失败会话时出错",
                        $"Failed to clean up the failed session for {renderer.FriendlyName}"), exception);
                }
            }

            await RunOnUiAsync(() =>
            {
                StatusText = SystemLanguage.Select(
                    $"{renderer.FriendlyName} 被其他设备占用",
                    $"{renderer.FriendlyName} is in use by another device");
                ErrorText = SystemLanguage.Select(
                    "音箱被其他设备占用",
                    "The speaker is in use by another device.");
                NotifyCastStateChanged();
            });
        }
        finally
        {
            _selectionGate.Release();
        }
    }

    private async Task SetMiPlaySpeakerOnlyPlaybackAsync(
        MiPlaySessionHandle session,
        bool enabled,
        CancellationToken cancellationToken)
    {
        if (enabled)
        {
            if (session.LocalMuteLease is not null) return;
            ILocalOutputLease? lease = null;
            try
            {
                lease = await _localOutputs.RouteForCastAsync(
                    session.OriginalSelection,
                    cancellationToken);
                await session.Transmitter.SetCaptureSelectionAsync(
                    lease.CaptureSelection,
                    cancellationToken);
                session.LocalMuteLease = lease;
            }
            catch
            {
                if (lease is not null) await lease.DisposeAsync();
                throw;
            }
        }
        else
        {
            if (session.LocalMuteLease is null) return;
            await session.Transmitter.SetCaptureSelectionAsync(
                session.OriginalSelection,
                cancellationToken);
            var lease = session.LocalMuteLease;
            session.LocalMuteLease = null;
            await lease.DisposeAsync();
        }
    }

    private int ActiveSessionCount
    {
        get
        {
            lock (_sessionGate)
            {
                return _dlnaSessions.Values.Count(session => session.Coordinator.IsCasting) +
                       _miPlaySessions.Values.Count(session => session.Transmitter.IsActive);
            }
        }
    }

    private (CastDiagnostics[] Dlna, MiPlayCastDiagnostics[] MiPlay) GetActiveSessionDiagnostics()
    {
        lock (_sessionGate)
        {
            return (
                [.. _dlnaSessions.Values
                    .Where(session => session.Coordinator.IsCasting)
                    .Select(session => session.Coordinator.Diagnostics)],
                [.. _miPlaySessions.Values
                    .Where(session => session.Transmitter.IsActive)
                    .Select(session => session.Transmitter.Diagnostics)]);
        }
    }

    private string GetMultiRendererStatusText(int activeSessionCount)
    {
        if (IsStereoSplitMode)
        {
            var left = Renderers.FirstOrDefault(renderer => renderer.IsLeftChannel);
            var right = Renderers.FirstOrDefault(renderer => renderer.IsRightChannel);
            if (left is not null && right is not null)
            {
                return SystemLanguage.Select(
                    $"左 {left.FriendlyName} · 右 {right.FriendlyName}",
                    $"L {left.FriendlyName} · R {right.FriendlyName}");
            }
        }

        return SystemLanguage.Select(
            $"正在向 {activeSessionCount} 台音箱投送",
            $"Casting to {activeSessionCount} speakers");
    }

    private string GetSingleRendererStatusText(RendererItemViewModel? preferredRenderer = null)
    {
        if (preferredRenderer is not null && HasSession(preferredRenderer.Udn))
        {
            return preferredRenderer.FriendlyName;
        }

        return Renderers.FirstOrDefault(renderer => HasSession(renderer.Udn))?.FriendlyName
            ?? preferredRenderer?.FriendlyName
            ?? SystemLanguage.Select("正在投送", "Casting");
    }

    private static string GetAggregateProfileText(
        IReadOnlyList<CastDiagnostics> dlnaDiagnostics,
        IReadOnlyList<MiPlayCastDiagnostics> miPlayDiagnostics)
    {
        if (miPlayDiagnostics.Count > 0 && dlnaDiagnostics.Count == 0) return "MiPlay AAC";
        if (dlnaDiagnostics.Count > 0 && miPlayDiagnostics.Count > 0)
        {
            return SystemLanguage.Select("多协议", "Multiple protocols");
        }

        var profiles = dlnaDiagnostics
            .Select(diagnostics => diagnostics.Profile)
            .Distinct()
            .ToArray();
        if (profiles.Length != 1)
        {
            return SystemLanguage.Select("多种格式", "Multiple formats");
        }

        return profiles[0] switch
        {
            StreamProfile.PcmWave => "PCM / WAV",
            StreamProfile.Mp3Cbr320 => "MP3 320 kbps",
            _ => SystemLanguage.Select("正在连接", "Connecting")
        };
    }

    private static string GetAggregateDiagnosticsText(
        IReadOnlyList<CastDiagnostics> dlnaDiagnostics,
        IReadOnlyList<MiPlayCastDiagnostics> miPlayDiagnostics)
    {
        var activeSessionCount = dlnaDiagnostics.Count + miPlayDiagnostics.Count;
        var maximumBufferedMilliseconds = dlnaDiagnostics
            .Select(diagnostics => diagnostics.BufferedMilliseconds)
            .Concat(miPlayDiagnostics.Select(diagnostics => diagnostics.BufferedMilliseconds))
            .DefaultIfEmpty()
            .Max();
        var overruns = dlnaDiagnostics.Sum(diagnostics => diagnostics.Overruns) +
                       miPlayDiagnostics.Sum(diagnostics => diagnostics.Overruns);
        var underruns = dlnaDiagnostics.Sum(diagnostics => diagnostics.Underruns) +
                        miPlayDiagnostics.Sum(diagnostics => diagnostics.Underruns);

        return SystemLanguage.Select(
            $"{activeSessionCount} 个会话 · 最大缓冲 {maximumBufferedMilliseconds} 毫秒 · 溢出 {overruns} · 欠载 {underruns}",
            $"{activeSessionCount} sessions · Max buffer {maximumBufferedMilliseconds} ms · Overruns {overruns} · Underruns {underruns}");
    }

    private void UpdateSessionDiagnostics(
        RendererItemViewModel renderer,
        string fallbackStatus,
        string singleProfile,
        string singleDiagnostics,
        string? lastError)
    {
        var (dlna, miPlay) = GetActiveSessionDiagnostics();
        var activeSessionCount = dlna.Length + miPlay.Length;
        StatusText = activeSessionCount switch
        {
            > 1 => GetMultiRendererStatusText(activeSessionCount),
            1 => GetSingleRendererStatusText(renderer),
            _ => fallbackStatus
        };
        ProfileText = activeSessionCount > 1
            ? GetAggregateProfileText(dlna, miPlay)
            : singleProfile;
        DiagnosticsText = activeSessionCount > 1
            ? GetAggregateDiagnosticsText(dlna, miPlay)
            : singleDiagnostics;
        ErrorText = lastError ?? ErrorText;
    }

    private void OnDiagnosticsChanged(RendererItemViewModel renderer, CastDiagnostics diagnostics) =>
        _ = RunOnUiAsync(() =>
        {
            var singleProfile = diagnostics.Profile switch
            {
                StreamProfile.PcmWave => "PCM / WAV",
                StreamProfile.Mp3Cbr320 => "MP3 320 kbps",
                _ => SystemLanguage.Select("未连接", "Not connected")
            };
            var singleDiagnostics = SystemLanguage.Select(
                $"应用缓冲 {diagnostics.BufferedMilliseconds} 毫秒（目标 60 毫秒） · 溢出 {diagnostics.Overruns} · 欠载 {diagnostics.Underruns}",
                $"App buffer {diagnostics.BufferedMilliseconds} ms (60 ms target) · Overruns {diagnostics.Overruns} · Underruns {diagnostics.Underruns}");
            UpdateSessionDiagnostics(
                renderer,
                diagnostics.Message,
                singleProfile,
                singleDiagnostics,
                diagnostics.LastError);
            if (diagnostics.State == CastSessionState.Streaming && DateTimeOffset.UtcNow >= _nextDiagnosticsLogAt)
            {
                _logger.Info($"投送诊断：profile={ProfileText}, buffer={diagnostics.BufferedMilliseconds}ms, overruns={diagnostics.Overruns}, underruns={diagnostics.Underruns}");
                _nextDiagnosticsLogAt = DateTimeOffset.UtcNow.AddSeconds(5);
            }
            NotifyCastStateChanged();
        });

    private void OnMiPlayDiagnosticsChanged(RendererItemViewModel renderer, MiPlayCastDiagnostics diagnostics) =>
        _ = RunOnUiAsync(() =>
        {
            var singleProfile = diagnostics.State == MiPlayCastState.Idle
                ? SystemLanguage.Select("未连接", "Not connected")
                : "MiPlay AAC";
            var singleDiagnostics = SystemLanguage.Select(
                $"MiPlay 缓冲 {diagnostics.BufferedMilliseconds} 毫秒 · " +
                $"音频单元 {diagnostics.AccessUnits} · RTP 帧 {diagnostics.RtpFrames} · " +
                $"溢出 {diagnostics.Overruns} · 欠载 {diagnostics.Underruns}",
                $"MiPlay buffer {diagnostics.BufferedMilliseconds} ms · " +
                $"Audio units {diagnostics.AccessUnits} · RTP frames {diagnostics.RtpFrames} · " +
                $"Overruns {diagnostics.Overruns} · Underruns {diagnostics.Underruns}");
            UpdateSessionDiagnostics(
                renderer,
                diagnostics.Message,
                singleProfile,
                singleDiagnostics,
                diagnostics.LastError);
            if (!string.IsNullOrWhiteSpace(diagnostics.ProtocolEvidence))
            {
                _logger.Info($"MiPlay wire evidence: {diagnostics.ProtocolEvidence}");
            }
            if (diagnostics.State == MiPlayCastState.Streaming && DateTimeOffset.UtcNow >= _nextDiagnosticsLogAt)
            {
                _logger.Info(
                    $"MiPlay streaming diagnostics: renderer={renderer.FriendlyName}, " +
                    $"buffer={diagnostics.BufferedMilliseconds}ms, " +
                    $"overruns={diagnostics.Overruns}, underruns={diagnostics.Underruns}, " +
                    $"accessUnits={diagnostics.AccessUnits}, rtpFrames={diagnostics.RtpFrames}, " +
                    $"sendGapMin={diagnostics.MinimumMediaSendGapMilliseconds:F3}ms, " +
                    $"sendGapMax={diagnostics.MaximumMediaSendGapMilliseconds:F3}ms, " +
                    $"lateSends={diagnostics.LateMediaSends}, " +
                    $"catchUpSends={diagnostics.CatchUpMediaSends}");
                _nextDiagnosticsLogAt = DateTimeOffset.UtcNow.AddSeconds(5);
            }
            if (diagnostics.State == MiPlayCastState.Error)
            {
                _logger.Error(SystemLanguage.Select(
                    $"MiPlay 会话失败：{diagnostics.LastError ?? diagnostics.Message}",
                    $"MiPlay session failed: {diagnostics.LastError ?? diagnostics.Message}"));
                if (diagnostics.FailureKind == MiPlayCastFailureKind.ReceiverBusy)
                {
                    ClearRendererAssignment(renderer);
                    _ = CleanupFailedMiPlaySessionAsync(renderer);
                }
            }
            NotifyCastStateChanged();
        });

    private void NotifyCastStateChanged()
    {
        UpdateCastDurationState(IsCasting);
        foreach (var renderer in Renderers) renderer.IsSelectionEnabled = !IsBusy;
        if (!IsCasting) ProfileText = SelectedTransportProfileText;
        OnPropertyChanged(nameof(IsCasting));
        OnPropertyChanged(nameof(CastingStatusVisibility));
        OnPropertyChanged(nameof(InactiveStatusVisibility));
        OnPropertyChanged(nameof(CanUseProcessCapture));
        OnPropertyChanged(nameof(CanChangeTransport));
        OnPropertyChanged(nameof(CanUseSpeakerOnlyPlayback));
        OnPropertyChanged(nameof(CanUseStereoSplit));
        RefreshSourcesCommand.RaiseCanExecuteChanged();
    }

    private void UpdateCastDurationState(bool isCasting)
    {
        if (isCasting)
        {
            if (_castDurationStopwatch.IsRunning) return;
            _castDurationStopwatch.Restart();
            CastDurationText = "00:00";
            _castDurationTimer.Start();
            return;
        }

        if (!_castDurationStopwatch.IsRunning) return;
        _castDurationTimer.Stop();
        _castDurationStopwatch.Reset();
        CastDurationText = "00:00";
    }

    private void OnCastDurationTimerTick(DispatcherQueueTimer sender, object args)
    {
        var elapsed = _castDurationStopwatch.Elapsed;
        var totalHours = (long)elapsed.TotalHours;
        CastDurationText = totalHours > 0
            ? $"{totalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}"
            : $"{elapsed.Minutes:00}:{elapsed.Seconds:00}";
    }

    private async Task SaveSpeakerOnlyPlaybackAsync(AppSettings settings)
    {
        try
        {
            await _settingsStore.SaveAsync(settings, _lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.Error(SystemLanguage.Select(
                "保存仅音箱播放设置失败",
                "Failed to save the speakers-only setting"), exception);
            await RunOnUiAsync(() => ErrorText = exception.Message);
        }
    }

    private async Task PeriodicRefreshAsync(CancellationToken cancellationToken)
    {
        var initialDiscoveryRetry = TimeSpan.FromSeconds(30);
        var maximumDiscoveryRetry = TimeSpan.FromMinutes(5);
        var discoveryRetry = initialDiscoveryRetry;
        var nextDiscoveryRetryAt = DateTimeOffset.UtcNow + discoveryRetry;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                RefreshNetworkStatus();
                if (IsCasting || Renderers.Count > 0)
                {
                    discoveryRetry = initialDiscoveryRetry;
                    nextDiscoveryRetryAt = DateTimeOffset.UtcNow + discoveryRetry;
                    continue;
                }

                var now = DateTimeOffset.UtcNow;
                if (now < nextDiscoveryRetryAt) continue;

                // Recover from a missed startup SSDP response without continuously rescanning
                // a healthy device list. Empty-list retries back off from 30 seconds to 5 minutes.
                await RefreshRenderersAsync();
                discoveryRetry = TimeSpan.FromSeconds(Math.Min(
                    discoveryRetry.TotalSeconds * 2,
                    maximumDiscoveryRetry.TotalSeconds));
                nextDiscoveryRetryAt = DateTimeOffset.UtcNow + discoveryRetry;
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
            completion.TrySetException(new InvalidOperationException(SystemLanguage.Select(
                "WinUI 调度队列已经关闭。",
                "The WinUI dispatcher queue is closed.")));
        }
        return completion.Task;
    }

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        _castDurationTimer.Stop();
        _castDurationTimer.Tick -= OnCastDurationTimerTick;
        if (_periodicRefresh is not null)
        {
            try { await _periodicRefresh; }
            catch (OperationCanceledException) { }
        }
        await StopCastingAsync();
        foreach (var renderer in Renderers)
        {
            renderer.SelectionChanged -= OnRendererSelectionChanged;
            renderer.ChannelAssignmentChanged -= OnRendererChannelAssignmentChanged;
            renderer.VolumeChanged -= OnRendererVolumeChanged;
            renderer.Dispose();
        }
        _lifetime.Dispose();
        _discoveryGate.Dispose();
        _selectionGate.Dispose();
    }

    private sealed record DlnaSessionHandle(
        RendererItemViewModel Renderer,
        CastCoordinator Coordinator,
        EventHandler<CastDiagnostics> Handler);

    private sealed class MiPlaySessionHandle(
        RendererItemViewModel renderer,
        MiPlaySystemAudioTransmitter transmitter,
        EventHandler<MiPlayCastDiagnostics> handler,
        CaptureSelection originalSelection,
        ILocalOutputLease? localMuteLease)
    {
        public RendererItemViewModel Renderer { get; } = renderer;
        public MiPlaySystemAudioTransmitter Transmitter { get; } = transmitter;
        public EventHandler<MiPlayCastDiagnostics> Handler { get; } = handler;
        public CaptureSelection OriginalSelection { get; set; } = originalSelection;
        public ILocalOutputLease? LocalMuteLease { get; set; } = localMuteLease;
    }

    private sealed record StartResult(string Udn, string RendererName, Exception? Error);

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
