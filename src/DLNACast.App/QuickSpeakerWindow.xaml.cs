using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using DLNACast.App.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.System;
using WinRT.Interop;

namespace DLNACast.App;

internal sealed partial class QuickSpeakerWindow : Microsoft.UI.Xaml.Window
{
    private const int WhMouseLowLevel = 14;
    private const uint WmLeftButtonDown = 0x0201;
    private const uint WmRightButtonDown = 0x0204;
    private const uint WmMiddleButtonDown = 0x0207;
    private const uint WmXButtonDown = 0x020B;
    private const double NormalWindowWidthInDips = 360;
    private const double StereoWindowWidthInDips = 460;
    private const double TrayGapInDips = 8;

    private readonly MainViewModel _viewModel;
    private readonly App _application;
    private readonly IntPtr _windowHandle;
    private readonly AppWindow _appWindow;
    private readonly LowLevelMouseProcedure _mouseHookProcedure;
    private IntPtr _mouseHookHandle;
    private int _anchorX;
    private int _anchorY;
    private int _showRevision;

    public QuickSpeakerWindow(MainViewModel viewModel, App application)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _application = application;
        _mouseHookProcedure = MouseHookCallback;
        Root.DataContext = viewModel;
        Title = $"MiPlay Cast - {viewModel.Text.PlaybackDevices}";
        SystemBackdrop = new MicaBackdrop();

        _windowHandle = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(_windowHandle);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        _appWindow.IsShownInSwitchers = false;
        var appIconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "MiPlay.ico");
        if (File.Exists(appIconPath)) _appWindow.SetIcon(appIconPath);

        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(true, false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
        }

        _appWindow.Closing += OnClosing;
        Activated += OnActivated;
        _viewModel.Renderers.CollectionChanged += OnRenderersChanged;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        UpdateEmptyState();
        UpdateRendererModeLayout();
    }

    public void ShowAt(int cursorX, int cursorY)
    {
        StopClickOutsideMonitoring();
        _showRevision++;
        _anchorX = cursorX;
        _anchorY = cursorY;
        UpdateRendererModeLayout();
        PositionAndResize();
        _appWindow.Show();
        Activate();
        StartClickOutsideMonitoring();
        var revision = _showRevision;
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (revision == _showRevision && _appWindow.IsVisible) PositionAndResize();
        });
    }

    private void PositionAndResize()
    {
        var dpi = GetDpiForWindow(_windowHandle);
        var scale = dpi == 0 ? 1d : dpi / 96d;
        var point = new PointInt32(_anchorX, _anchorY);
        var workArea = DisplayArea.GetFromPoint(point, DisplayAreaFallback.Nearest).WorkArea;
        var gap = (int)Math.Round(TrayGapInDips * scale);
        var desiredWidthInDips = _viewModel.IsStereoSplitMode
            ? StereoWindowWidthInDips
            : NormalWindowWidthInDips;
        var width = Math.Min(
            (int)Math.Round(desiredWidthInDips * scale),
            Math.Max(1, workArea.Width - gap * 2));
        var frameSize = GetNonClientFrameSize();
        var contentWidthInDips = Math.Max(1, width - frameSize.Width) / scale;
        Root.InvalidateMeasure();
        Root.Measure(new Windows.Foundation.Size(contentWidthInDips, double.PositiveInfinity));
        var desiredHeight = (int)Math.Ceiling(Root.DesiredSize.Height * scale) + frameSize.Height;
        var height = Math.Min(desiredHeight, Math.Max(1, workArea.Height - gap * 2));

        var x = _anchorX - width;
        var y = _anchorY - height - gap;
        if (y < workArea.Y) y = _anchorY + gap;

        x = Math.Clamp(x, workArea.X, workArea.X + workArea.Width - width);
        y = Math.Clamp(y, workArea.Y, workArea.Y + workArea.Height - height);
        _appWindow.MoveAndResize(new RectInt32(x, y, width, height));
    }

    private void OnRenderersChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        UpdateEmptyState();
        if (_appWindow.IsVisible) PositionAndResize();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(MainViewModel.IsStereoSplitMode)) return;
        UpdateRendererModeLayout();
        if (!_appWindow.IsVisible) return;
        var revision = _showRevision;
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (revision == _showRevision && _appWindow.IsVisible) PositionAndResize();
        });
    }

    private void UpdateEmptyState()
    {
        var hasRenderers = _viewModel.Renderers.Count > 0;
        RendererList.Visibility = hasRenderers ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Visibility = hasRenderers ? Visibility.Collapsed : Visibility.Visible;
    }

    private void UpdateRendererModeLayout()
    {
        var isStereo = _viewModel.IsStereoSplitMode;
        NormalRendererList.Visibility = isStereo ? Visibility.Collapsed : Visibility.Visible;
        StereoRendererLists.Visibility = isStereo ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated && !_application.IsExiting)
        {
            Dismiss();
        }
    }

    private void OnRootKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Escape) return;
        args.Handled = true;
        Dismiss();
    }

    private void StartClickOutsideMonitoring()
    {
        if (_mouseHookHandle != IntPtr.Zero) return;
        _mouseHookHandle = SetWindowsHookEx(
            WhMouseLowLevel,
            _mouseHookProcedure,
            GetModuleHandle(null),
            0);
        if (_mouseHookHandle == IntPtr.Zero)
        {
            StartupTrace.Write($"QuickSpeakerWindow: mouse hook failed ({Marshal.GetLastWin32Error()})");
        }
    }

    private void StopClickOutsideMonitoring()
    {
        if (_mouseHookHandle == IntPtr.Zero) return;
        _ = UnhookWindowsHookEx(_mouseHookHandle);
        _mouseHookHandle = IntPtr.Zero;
    }

    private IntPtr MouseHookCallback(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && IsMouseButtonDown(unchecked((uint)message.ToInt64())))
        {
            var mouse = Marshal.PtrToStructure<LowLevelMouseInput>(data);
            if (!IsInsideWindow(mouse.Point))
            {
                var revision = _showRevision;
                _ = DispatcherQueue.TryEnqueue(() =>
                {
                    if (revision == _showRevision) Dismiss();
                });
            }
        }

        return CallNextHookEx(_mouseHookHandle, code, message, data);
    }

    private static bool IsMouseButtonDown(uint message) =>
        message is WmLeftButtonDown or WmRightButtonDown or WmMiddleButtonDown or WmXButtonDown;

    private bool IsInsideWindow(NativePoint point) =>
        GetWindowRect(_windowHandle, out var bounds) &&
        point.X >= bounds.Left &&
        point.X < bounds.Right &&
        point.Y >= bounds.Top &&
        point.Y < bounds.Bottom;

    private SizeInt32 GetNonClientFrameSize()
    {
        if (!GetWindowRect(_windowHandle, out var windowBounds) ||
            !GetClientRect(_windowHandle, out var clientBounds))
        {
            return new SizeInt32(0, 0);
        }

        return new SizeInt32(
            Math.Max(0, windowBounds.Right - windowBounds.Left - (clientBounds.Right - clientBounds.Left)),
            Math.Max(0, windowBounds.Bottom - windowBounds.Top - (clientBounds.Bottom - clientBounds.Top)));
    }

    private void Dismiss()
    {
        StopClickOutsideMonitoring();
        _showRevision++;
        _appWindow.Hide();
    }

    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_application.IsExiting)
        {
            StopClickOutsideMonitoring();
            _viewModel.Renderers.CollectionChanged -= OnRenderersChanged;
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            return;
        }

        args.Cancel = true;
        Dismiss();
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint windowHandle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(
        int hookType,
        LowLevelMouseProcedure hookProcedure,
        IntPtr module,
        uint threadId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(
        IntPtr hook,
        int code,
        IntPtr message,
        IntPtr data);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr windowHandle, out NativeRect bounds);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr windowHandle, out NativeRect bounds);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    private delegate IntPtr LowLevelMouseProcedure(int code, IntPtr message, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LowLevelMouseInput
    {
        public NativePoint Point;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }
}
