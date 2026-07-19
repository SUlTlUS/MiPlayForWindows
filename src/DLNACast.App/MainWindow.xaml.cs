using DLNACast.App.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using WinRT.Interop;

namespace DLNACast.App;

public sealed partial class MainWindow : Microsoft.UI.Xaml.Window
{
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

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        SystemBackdrop = new MicaBackdrop();

        var windowHandle = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(windowHandle);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        _appWindow.Title = "DLNA Cast for Windows";
        _appWindow.Resize(new SizeInt32(920, 760));
        _appWindow.Closing += OnClosing;
        Activated += OnActivated;
        StartupTrace.Write("MainWindow: constructor complete");
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
