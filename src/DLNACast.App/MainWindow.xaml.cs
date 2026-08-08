using DLNACast.App.ViewModels;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using WinRT.Interop;

namespace DLNACast.App;

public sealed partial class MainWindow : Microsoft.UI.Xaml.Window
{
    private const double InitialWidthInDips = 1080;
    private const double InitialHeightInDips = 800;
    private const double WorkAreaMarginInDips = 48;

    private readonly MainViewModel _viewModel;
    private readonly App _application;
    private readonly AppWindow _appWindow;
    private bool _initialized;

    public MainWindow(MainViewModel viewModel, App application)
    {
        StartupTrace.Write("MainWindow: before InitializeComponent");
        InitializeComponent();
        StartupTrace.Write("MainWindow: after InitializeComponent");
        _viewModel = viewModel;
        _application = application;
        Root.DataContext = viewModel;
        Root.SizeChanged += OnRootSizeChanged;

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        SystemBackdrop = new MicaBackdrop();

        var windowHandle = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(windowHandle);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        _appWindow.Title = "DLNA / MiPlay Cast for Windows";
        _appWindow.Resize(GetInitialWindowSize(windowHandle, windowId));
        _appWindow.Closing += OnClosing;
        Activated += OnActivated;
        StartupTrace.Write("MainWindow: constructor complete");
    }

    private static SizeInt32 GetInitialWindowSize(nint windowHandle, WindowId windowId)
    {
        var dpi = GetDpiForWindow(windowHandle);
        var scale = dpi == 0 ? 1d : dpi / 96d;
        var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
        var workArea = displayArea.WorkArea;
        var margin = (int)Math.Ceiling(WorkAreaMarginInDips * scale);
        var desiredWidth = (int)Math.Round(InitialWidthInDips * scale);
        var desiredHeight = (int)Math.Round(InitialHeightInDips * scale);
        var availableWidth = Math.Max(1, workArea.Width - margin);
        var availableHeight = Math.Max(1, workArea.Height - margin);

        return new SizeInt32(
            Math.Min(desiredWidth, availableWidth),
            Math.Min(desiredHeight, availableHeight));
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint windowHandle);

    private void OnRootSizeChanged(object sender, SizeChangedEventArgs args)
    {
        var useNarrowLayout = args.NewSize.Width < 900;
        PageLayout.Width = Math.Min(1180, args.NewSize.Width);
        SideColumn.Width = new GridLength(useNarrowLayout ? 0 : 344);
        Grid.SetColumnSpan(WorkflowPanel, useNarrowLayout ? 2 : 1);
        Grid.SetRow(DevicePanel, useNarrowLayout ? 2 : 1);
        Grid.SetColumn(DevicePanel, useNarrowLayout ? 0 : 1);
        Grid.SetColumnSpan(DevicePanel, useNarrowLayout ? 2 : 1);
    }

    private async void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (_initialized || args.WindowActivationState == WindowActivationState.Deactivated) return;
        _initialized = true;
        StartupTrace.Write("MainWindow: initialization begin");
        try
        {
            await _viewModel.InitializeAsync();
            StartupTrace.Write("MainWindow: initialization complete");
        }
        catch (Exception exception)
        {
            StartupTrace.Write("MainWindow: initialization failed", exception);
            _viewModel.ReportInitializationFailure(exception);
        }
    }

    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_application.IsExiting) return;
        args.Cancel = true;
        sender.Hide();
    }

    public void ShowAndActivate()
    {
        _appWindow.Show();
        Activate();
    }
}
