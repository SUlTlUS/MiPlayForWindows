using DLNACast.App.ViewModels;
using DLNACast.Core.Audio;
using DLNACast.Core.Casting;
using DLNACast.Core.Dlna;
using DLNACast.Core.Storage;
using DLNACast.Core.Streaming;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace DLNACast.App;

public partial class App : Microsoft.UI.Xaml.Application
{
    private TrayIconService? _trayIcon;
    private TrayMenuWindow? _trayMenu;
    private MainViewModel? _viewModel;
    private MainWindow? _mainWindow;
    private RendererDiscoveryService? _discovery;
    private RendererController? _controller;

    public bool IsExiting { get; private set; }

    public App()
    {
        StartupTrace.Write("App constructor: before InitializeComponent");
        UnhandledException += OnUnhandledException;
        InitializeComponent();
        StartupTrace.Write("App constructor: after InitializeComponent");
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        StartupTrace.Write("OnLaunched: begin");
        var settingsStore = new AppSettingsStore();
        var logger = new AppLogger(settingsStore.BasePath);
        var audioCatalog = new AudioSourceCatalog();
        _discovery = new RendererDiscoveryService();
        _controller = new RendererController();
        var streamServer = new LiveStreamServer();
        var coordinator = new CastCoordinator(
            audioCatalog,
            _controller,
            streamServer,
            new WindowsLocalOutputManager());
        _viewModel = new MainViewModel(
            audioCatalog,
            _discovery,
            coordinator,
            _controller,
            settingsStore,
            logger,
            DispatcherQueue.GetForCurrentThread());
        StartupTrace.Write("OnLaunched: services and view model created");

        _mainWindow = new MainWindow(_viewModel, this);
        StartupTrace.Write("OnLaunched: main window created");
        _trayMenu = new TrayMenuWindow(ShowMainWindow, _viewModel.StopCastingAsync, ExitApplicationAsync);
        StartupTrace.Write("OnLaunched: tray menu created");
        _trayIcon = new TrayIconService(
            WindowNative.GetWindowHandle(_mainWindow),
            ShowMainWindow,
            ShowTrayMenu);
        StartupTrace.Write("OnLaunched: tray icon created");
        _mainWindow.Activate();
        StartupTrace.Write("OnLaunched: main window activated");
    }

    private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs args) =>
        StartupTrace.Write($"Unhandled WinUI exception: {args.Message}", args.Exception);

    public void ShowMainWindow() => _mainWindow?.ShowAndActivate();

    private void ShowTrayMenu(int x, int y) => _trayMenu?.ShowAt(x, y);

    public async Task ExitApplicationAsync()
    {
        if (IsExiting) return;
        IsExiting = true;
        try
        {
            if (_viewModel is not null) await _viewModel.DisposeAsync();
            if (_discovery is not null) await _discovery.DisposeAsync();
            _controller?.Dispose();
        }
        finally
        {
            _trayIcon?.Dispose();
            _trayMenu?.Close();
            _mainWindow?.Close();
            Exit();
        }
    }
}
