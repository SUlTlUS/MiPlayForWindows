using System.ComponentModel;
using System.Runtime.CompilerServices;
using DLNACast.Core.Abstractions;
using DLNACast.Core.Localization;
using DLNACast.Core.Models;
using DLNACast.Core.Storage;
using Microsoft.UI.Xaml;

namespace DLNACast.App.ViewModels;

public sealed class RendererItemViewModel(
    RendererDevice device,
    IRendererController controller,
    AppLogger logger,
    CancellationToken lifetimeToken) : INotifyPropertyChanged, IDisposable
{
    private readonly IRendererController _controller = controller;
    private readonly AppLogger _logger = logger;
    private readonly CancellationToken _lifetimeToken = lifetimeToken;
    private CancellationTokenSource? _volumeDebounce;
    private bool _isSelected;
    private bool _isLeftChannel;
    private bool _isRightChannel;
    private bool _isStereoMode;
    private bool _isSelectionEnabled = true;
    private bool _updatingVolume;
    private double _volume = 30;

    public RendererDevice Device { get; } = device;
    public string Udn => Device.Udn;
    public string FriendlyName => Device.FriendlyName;
    public string DeviceDescription => string.IsNullOrWhiteSpace(Device.ModelName)
        ? Device.Manufacturer
        : Device.ModelName;
    public bool CanControlVolume => Device.RenderingControl is not null;
    public bool CanAdjustVolume => IsAssigned && CanControlVolume;
    public bool CanAdjustLeftChannelVolume => IsLeftChannel && CanControlVolume;
    public bool CanAdjustRightChannelVolume => IsRightChannel && CanControlVolume;
    public bool IsAssigned => IsSelected || IsLeftChannel || IsRightChannel;
    public Visibility NormalSelectionVisibility => IsStereoMode ? Visibility.Collapsed : Visibility.Visible;
    public Visibility StereoSelectionVisibility => IsStereoMode ? Visibility.Visible : Visibility.Collapsed;
    public string LeftChannelText => SystemLanguage.Select("左声道", "Left channel");
    public string RightChannelText => SystemLanguage.Select("右声道", "Right channel");
    public string VolumeText => CanControlVolume
        ? $"{Volume:F0}%"
        : SystemLanguage.Select("不可用", "Unavailable");
    public string SpeakerVolumeAutomationName => SystemLanguage.Select("音箱音量", "Speaker volume");
    public string LeftChannelSelectionAutomationName => SystemLanguage.Select(
        $"将 {FriendlyName} 设为左声道音箱",
        $"Use {FriendlyName} for the left channel");
    public string RightChannelSelectionAutomationName => SystemLanguage.Select(
        $"将 {FriendlyName} 设为右声道音箱",
        $"Use {FriendlyName} for the right channel");
    public string LeftChannelVolumeAutomationName => SystemLanguage.Select(
        $"{FriendlyName} 左声道音量",
        $"{FriendlyName} left-channel volume");
    public string RightChannelVolumeAutomationName => SystemLanguage.Select(
        $"{FriendlyName} 右声道音量",
        $"{FriendlyName} right-channel volume");

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (!SetField(ref _isSelected, value)) return;
            OnPropertyChanged(nameof(IsAssigned));
            OnPropertyChanged(nameof(CanAdjustVolume));
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool IsLeftChannel
    {
        get => _isLeftChannel;
        set
        {
            if (value && _isRightChannel)
            {
                _isRightChannel = false;
                OnPropertyChanged(nameof(IsRightChannel));
                OnPropertyChanged(nameof(CanAdjustRightChannelVolume));
            }
            if (!SetField(ref _isLeftChannel, value)) return;
            OnPropertyChanged(nameof(IsAssigned));
            OnPropertyChanged(nameof(CanAdjustVolume));
            OnPropertyChanged(nameof(CanAdjustLeftChannelVolume));
            ChannelAssignmentChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool IsRightChannel
    {
        get => _isRightChannel;
        set
        {
            if (value && _isLeftChannel)
            {
                _isLeftChannel = false;
                OnPropertyChanged(nameof(IsLeftChannel));
                OnPropertyChanged(nameof(CanAdjustLeftChannelVolume));
            }
            if (!SetField(ref _isRightChannel, value)) return;
            OnPropertyChanged(nameof(IsAssigned));
            OnPropertyChanged(nameof(CanAdjustVolume));
            OnPropertyChanged(nameof(CanAdjustRightChannelVolume));
            ChannelAssignmentChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool IsStereoMode
    {
        get => _isStereoMode;
        set
        {
            if (!SetField(ref _isStereoMode, value)) return;
            OnPropertyChanged(nameof(NormalSelectionVisibility));
            OnPropertyChanged(nameof(StereoSelectionVisibility));
        }
    }

    public bool IsSelectionEnabled
    {
        get => _isSelectionEnabled;
        set => SetField(ref _isSelectionEnabled, value);
    }

    public double Volume
    {
        get => _volume;
        set
        {
            if (!SetField(ref _volume, Math.Clamp(value, 0, 100))) return;
            OnPropertyChanged(nameof(VolumeText));
            if (!_updatingVolume && CanControlVolume) DebounceVolumeUpdate();
            VolumeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? SelectionChanged;
    public event EventHandler? ChannelAssignmentChanged;
    public event EventHandler? VolumeChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    public void SetInitialVolume(int? volume)
    {
        if (volume is null) return;
        _updatingVolume = true;
        try
        {
            Volume = volume.Value;
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
        _volumeDebounce = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeToken);
        var token = _volumeDebounce.Token;
        var volume = (int)Math.Round(Volume);
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(150, token);
                await _controller.SetVolumeAsync(Device, volume, token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                _logger.Error(SystemLanguage.Select(
                    $"设置 {FriendlyName} 音量失败",
                    $"Failed to set the volume for {FriendlyName}"), exception);
            }
        }, token);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    public void Dispose()
    {
        _volumeDebounce?.Cancel();
        _volumeDebounce?.Dispose();
    }
}
