using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using DLNACast.App.Commands;
using DLNACast.App.Localization;
using DLNACast.Core.Abstractions;
using DLNACast.Core.Audio;
using DLNACast.Core.Casting;
using DLNACast.Core.Collections;
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
    private readonly BulkObservableCollection<RendererItemViewModel> _renderers = [];
    private readonly IAudioSourceCatalog _audioCatalog;
    private readonly IRendererDiscovery _discovery;
    private readonly Func<CastCoordinator> _coordinatorFactory;
    private readonly Func<MiPlaySystemAudioTransmitter> _miPlayTransmitterFactory;
    private readonly ILocalOutputManager _localOutputs;
    private readonly IRendererController _controller;
    private readonly IAcousticLatencyTester _acousticLatencyTester;
    private readonly AppSettingsStore _settingsStore;
    private readonly AppLogger _logger;
    private readonly DispatcherQueue _dispatcher;
    private readonly NetworkProfileService _networkProfiles = new();
    private readonly SemaphoreSlim _discoveryGate = new(1, 1);
    private readonly SemaphoreSlim _selectionGate = new(1, 1);
    private readonly SemaphoreSlim _settingsSaveGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DispatcherQueueTimer _castDurationTimer;
    private readonly Stopwatch _castDurationStopwatch = new();
    private readonly Lock _sessionGate = new();
    private readonly Dictionary<string, DlnaSessionHandle> _dlnaSessions = [];
    private readonly Dictionary<string, MiPlaySessionHandle> _miPlaySessions = [];
    private readonly HashSet<string> _recoveringMiPlayMembers = [];
    private readonly Dictionary<string, PendingMiPlayRejoin> _pendingMiPlayRejoins = [];
    private MiPlaySharedAudioSession? _activeMiPlaySharedSession;
    private CaptureSelection? _activeMiPlayGroupSelection;
    private string? _activeMiPlayGroupFfmpegPath;
    private AudioSourceItem? _selectedSource;
    private bool _isProcessMode;
    private bool _isMiPlayMode;
    private bool _isStereoSplitMode;
    private bool _isSpeakerOnlyPlayback = true;
    private double _miPlayTimeOffsetMilliseconds = 1_000;
    private bool _isBusy;
    private bool _isPrivateNetwork;
    private string _networkSummary = SystemLanguage.Select("正在检查网络…", "Checking the network…");
    private string _statusText = SystemLanguage.Select("正在初始化…", "Initializing…");
    private string _profileText = "DLNA";
    private string _castDurationText = "00:00";
    private string _speakerPlaybackStatusText = string.Empty;
    private string _diagnosticsText = string.Empty;
    private string _errorText = string.Empty;
    private string _latencyTestText = SystemLanguage.Select("测试延迟", "Test latency");
    private string _latencyTestToolTip = SystemLanguage.Select(
        "播放短测试音，并用电脑默认麦克风测量返回时间",
        "Play a short probe and measure its return through the default PC microphone");
    private AppSettings _settings = new();
    private Task? _periodicRefresh;
    private DateTimeOffset _nextDiagnosticsLogAt;
    private bool _suppressSelectionEvents;
    private bool _updatingStereoMasterVolume;
    private double _stereoMasterVolume = 30;
    private int _stereoSelectionRevision;
    private bool _isInitialized;
    private bool _isLatencyTesting;
    private bool _isMiPlayPlaybackControlBusy;
    private CancellationTokenSource? _latencyTestCancellation;

    public MainViewModel(
        IAudioSourceCatalog audioCatalog,
        IRendererDiscovery discovery,
        Func<CastCoordinator> coordinatorFactory,
        Func<MiPlaySystemAudioTransmitter> miPlayTransmitterFactory,
        ILocalOutputManager localOutputs,
        IRendererController controller,
        AppSettingsStore settingsStore,
        AppLogger logger,
        DispatcherQueue dispatcher,
        IAcousticLatencyTester? acousticLatencyTester = null)
    {
        _audioCatalog = audioCatalog;
        _discovery = discovery;
        _coordinatorFactory = coordinatorFactory;
        _miPlayTransmitterFactory = miPlayTransmitterFactory;
        _localOutputs = localOutputs;
        _controller = controller;
        _acousticLatencyTester = acousticLatencyTester ?? new WindowsAcousticLatencyTester();
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
        TestLatencyCommand = new AsyncRelayCommand(RunLatencyTestAsync, () => CanTestLatency);
        ToggleMiPlayPlaybackCommand = new AsyncRelayCommand(
            ToggleMiPlayPlaybackAsync,
            CanToggleMiPlayPlayback);
    }

    public ObservableCollection<RendererItemViewModel> Renderers => _renderers;
    public ObservableCollection<AudioSourceItem> Sources { get; } = [];
    public UiText Text { get; } = new();
    public AsyncRelayCommand RefreshRenderersCommand { get; }
    public RelayCommand RefreshSourcesCommand { get; }
    public RelayCommand RefreshNetworkCommand { get; }
    public RelayCommand OpenNetworkSettingsCommand { get; }
    public AsyncRelayCommand TestLatencyCommand { get; }
    public AsyncRelayCommand ToggleMiPlayPlaybackCommand { get; }

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
            OnPropertyChanged(nameof(MiPlayTimeOffsetVisibility));
            OnPropertyChanged(nameof(MiPlayPlaybackControlVisibility));
            OnPropertyChanged(nameof(CanChangeMiPlayTimeOffset));
            RaiseMiPlayPlaybackControlStateChanged();
        }
    }

    public bool IsDlnaMode
    {
        get => !IsMiPlayMode;
        set { if (value) IsMiPlayMode = false; }
    }

    public bool CanUseProcessCapture => !IsBusy;
    public bool CanChangeTransport => !IsCasting;
    public bool CanChangeMiPlayTimeOffset => IsMiPlayMode && !IsCasting && !IsBusy;
    public bool CanUseSpeakerOnlyPlayback => !IsBusy;
    public bool CanUseStereoSplit => !IsBusy;
    public Visibility MiPlayTimeOffsetVisibility =>
        IsMiPlayMode ? Visibility.Visible : Visibility.Collapsed;
    public Visibility MiPlayPlaybackControlVisibility =>
        IsMiPlayMode && IsCasting ? Visibility.Visible : Visibility.Collapsed;
    public Visibility AudioSourceSelectorVisibility =>
        IsProcessMode ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NormalRendererListVisibility => IsStereoSplitMode
        ? Visibility.Collapsed
        : Visibility.Visible;
    public Visibility StereoRendererListsVisibility => IsStereoSplitMode
        ? Visibility.Visible
        : Visibility.Collapsed;
    public string LeftChannelText => SystemLanguage.Select("左声道", "Left channel");
    public string RightChannelText => SystemLanguage.Select("右声道", "Right channel");
    public string StereoMasterVolumeText => $"{StereoMasterVolume:F0}%";
    public string StereoMasterVolumeLabel => SystemLanguage.Select("总音量", "Master volume");
    public string StereoMasterVolumeAutomationName => SystemLanguage.Select(
        "总音量",
        "Master volume");

    public double MiPlayTimeOffsetMilliseconds
    {
        get => _miPlayTimeOffsetMilliseconds;
        set
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || IsCasting || IsBusy)
            {
                OnPropertyChanged();
                return;
            }

            var minimumMilliseconds =
                MiPlayProtocolConstants.MinimumConfigurablePlaybackDelayMicroseconds / 1_000;
            var maximumMilliseconds =
                MiPlayProtocolConstants.MaximumConfigurablePlaybackDelayMicroseconds / 1_000;
            var normalized = Math.Clamp(
                (int)Math.Round(value, MidpointRounding.AwayFromZero),
                minimumMilliseconds,
                maximumMilliseconds);
            if (Math.Abs(_miPlayTimeOffsetMilliseconds - normalized) < double.Epsilon) return;

            _miPlayTimeOffsetMilliseconds = normalized;
            OnPropertyChanged();
            _settings = _settings with { MiPlayTimeOffsetMilliseconds = normalized };
            if (_isInitialized) _ = SaveMiPlayTimeOffsetAsync(_settings);
        }
    }
    public bool CanAdjustStereoMasterVolume
    {
        get
        {
            var assigned = GetVolumeTargets();
            if (assigned.Length == 0 || assigned.Any(renderer => !renderer.CanControlVolume)) return false;
            return !IsStereoSplitMode ||
                   (assigned.Any(renderer => renderer.IsLeftChannel) &&
                    assigned.Any(renderer => renderer.IsRightChannel));
        }
    }

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

            var assigned = GetVolumeTargets();
            var currentMaster = assigned.Max(renderer => renderer.Volume);
            var scale = currentMaster <= double.Epsilon ? 0 : requested / currentMaster;

            _updatingStereoMasterVolume = true;
            try
            {
                SetStereoMasterVolume(requested);
                foreach (var renderer in assigned)
                {
                    renderer.Volume = currentMaster <= double.Epsilon
                        ? requested
                        : Math.Clamp(renderer.Volume * scale, 0, 100);
                }
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
            TestLatencyCommand.RaiseCanExecuteChanged();
            ToggleMiPlayPlaybackCommand.RaiseCanExecuteChanged();
            foreach (var renderer in Renderers) renderer.IsSelectionEnabled = !value;
            OnPropertyChanged(nameof(CanUseProcessCapture));
            OnPropertyChanged(nameof(CanChangeMiPlayTimeOffset));
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
    public bool IsLatencyTesting
    {
        get => _isLatencyTesting;
        private set
        {
            if (!SetField(ref _isLatencyTesting, value)) return;
            OnPropertyChanged(nameof(LatencyTestIconVisibility));
            OnPropertyChanged(nameof(LatencyTestProgressVisibility));
            TestLatencyCommand.RaiseCanExecuteChanged();
        }
    }
    public bool CanTestLatency => IsLatencyTestSessionReady && !IsBusy && !IsLatencyTesting;

    private bool IsLatencyTestSessionReady
    {
        get
        {
            lock (_sessionGate)
            {
                var hasActiveSession = false;
                foreach (var session in _dlnaSessions.Values)
                {
                    if (!session.Coordinator.IsCasting) continue;
                    hasActiveSession = true;
                    if (session.Coordinator.Diagnostics.State != CastSessionState.Streaming)
                    {
                        return false;
                    }
                }

                foreach (var session in _miPlaySessions.Values)
                {
                    if (!session.Transmitter.IsActive) continue;
                    hasActiveSession = true;
                    if (session.Transmitter.Diagnostics.State != MiPlayCastState.Streaming)
                    {
                        return false;
                    }
                }

                return hasActiveSession;
            }
        }
    }
    public string LatencyTestText
    {
        get => _latencyTestText;
        private set => SetField(ref _latencyTestText, value);
    }
    public string LatencyTestToolTip
    {
        get => _latencyTestToolTip;
        private set => SetField(ref _latencyTestToolTip, value);
    }
    public Visibility LatencyTestIconVisibility => IsLatencyTesting
        ? Visibility.Collapsed
        : Visibility.Visible;
    public Visibility LatencyTestProgressVisibility => IsLatencyTesting
        ? Visibility.Visible
        : Visibility.Collapsed;
    public bool IsMiPlayPlaybackControlBusy
    {
        get => _isMiPlayPlaybackControlBusy;
        private set
        {
            if (!SetField(ref _isMiPlayPlaybackControlBusy, value)) return;
            OnPropertyChanged(nameof(MiPlayPlaybackControlIconVisibility));
            OnPropertyChanged(nameof(MiPlayPlaybackControlProgressVisibility));
            ToggleMiPlayPlaybackCommand.RaiseCanExecuteChanged();
        }
    }
    public string MiPlayPlaybackControlText => AreMiPlayReceiversPaused
        ? SystemLanguage.Select("继续", "Resume")
        : SystemLanguage.Select("暂停", "Pause");
    public string MiPlayPlaybackControlToolTip => AreMiPlayReceiversPaused
        ? SystemLanguage.Select(
            "继续当前 MiPlay 音箱播放",
            "Resume the current MiPlay speakers")
        : SystemLanguage.Select(
            "暂停当前 MiPlay 音箱播放",
            "Pause the current MiPlay speakers");
    public string MiPlayPlaybackControlGlyph => AreMiPlayReceiversPaused ? "\uE768" : "\uE769";
    public Visibility MiPlayPlaybackControlIconVisibility => IsMiPlayPlaybackControlBusy
        ? Visibility.Collapsed
        : Visibility.Visible;
    public Visibility MiPlayPlaybackControlProgressVisibility => IsMiPlayPlaybackControlBusy
        ? Visibility.Visible
        : Visibility.Collapsed;
    public string CastDurationText
    {
        get => _castDurationText;
        private set => SetField(ref _castDurationText, value);
    }
    public string SpeakerPlaybackStatusText
    {
        get => _speakerPlaybackStatusText;
        private set
        {
            if (!SetField(ref _speakerPlaybackStatusText, value)) return;
            OnPropertyChanged(nameof(SpeakerPlaybackStatusVisibility));
        }
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
    public Visibility SpeakerPlaybackStatusVisibility =>
        string.IsNullOrWhiteSpace(SpeakerPlaybackStatusText)
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
                var leftCount = Renderers.Count(renderer => renderer.IsLeftChannel);
                var rightCount = Renderers.Count(renderer => renderer.IsRightChannel);
                if (leftCount == 0)
                {
                    return SystemLanguage.Select(
                        "请为左声道选择至少一台音箱",
                        "Select at least one speaker for the left channel");
                }
                if (rightCount == 0)
                {
                    return SystemLanguage.Select(
                        "请为右声道选择至少一台音箱",
                        "Select at least one speaker for the right channel");
                }
                return SystemLanguage.Select(
                    $"左声道 {leftCount} 台 · 右声道 {rightCount} 台",
                    $"Left {leftCount} · Right {rightCount}");
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
        _miPlayTimeOffsetMilliseconds = Math.Clamp(
            _settings.MiPlayTimeOffsetMilliseconds,
            MiPlayProtocolConstants.MinimumConfigurablePlaybackDelayMicroseconds / 1_000,
            MiPlayProtocolConstants.MaximumConfigurablePlaybackDelayMicroseconds / 1_000);
        _settings = _settings with
        {
            MiPlayTimeOffsetMilliseconds = (int)_miPlayTimeOffsetMilliseconds,
        };
        OnPropertyChanged(nameof(IsSpeakerOnlyPlayback));
        OnPropertyChanged(nameof(MiPlayTimeOffsetMilliseconds));
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
            var leftUdns = Renderers
                .Where(renderer => renderer.IsLeftChannel)
                .Select(renderer => renderer.Udn)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var rightUdns = Renderers
                .Where(renderer => renderer.IsRightChannel)
                .Select(renderer => renderer.Udn)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var devices = await _discovery.SearchAsync(_lifetime.Token);
            RendererItemViewModel[] items = [];
            await RunOnUiAsync(() =>
            {
                var replacement = new List<RendererItemViewModel>(devices.Count);
                foreach (var device in devices)
                {
                    var item = new RendererItemViewModel(device, _controller, _logger, _lifetime.Token)
                    {
                        IsSelected = !IsStereoSplitMode && selectedUdns.Contains(device.Udn),
                        IsLeftChannel = IsStereoSplitMode && leftUdns.Contains(device.Udn),
                        IsRightChannel = IsStereoSplitMode && rightUdns.Contains(device.Udn),
                        IsStereoMode = IsStereoSplitMode,
                        IsSelectionEnabled = !IsBusy,
                    };
                    item.SelectionChanged += OnRendererSelectionChanged;
                    item.ChannelAssignmentChanged += OnRendererChannelAssignmentChanged;
                    item.VolumeChanged += OnRendererVolumeChanged;
                    replacement.Add(item);
                }

                foreach (var renderer in Renderers)
                {
                    renderer.SelectionChanged -= OnRendererSelectionChanged;
                    renderer.ChannelAssignmentChanged -= OnRendererChannelAssignmentChanged;
                    renderer.VolumeChanged -= OnRendererVolumeChanged;
                    renderer.Dispose();
                }
                _renderers.ReplaceAll(replacement);
                items = [.. replacement];
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

    private async Task ApplyStereoGroupAsync(int revision)
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

            var leftRenderers = Renderers.Where(renderer => renderer.IsLeftChannel).ToArray();
            var rightRenderers = Renderers.Where(renderer => renderer.IsRightChannel).ToArray();
            if (leftRenderers.Length == 0 || rightRenderers.Length == 0)
            {
                StatusText = SystemLanguage.Select(
                    "左、右声道都需要选择至少一台音箱",
                    "Select at least one speaker for both the left and right channels");
                return;
            }

            var targets = leftRenderers
                .Select(renderer => new StereoTarget(renderer, AudioChannelRoute.LeftAsMono))
                .Concat(rightRenderers.Select(renderer =>
                    new StereoTarget(renderer, AudioChannelRoute.RightAsMono)))
                .ToArray();
            if (targets.Length > 63)
            {
                ErrorText = SystemLanguage.Select(
                    "左右声道合计最多支持 63 台音箱。",
                    "The left and right channel groups support up to 63 speakers in total.");
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
            StatusText = SystemLanguage.Select(
                $"正在启动左声道 {leftRenderers.Length} 台、右声道 {rightRenderers.Length} 台音箱…",
                $"Starting {leftRenderers.Length} left-channel and {rightRenderers.Length} right-channel speakers…");
            CaptureSelection selection = IsProcessMode
                ? new CaptureSelection.Process(SelectedSource.ProcessId!.Value, SelectedSource.DisplayName, true)
                : new CaptureSelection.SystemMix(SelectedSource.Id, SelectedSource.DisplayName);
            StartResult[] results;
            var updatedRunningMiPlayGroup = false;
            MiPlaySharedAudioSession? activeSharedSession;
            lock (_sessionGate)
            {
                activeSharedSession = _activeMiPlaySharedSession;
            }
            if (IsMiPlayMode && activeSharedSession is not null && ActiveSessionCount > 0)
            {
                updatedRunningMiPlayGroup = true;
                results = await UpdateMiPlayGroupAsync(
                    activeSharedSession,
                    targets,
                    selection);
            }
            else if (IsMiPlayMode)
            {
                if (ActiveSessionCount > 0)
                {
                    await StopActiveSessionsPreservingAssignmentsAsync();
                }
                results = await StartMiPlayGroupAsync(targets, selection);
            }
            else
            {
                if (ActiveSessionCount > 0)
                {
                    await StopActiveSessionsPreservingAssignmentsAsync();
                }
                results = await Task.WhenAll(targets.Select(target =>
                    StartDlnaTargetAsync(target.Renderer, selection, target.ChannelRoute)));
            }
            var failures = results.Where(result => result.Error is not null).ToArray();
            if (failures.Length > 0)
            {
                if (!updatedRunningMiPlayGroup)
                {
                    await StopActiveSessionsPreservingAssignmentsAsync();
                }
                throw new AggregateException(failures.Select(result => result.Error!));
            }

            _settings = _settings with
            {
                LastRendererUdn = targets[0].Renderer.Udn,
                CaptureMode = IsProcessMode ? "Process" : "SystemMix",
                LastSourceId = SelectedSource.Id,
                TransportMode = IsMiPlayMode ? "MiPlay" : "DLNA"
            };
            await _settingsStore.SaveAsync(_settings, _lifetime.Token);
            StatusText = SystemLanguage.Select(
                $"立体声播放：左 {leftRenderers.Length} 台 · 右 {rightRenderers.Length} 台",
                $"Stereo playback: left {leftRenderers.Length} · right {rightRenderers.Length}");
            _logger.Info(SystemLanguage.Select(
                $"已开始多音箱立体声播放：左声道 {string.Join("、", leftRenderers.Select(renderer => renderer.FriendlyName))}；右声道 {string.Join("、", rightRenderers.Select(renderer => renderer.FriendlyName))}",
                $"Started multi-speaker stereo playback: left {string.Join(", ", leftRenderers.Select(renderer => renderer.FriendlyName))}; right {string.Join(", ", rightRenderers.Select(renderer => renderer.FriendlyName))}"));
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ErrorText = exception is AggregateException aggregate
                ? string.Join(Environment.NewLine, aggregate.InnerExceptions.Select(error => error.Message))
                : exception.Message;
            StatusText = SystemLanguage.Select("立体声播放启动失败", "Failed to start stereo playback");
            _logger.Error(SystemLanguage.Select(
                "立体声播放启动失败",
                "Failed to start stereo playback"), exception);
        }
        finally
        {
            IsBusy = false;
            NotifyCastStateChanged();
            _selectionGate.Release();
        }
    }

    private async Task<StartResult[]> UpdateMiPlayGroupAsync(
        MiPlaySharedAudioSession sharedSession,
        IReadOnlyList<StereoTarget> targets,
        CaptureSelection originalSelection)
    {
        var targetsByUdn = targets.ToDictionary(target => target.Renderer.Udn);
        MiPlaySessionHandle[] current;
        string? ffmpegPath;
        lock (_sessionGate)
        {
            current = [.. _miPlaySessions.Values.Where(session =>
                ReferenceEquals(session.SharedAudioSession, sharedSession))];
            _activeMiPlayGroupSelection = originalSelection;
            ffmpegPath = _activeMiPlayGroupFfmpegPath;
        }

        if (ffmpegPath is null)
        {
            throw new InvalidOperationException(
                "The active MiPlay group does not have an AAC encoder path.");
        }

        var results = new List<StartResult>();
        var currentByUdn = current.ToDictionary(session => session.Renderer.Udn);
        var pureDepartures = current
            .Where(session => !targetsByUdn.ContainsKey(session.Renderer.Udn))
            .ToList();

        async Task RemoveMemberAsync(MiPlaySessionHandle session)
        {
            lock (_sessionGate)
            {
                if (_miPlaySessions.TryGetValue(session.Renderer.Udn, out var active) &&
                    ReferenceEquals(active, session))
                {
                    _miPlaySessions.Remove(session.Renderer.Udn);
                }
            }
            currentByUdn.Remove(session.Renderer.Udn);
            pureDepartures.Remove(session);
            await StopAndDisposeAsync(session).ConfigureAwait(false);
        }

        async Task<bool> AddMemberAsync(StereoTarget target)
        {
            var result = await StartMiPlayTargetAsync(
                target.Renderer,
                originalSelection,
                target.ChannelRoute,
                sharedAudioSession: sharedSession,
                originalSelection: originalSelection,
                ffmpegPath: ffmpegPath,
                waitUntilReady: true).ConfigureAwait(false);
            results.Add(result);
            if (result.Error is not null)
            {
                return false;
            }
            lock (_sessionGate)
            {
                currentByUdn[target.Renderer.Udn] = _miPlaySessions[target.Renderer.Udn];
                _pendingMiPlayRejoins.Remove(target.Renderer.Udn);
            }
            return true;
        }

        lock (_sessionGate)
        {
            foreach (var pendingUdn in _pendingMiPlayRejoins.Keys
                         .Where(udn => !targetsByUdn.ContainsKey(udn))
                         .ToArray())
            {
                _pendingMiPlayRejoins.Remove(pendingUdn);
            }
        }

        // The tablet serializes native addMirror/deleteMirror on one Handler.
        // Do the same here so each new member receives one explicit future AU
        // boundary and packetizer snapshot before membership changes again.
        foreach (var target in targets.Where(target => !currentByUdn.ContainsKey(target.Renderer.Udn)))
        {
            if (currentByUdn.Count >= 63)
            {
                var slot = pureDepartures.FirstOrDefault() ??
                    throw new InvalidOperationException(
                        "The active MiPlay group has no membership slot for a new receiver.");
                await RemoveMemberAsync(slot).ConfigureAwait(false);
            }
            if (!await AddMemberAsync(target).ConfigureAwait(false))
            {
                return [.. results];
            }
        }

        // Route changes cannot overlap for the same receiver. Remove and add
        // one member at a time so a two-speaker L/R swap never drops the shared
        // session to zero subscribers and restarts capture.
        foreach (var target in targets)
        {
            if (!currentByUdn.TryGetValue(target.Renderer.Udn, out var existing) ||
                existing.ChannelRoute == target.ChannelRoute)
            {
                continue;
            }
            await RemoveMemberAsync(existing).ConfigureAwait(false);
            if (!await AddMemberAsync(target).ConfigureAwait(false))
            {
                return [.. results];
            }
        }

        // Tablet stopSingleMirror removes only the selected native sender when
        // other members remain. Subscription disposal releases the current
        // AAC/send barriers without restarting capture or route encoders.
        foreach (var departed in pureDepartures.ToArray())
        {
            await RemoveMemberAsync(departed).ConfigureAwait(false);
        }

        return results.Count == 0
            ? targets.Select(target =>
                new StartResult(target.Renderer.Udn, target.Renderer.FriendlyName, null)).ToArray()
            : [.. results];
    }

    private async Task<StartResult[]> StartMiPlayGroupAsync(
        IReadOnlyList<StereoTarget> targets,
        CaptureSelection originalSelection)
    {
        var localOutputLeases = new ILocalOutputLease?[targets.Count];
        MiPlaySharedAudioSession? sharedCapture = null;
        try
        {
            var captureSelection = originalSelection;
            if (IsSpeakerOnlyPlayback)
            {
                for (var index = 0; index < targets.Count; index++)
                {
                    localOutputLeases[index] = await _localOutputs.RouteForCastAsync(
                        originalSelection,
                        _lifetime.Token);
                }
                captureSelection = localOutputLeases[0]!.CaptureSelection;
                if (localOutputLeases.Any(lease =>
                        lease is null || !Equals(lease.CaptureSelection, captureSelection)))
                {
                    throw new InvalidOperationException(
                        "A MiPlay stereo group must capture one common routed output.");
                }
            }

            var ffmpegPath = MiPlayFfmpegLocator.RequireExecutable();
            sharedCapture = await MiPlaySharedAudioSession.StartAsync(
                _audioCatalog,
                captureSelection,
                targets.Select(target => target.ChannelRoute).ToArray(),
                ffmpegPath,
                _lifetime.Token);

            var startTasks = new Task<StartResult>[targets.Count];
            for (var index = 0; index < targets.Count; index++)
            {
                startTasks[index] = StartMiPlayTargetAsync(
                    targets[index].Renderer,
                    captureSelection,
                    targets[index].ChannelRoute,
                    sharedAudioSession: sharedCapture,
                    preAcquiredLocalOutputLease: localOutputLeases[index],
                    originalSelection: originalSelection,
                    ffmpegPath: ffmpegPath,
                    waitUntilReady: true);
                localOutputLeases[index] = null;
            }

            var results = await Task.WhenAll(startTasks);
            if (results.Any(result => result.Error is not null))
            {
                await sharedCapture.DisposeAsync();
            }
            else
            {
                lock (_sessionGate)
                {
                    _activeMiPlaySharedSession = sharedCapture;
                    _activeMiPlayGroupSelection = originalSelection;
                    _activeMiPlayGroupFfmpegPath = ffmpegPath;
                }
            }
            return results;
        }
        catch
        {
            if (sharedCapture is not null)
            {
                await sharedCapture.DisposeAsync();
            }
            foreach (var localOutputLease in localOutputLeases)
            {
                if (localOutputLease is not null)
                {
                    await localOutputLease.DisposeAsync();
                }
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

    private async Task RunLatencyTestAsync()
    {
        if (!CanTestLatency) return;

        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        cancellation.CancelAfter(TimeSpan.FromSeconds(12));
        if (Interlocked.CompareExchange(ref _latencyTestCancellation, cancellation, null) is not null)
        {
            cancellation.Dispose();
            return;
        }

        IsLatencyTesting = true;
        LatencyTestText = SystemLanguage.Select("测试中…", "Testing…");
        LatencyTestToolTip = SystemLanguage.Select(
            "请保持房间安静，等待音箱播放短测试音",
            "Keep the room quiet and wait for the speaker to play the short probe");
        ErrorText = string.Empty;
        try
        {
            var measurement = await _acousticLatencyTester.MeasureAsync(
                EmitLatencyProbeAsync,
                cancellation.Token);
            var milliseconds = Math.Max(0, (int)Math.Round(measurement.Latency.TotalMilliseconds));
            LatencyTestText = SystemLanguage.Select(
                $"延迟 {milliseconds} ms",
                $"Latency {milliseconds} ms");
            LatencyTestToolTip = SystemLanguage.Select(
                $"电脑默认麦克风检测到测试音，声学延迟约 {milliseconds} 毫秒",
                $"The default PC microphone detected the probe; acoustic latency is about {milliseconds} ms");
            _logger.Info(SystemLanguage.Select(
                $"声学延迟测试完成：{milliseconds} 毫秒，相关度 {measurement.Confidence:F3}，" +
                $"检测方式={(measurement.UsedWakeToneFallback ? "提示音回退" : "探针相关")}",
                $"Acoustic latency test completed: {milliseconds} ms, correlation {measurement.Confidence:F3}, " +
                $"method={(measurement.UsedWakeToneFallback ? "wake fallback" : "probe correlation")}"));
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (!_lifetime.IsCancellationRequested && IsCasting)
            {
                ErrorText = SystemLanguage.Select(
                    "延迟测试超时，请确认电脑麦克风可用并调高音箱音量。",
                    "The latency test timed out. Check the PC microphone and raise the speaker volume.");
            }
            ResetLatencyTestDisplay();
        }
        catch (Exception exception)
        {
            ErrorText = GetLatencyTestError(exception);
            ResetLatencyTestDisplay();
            _logger.Error(SystemLanguage.Select(
                "声学延迟测试失败",
                "Acoustic latency test failed"), exception);
        }
        finally
        {
            if (ReferenceEquals(
                    Interlocked.CompareExchange(ref _latencyTestCancellation, null, cancellation),
                    cancellation))
            {
                cancellation.Dispose();
            }
            IsLatencyTesting = false;
        }
    }

    private async Task<long> EmitLatencyProbeAsync(CancellationToken cancellationToken)
    {
        DlnaSessionHandle[] dlnaSessions;
        MiPlaySessionHandle[] miPlaySessions;
        lock (_sessionGate)
        {
            dlnaSessions = [.. _dlnaSessions.Values.Where(session =>
                session.Coordinator.Diagnostics.State == CastSessionState.Streaming)];
            miPlaySessions = [.. _miPlaySessions.Values.Where(session =>
                session.Transmitter.Diagnostics.State == MiPlayCastState.Streaming)];
        }

        if (miPlaySessions.Length > 0)
        {
            // A multi-speaker MiPlay group shares its source PCM buffer, so one
            // scheduled probe reaches every participant at the same media point.
            // Validate every receiver without rewriting the active media clock.
            await Task.WhenAll(miPlaySessions.Skip(1).Select(session =>
                session.Transmitter.PrepareLatencyProbeAsync(cancellationToken)));
            return await miPlaySessions[0].Transmitter
                .StartLatencyProbeAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        if (dlnaSessions.Length > 0)
        {
            var starts = await Task.WhenAll(dlnaSessions.Select(session =>
                session.Coordinator.StartLatencyProbeAsync(cancellationToken)));
            return starts.Min();
        }

        throw new InvalidOperationException(SystemLanguage.Select(
            "没有正在运行的投送会话。",
            "There is no active cast session."));
    }

    private static string GetLatencyTestError(Exception exception)
    {
        if (exception.Message.Contains("只能在 MiPlay 正在投送时", StringComparison.OrdinalIgnoreCase) ||
            exception.Message.Contains("Latency can be tested only while", StringComparison.OrdinalIgnoreCase) ||
            exception.Message.Contains("must be active before starting a latency probe", StringComparison.OrdinalIgnoreCase) ||
            exception.Message.Contains("没有正在运行的投送会话", StringComparison.OrdinalIgnoreCase) ||
            exception.Message.Contains("There is no active cast session", StringComparison.OrdinalIgnoreCase))
        {
            return SystemLanguage.Select(
                "音箱仍在连接或投送已结束，请等待状态显示正在投送后重试。",
                "The speaker is still connecting or the cast has ended. Wait until the status shows streaming and retry.");
        }
        if (exception is TimeoutException)
        {
            return SystemLanguage.Select(
                "未收到麦克风数据，请检查 Windows 麦克风权限和默认输入设备。",
                "No microphone data was received. Check Windows microphone permission and the default input device.");
        }
        if (exception.Message.Contains("did not detect", StringComparison.OrdinalIgnoreCase))
        {
            return SystemLanguage.Select(
                "麦克风未检测到测试音，请保持安静、靠近音箱并调高音量后重试。",
                "The microphone did not detect the probe. Move closer, keep the room quiet, and raise the volume before retrying.");
        }
        return SystemLanguage.Select(
            "无法使用电脑默认麦克风，请检查麦克风权限和输入设备。",
            "The default PC microphone is unavailable. Check microphone permission and the input device.");
    }

    private void ResetLatencyTestDisplay()
    {
        LatencyTestText = SystemLanguage.Select("测试延迟", "Test latency");
        LatencyTestToolTip = SystemLanguage.Select(
            "播放短测试音，并用电脑默认麦克风测量返回时间",
            "Play a short probe and measure its return through the default PC microphone");
    }

    public async Task StopCastingAsync()
    {
        _latencyTestCancellation?.Cancel();
        IsBusy = true;
        try
        {
            DlnaSessionHandle[] dlnaSessions;
            MiPlaySessionHandle[] miPlaySessions;
            MiPlaySharedAudioSession? sharedSession;
            lock (_sessionGate)
            {
                dlnaSessions = [.. _dlnaSessions.Values];
                miPlaySessions = [.. _miPlaySessions.Values];
                sharedSession = _activeMiPlaySharedSession;
            }

            await Task.WhenAll(
                dlnaSessions.Select(StopAndDisposeAsync)
                    .Concat(miPlaySessions.Select(StopAndDisposeAsync)));
            if (sharedSession is not null)
            {
                await sharedSession.DisposeAsync();
            }
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
                _recoveringMiPlayMembers.Clear();
                _pendingMiPlayRejoins.Clear();
                _activeMiPlaySharedSession = null;
                _activeMiPlayGroupSelection = null;
                _activeMiPlayGroupFfmpegPath = null;
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
        if (_suppressSelectionEvents || sender is not RendererItemViewModel renderer) return;
        if (IsStereoSplitMode) return;
        NotifyRendererSelectionChanged();
        _ = ApplyRendererSelectionAsync(renderer);
    }

    private void OnRendererChannelAssignmentChanged(object? sender, EventArgs eventArgs)
    {
        if (_suppressSelectionEvents ||
            !IsStereoSplitMode ||
            sender is not RendererItemViewModel renderer)
        {
            return;
        }

        _suppressSelectionEvents = true;
        try
        {
            renderer.IsSelected = false;
        }
        finally
        {
            _suppressSelectionEvents = false;
        }

        NotifyRendererSelectionChanged();
        var revision = Interlocked.Increment(ref _stereoSelectionRevision);
        _ = ApplyStereoGroupAsync(revision);
    }

    private void OnRendererVolumeChanged(object? sender, EventArgs eventArgs)
    {
        if (_updatingStereoMasterVolume) return;
        UpdateStereoMasterVolumeFromSelection();
    }

    private void NotifyRendererSelectionChanged()
    {
        OnPropertyChanged(nameof(HasNoRenderers));
        OnPropertyChanged(nameof(SelectedRendererCount));
        OnPropertyChanged(nameof(RendererSelectionText));
        OnPropertyChanged(nameof(CanAdjustStereoMasterVolume));
        UpdateStereoMasterVolumeFromSelection();
    }

    private void UpdateStereoMasterVolumeFromSelection()
    {
        var selected = GetVolumeTargets()
            .Where(renderer => renderer.CanControlVolume)
            .ToArray();
        if (selected.Length == 0) return;
        SetStereoMasterVolume(selected.Max(renderer => renderer.Volume));
    }

    private RendererItemViewModel[] GetVolumeTargets() =>
        [.. Renderers.Where(renderer => IsStereoSplitMode
            ? renderer.IsLeftChannel || renderer.IsRightChannel
            : renderer.IsSelected)];

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
        MiPlaySharedAudioSession? sharedSession;
        lock (_sessionGate)
        {
            dlnaSessions = [.. _dlnaSessions.Values];
            miPlaySessions = [.. _miPlaySessions.Values];
            _dlnaSessions.Clear();
            _miPlaySessions.Clear();
            _recoveringMiPlayMembers.Clear();
            _pendingMiPlayRejoins.Clear();
            sharedSession = _activeMiPlaySharedSession;
            _activeMiPlaySharedSession = null;
            _activeMiPlayGroupSelection = null;
            _activeMiPlayGroupFfmpegPath = null;
        }

        await Task.WhenAll(
            dlnaSessions.Select(StopAndDisposeAsync)
                .Concat(miPlaySessions.Select(StopAndDisposeAsync)));
        if (sharedSession is not null)
        {
            await sharedSession.DisposeAsync();
        }
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
            UpdateStereoMasterVolumeFromSelection();
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
        string? ffmpegPath = null,
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
                localOutputLease,
                channelRoute,
                sharedAudioSession);
            lock (_sessionGate) _miPlaySessions[renderer.Udn] = handle;
            var request = new MiPlaySystemAudioRequest(
                renderer.Device,
                selection,
                ffmpegPath ?? MiPlayFfmpegLocator.RequireExecutable(),
                channelRoute,
                pairSynchronization,
                sharedAudioSession,
                PlaybackDelayMicroseconds: checked((int)_miPlayTimeOffsetMilliseconds * 1_000));
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
            var leftCount = Renderers.Count(renderer => renderer.IsLeftChannel && HasSession(renderer.Udn));
            var rightCount = Renderers.Count(renderer => renderer.IsRightChannel && HasSession(renderer.Udn));
            return SystemLanguage.Select(
                $"立体声播放：左 {leftCount} 台 · 右 {rightCount} 台",
                $"Stereo playback: left {leftCount} · right {rightCount}");
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

    private void OnMiPlayDiagnosticsChanged(RendererItemViewModel renderer, MiPlayCastDiagnostics diagnostics)
    {
        lock (_sessionGate)
        {
            if (_miPlaySessions.TryGetValue(renderer.Udn, out var session))
            {
                session.Diagnostics = diagnostics;
            }
        }

        _ = RunOnUiAsync(() =>
        {
            SpeakerPlaybackStatusText = GetSpeakerPlaybackStatusText();
            RaiseMiPlayPlaybackControlStateChanged();
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
                    $"catchUpSends={diagnostics.CatchUpMediaSends}, " +
                    $"timelineRebases={diagnostics.MediaTimelineRebases}, " +
                    $"interReceiverSkewMax=" +
                    $"{diagnostics.MaximumInterReceiverSendSkewMilliseconds:F3}ms, " +
                    $"aacFifo={diagnostics.BufferedAccessUnits}, " +
                    $"aacStarvations={diagnostics.AccessUnitBufferStarvations}, " +
                    $"encodedAgeMin={diagnostics.MinimumEncodedAccessUnitAgeMilliseconds:F3}ms, " +
                    $"encodedAgeMax={diagnostics.MaximumEncodedAccessUnitAgeMilliseconds:F3}ms, " +
                    $"receiverLatencyMs={diagnostics.ReceiverReportedLatencyMilliseconds?.ToString() ?? "unknown"}, " +
                    $"receiverRtpPackets={diagnostics.ReceiverReportedRtpPacketCount?.ToString() ?? "unknown"}, " +
                    $"receiverBitrate={diagnostics.ReceiverReportedBitrate?.ToString() ?? "unknown"}");
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
                else if (diagnostics.FailureKind == MiPlayCastFailureKind.TransportStall)
                {
                    _ = RecoverStalledMiPlayMemberAsync(renderer);
                }
            }
            NotifyCastStateChanged();
        });
    }

    private string GetSpeakerPlaybackStatusText()
    {
        string[] statuses;
        lock (_sessionGate)
        {
            statuses = _miPlaySessions.Values
                .Where(session => session.Transmitter.IsActive)
                .OrderBy(session => session.Renderer.FriendlyName, StringComparer.CurrentCultureIgnoreCase)
                .Select(session =>
                {
                    var diagnostics = session.Diagnostics;
                    var state = diagnostics.State switch
                    {
                        MiPlayCastState.Streaming when session.Transmitter.ReceiverPaused =>
                            SystemLanguage.Select("已暂停", "Paused"),
                        MiPlayCastState.Streaming =>
                            SystemLanguage.Select("播放中", "Playing"),
                        MiPlayCastState.AwaitingReceiver =>
                            SystemLanguage.Select("等待音箱", "Waiting for speaker"),
                        MiPlayCastState.Connecting or MiPlayCastState.Bootstrapping =>
                            SystemLanguage.Select("连接中", "Connecting"),
                        MiPlayCastState.Stopping =>
                            SystemLanguage.Select("停止中", "Stopping"),
                        MiPlayCastState.Error =>
                            SystemLanguage.Select("错误", "Error"),
                        _ => SystemLanguage.Select("准备中", "Preparing"),
                    };
                    return $"{session.Renderer.FriendlyName} {state}";
                })
                .ToArray();
        }

        if (statuses.Length == 0) return string.Empty;
        return SystemLanguage.Select("音箱状态：", "Speaker state: ") +
               string.Join(" · ", statuses);
    }

    private bool AreMiPlayReceiversPaused
    {
        get
        {
            MiPlaySessionHandle[] sessions;
            lock (_sessionGate)
            {
                sessions = [.. _miPlaySessions.Values];
            }
            var controls = SelectMiPlayPlaybackControls(sessions);
            return controls.Count > 0 && controls.All(session => session.Transmitter.ReceiverPaused);
        }
    }

    private bool CanToggleMiPlayPlayback()
    {
        if (!IsMiPlayMode || IsBusy || IsMiPlayPlaybackControlBusy) return false;
        lock (_sessionGate)
        {
            return SelectMiPlayPlaybackControls(_miPlaySessions.Values).Count > 0;
        }
    }

    private async Task ToggleMiPlayPlaybackAsync()
    {
        MiPlaySessionHandle[] sessions;
        lock (_sessionGate)
        {
            sessions = [.. _miPlaySessions.Values];
        }
        var controls = SelectMiPlayPlaybackControls(sessions);
        if (controls.Count == 0) return;

        var sendResume = controls.All(session => session.Transmitter.ReceiverPaused);
        IsMiPlayPlaybackControlBusy = true;
        ErrorText = string.Empty;
        try
        {
            if (sendResume)
            {
                await Task.WhenAll(controls.Select(session =>
                    session.Transmitter.ResumeReceiverAsync(_lifetime.Token))).ConfigureAwait(false);
            }
            else
            {
                await Task.WhenAll(controls.Select(session =>
                    session.Transmitter.PauseReceiverAsync(_lifetime.Token))).ConfigureAwait(false);
            }

            await RunOnUiAsync(() =>
            {
                SpeakerPlaybackStatusText = GetSpeakerPlaybackStatusText();
                StatusText = sendResume
                    ? SystemLanguage.Select("音箱已继续播放", "Speaker playback resumed")
                    : SystemLanguage.Select("音箱已暂停", "Speaker playback paused");
                _logger.Info(SystemLanguage.Select(
                    $"已手动向 {controls.Count} 个 MiPlay 会话组发送{(sendResume ? "继续" : "暂停")}序列",
                    $"Manually sent {(sendResume ? "Resume" : "Pause")} to {controls.Count} MiPlay session group(s)"));
                RaiseMiPlayPlaybackControlStateChanged();
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            await RunOnUiAsync(() =>
            {
                ErrorText = exception.Message;
                _logger.Error(SystemLanguage.Select(
                    "手动发送 MiPlay 播放控制序列失败",
                    "Failed to send the MiPlay playback-control sequence"), exception);
            }).ConfigureAwait(false);
        }
        finally
        {
            await RunOnUiAsync(() => IsMiPlayPlaybackControlBusy = false).ConfigureAwait(false);
        }
    }

    private void RaiseMiPlayPlaybackControlStateChanged()
    {
        OnPropertyChanged(nameof(MiPlayPlaybackControlText));
        OnPropertyChanged(nameof(MiPlayPlaybackControlToolTip));
        OnPropertyChanged(nameof(MiPlayPlaybackControlGlyph));
        ToggleMiPlayPlaybackCommand.RaiseCanExecuteChanged();
    }

    private static List<MiPlaySessionHandle> SelectMiPlayPlaybackControls(
        IEnumerable<MiPlaySessionHandle> sessions)
    {
        var controls = new List<MiPlaySessionHandle>();
        var sharedSessions = new HashSet<MiPlaySharedAudioSession>(ReferenceEqualityComparer.Instance);
        foreach (var session in sessions)
        {
            if (session.Transmitter.Diagnostics.State != MiPlayCastState.Streaming) continue;
            if (session.SharedAudioSession is null || sharedSessions.Add(session.SharedAudioSession))
            {
                controls.Add(session);
            }
        }
        return controls;
    }

    private async Task RecoverStalledMiPlayMemberAsync(RendererItemViewModel renderer)
    {
        try
        {
            await _selectionGate.WaitAsync(_lifetime.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return;
        }

        try
        {
            MiPlaySessionHandle? failed;
            MiPlaySharedAudioSession? sharedSession;
            CaptureSelection? selection;
            string? ffmpegPath;
            lock (_sessionGate)
            {
                if (!_recoveringMiPlayMembers.Add(renderer.Udn))
                {
                    return;
                }
                if (!_miPlaySessions.Remove(renderer.Udn, out failed))
                {
                    _recoveringMiPlayMembers.Remove(renderer.Udn);
                    return;
                }
                sharedSession = failed.SharedAudioSession;
                selection = _activeMiPlayGroupSelection;
                ffmpegPath = _activeMiPlayGroupFfmpegPath;
            }

            try
            {
                await StopAndDisposeAsync(failed).ConfigureAwait(false);
                if (sharedSession is null || selection is null || ffmpegPath is null)
                {
                    throw new InvalidOperationException(
                        "The stalled MiPlay receiver is not part of a recoverable shared group.");
                }

                Exception? lastFailure = null;
                // Tablet reConnectNum retries the failed native member five
                // times before deleting it from the still-running group.
                foreach (var delayMilliseconds in new[] { 500, 1_000, 2_000, 3_000, 5_000 })
                {
                    await Task.Delay(delayMilliseconds, _lifetime.Token).ConfigureAwait(false);
                    if (!IsRendererAssignedToRoute(renderer, failed.ChannelRoute))
                    {
                        return;
                    }
                    var result = await StartMiPlayTargetAsync(
                            renderer,
                            selection,
                            failed.ChannelRoute,
                            sharedAudioSession: sharedSession,
                            originalSelection: selection,
                            ffmpegPath: ffmpegPath,
                            waitUntilReady: true)
                        .ConfigureAwait(false);
                    if (result.Error is null)
                    {
                        _logger.Info(SystemLanguage.Select(
                            $"{renderer.FriendlyName} 长时间阻塞后已重新加入共享播放",
                            $"{renderer.FriendlyName} rejoined shared playback after a long transport stall"));
                        return;
                    }
                    lastFailure = result.Error;
                }

                var waitsForRediscovery = false;
                lock (_sessionGate)
                {
                    if (ReferenceEquals(_activeMiPlaySharedSession, sharedSession) &&
                        _miPlaySessions.Values.Any(session =>
                            ReferenceEquals(session.SharedAudioSession, sharedSession)))
                    {
                        _pendingMiPlayRejoins[renderer.Udn] = new PendingMiPlayRejoin(
                            renderer,
                            failed.ChannelRoute);
                        waitsForRediscovery = true;
                    }
                }
                if (waitsForRediscovery)
                {
                    await RunOnUiAsync(() =>
                    {
                        StatusText = SystemLanguage.Select(
                            $"{renderer.FriendlyName} 暂时离线，其他音箱继续播放；发现它后将自动重新加入",
                            $"{renderer.FriendlyName} is offline. Other speakers continue, and it will rejoin when rediscovered.");
                        _logger.Info(SystemLanguage.Select(
                            $"{renderer.FriendlyName} 五次重连失败，等待 SSDP 重新发现后加入原共享会话",
                            $"Five rejoin attempts for {renderer.FriendlyName} failed; waiting for SSDP rediscovery before rejoining the original shared session."));
                    }).ConfigureAwait(false);
                    return;
                }

                throw new InvalidOperationException(SystemLanguage.Select(
                    $"{renderer.FriendlyName} 长时间阻塞后的五次重新加入均失败。",
                    $"All five rejoin attempts failed after {renderer.FriendlyName} stalled."),
                    lastFailure);
            }
            finally
            {
                lock (_sessionGate)
                {
                    _recoveringMiPlayMembers.Remove(renderer.Udn);
                }
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            await RunOnUiAsync(() =>
            {
                ErrorText = exception.Message;
                _logger.Error(SystemLanguage.Select(
                    $"恢复 {renderer.FriendlyName} 的共享播放失败",
                    $"Failed to recover shared playback for {renderer.FriendlyName}"), exception);
            }).ConfigureAwait(false);
        }
        finally
        {
            _selectionGate.Release();
        }
    }

    private bool IsRendererAssignedToRoute(
        RendererItemViewModel renderer,
        AudioChannelRoute route) => route switch
    {
        AudioChannelRoute.LeftAsMono => renderer.IsLeftChannel,
        AudioChannelRoute.RightAsMono => renderer.IsRightChannel,
        _ => renderer.IsSelected,
    };

    private async Task RediscoverPendingMiPlayMembersAsync(CancellationToken cancellationToken)
    {
        PendingMiPlayRejoin[] pending;
        lock (_sessionGate)
        {
            pending = [.. _pendingMiPlayRejoins.Values];
        }
        if (pending.Length == 0 || !await _discoveryGate.WaitAsync(0, cancellationToken)) return;

        IReadOnlyList<RendererDevice> devices;
        try
        {
            devices = await _discovery.SearchAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _discoveryGate.Release();
        }

        foreach (var desired in pending)
        {
            var device = devices.FirstOrDefault(candidate =>
                string.Equals(candidate.Udn, desired.Renderer.Udn, StringComparison.OrdinalIgnoreCase));
            if (device is null) continue;

            await RunOnUiAsync(() => desired.Renderer.UpdateDevice(device)).ConfigureAwait(false);
            await _selectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                MiPlaySharedAudioSession? sharedSession;
                CaptureSelection? selection;
                string? ffmpegPath;
                lock (_sessionGate)
                {
                    if (!_pendingMiPlayRejoins.TryGetValue(desired.Renderer.Udn, out var current) ||
                        !ReferenceEquals(current, desired))
                    {
                        continue;
                    }
                    sharedSession = _activeMiPlaySharedSession;
                    selection = _activeMiPlayGroupSelection;
                    ffmpegPath = _activeMiPlayGroupFfmpegPath;
                }

                if (!IsRendererAssignedToRoute(desired.Renderer, desired.ChannelRoute) ||
                    sharedSession is null || selection is null || ffmpegPath is null)
                {
                    lock (_sessionGate) _pendingMiPlayRejoins.Remove(desired.Renderer.Udn);
                    continue;
                }

                var result = await StartMiPlayTargetAsync(
                        desired.Renderer,
                        selection,
                        desired.ChannelRoute,
                        sharedAudioSession: sharedSession,
                        originalSelection: selection,
                        ffmpegPath: ffmpegPath,
                        waitUntilReady: true)
                    .ConfigureAwait(false);
                if (result.Error is not null)
                {
                    _logger.Error(SystemLanguage.Select(
                        $"重新发现 {desired.Renderer.FriendlyName} 后加入共享播放失败，继续等待下一次发现",
                        $"{desired.Renderer.FriendlyName} was rediscovered but failed to rejoin shared playback; waiting for the next discovery."),
                        result.Error);
                    continue;
                }

                lock (_sessionGate) _pendingMiPlayRejoins.Remove(desired.Renderer.Udn);
                await RunOnUiAsync(() =>
                {
                    StatusText = SystemLanguage.Select(
                        $"{desired.Renderer.FriendlyName} 已重新加入共享播放",
                        $"{desired.Renderer.FriendlyName} rejoined shared playback.");
                    _logger.Info(SystemLanguage.Select(
                        $"重新发现 {desired.Renderer.FriendlyName} 后已加入原共享会话",
                        $"{desired.Renderer.FriendlyName} rejoined the original shared session after rediscovery."));
                    NotifyCastStateChanged();
                }).ConfigureAwait(false);
            }
            finally
            {
                _selectionGate.Release();
            }
        }
    }

    private void NotifyCastStateChanged()
    {
        UpdateCastDurationState(IsCasting);
        SpeakerPlaybackStatusText = GetSpeakerPlaybackStatusText();
        foreach (var renderer in Renderers) renderer.IsSelectionEnabled = !IsBusy;
        if (!IsCasting) ProfileText = SelectedTransportProfileText;
        OnPropertyChanged(nameof(IsCasting));
        OnPropertyChanged(nameof(CastingStatusVisibility));
        OnPropertyChanged(nameof(InactiveStatusVisibility));
        OnPropertyChanged(nameof(CanUseProcessCapture));
        OnPropertyChanged(nameof(CanChangeTransport));
        OnPropertyChanged(nameof(CanChangeMiPlayTimeOffset));
        OnPropertyChanged(nameof(CanUseSpeakerOnlyPlayback));
        OnPropertyChanged(nameof(CanUseStereoSplit));
        OnPropertyChanged(nameof(MiPlayPlaybackControlVisibility));
        RefreshSourcesCommand.RaiseCanExecuteChanged();
        TestLatencyCommand.RaiseCanExecuteChanged();
        RaiseMiPlayPlaybackControlStateChanged();
        if (!IsCasting && !IsLatencyTesting)
        {
            ResetLatencyTestDisplay();
        }
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
        var entered = false;
        try
        {
            await _settingsSaveGate.WaitAsync(_lifetime.Token);
            entered = true;
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
        finally
        {
            if (entered) _settingsSaveGate.Release();
        }
    }

    private async Task SaveMiPlayTimeOffsetAsync(AppSettings settings)
    {
        var entered = false;
        try
        {
            await _settingsSaveGate.WaitAsync(_lifetime.Token);
            entered = true;
            await _settingsStore.SaveAsync(settings, _lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.Error(SystemLanguage.Select(
                "保存 TIME_OFFSET 设置失败",
                "Failed to save the TIME_OFFSET setting"), exception);
            await RunOnUiAsync(() => ErrorText = exception.Message);
        }
        finally
        {
            if (entered) _settingsSaveGate.Release();
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
                bool hasPendingMiPlayRejoins;
                lock (_sessionGate)
                {
                    hasPendingMiPlayRejoins = _pendingMiPlayRejoins.Count > 0;
                }
                if (hasPendingMiPlayRejoins)
                {
                    await RediscoverPendingMiPlayMembersAsync(cancellationToken);
                    discoveryRetry = initialDiscoveryRetry;
                    nextDiscoveryRetryAt = DateTimeOffset.UtcNow + discoveryRetry;
                    continue;
                }
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
        ILocalOutputLease? localMuteLease,
        AudioChannelRoute channelRoute,
        MiPlaySharedAudioSession? sharedAudioSession)
    {
        public RendererItemViewModel Renderer { get; } = renderer;
        public MiPlaySystemAudioTransmitter Transmitter { get; } = transmitter;
        public EventHandler<MiPlayCastDiagnostics> Handler { get; } = handler;
        public CaptureSelection OriginalSelection { get; set; } = originalSelection;
        public ILocalOutputLease? LocalMuteLease { get; set; } = localMuteLease;
        public AudioChannelRoute ChannelRoute { get; } = channelRoute;
        public MiPlaySharedAudioSession? SharedAudioSession { get; } = sharedAudioSession;
        public MiPlayCastDiagnostics Diagnostics { get; set; } = transmitter.Diagnostics;
    }

    private sealed record StartResult(string Udn, string RendererName, Exception? Error);

    private sealed record StereoTarget(
        RendererItemViewModel Renderer,
        AudioChannelRoute ChannelRoute);

    private sealed record PendingMiPlayRejoin(
        RendererItemViewModel Renderer,
        AudioChannelRoute ChannelRoute);

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
