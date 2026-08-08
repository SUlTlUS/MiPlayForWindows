using System.Runtime.InteropServices;
using DLNACast.Core.Localization;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.Graphics;
using WinRT.Interop;

namespace DLNACast.App;

internal sealed class TrayMenuWindow : Microsoft.UI.Xaml.Window
{
    private const int HostSize = 2;
    private readonly Action _showMainWindow;
    private readonly Func<Task> _stopCasting;
    private readonly Func<Task> _exitApplication;
    private readonly Grid _anchor;
    private readonly MenuFlyout _menu;
    private readonly IntPtr _windowHandle;
    private readonly AppWindow _appWindow;

    public TrayMenuWindow(Action showMainWindow, Func<Task> stopCasting, Func<Task> exitApplication)
    {
        _showMainWindow = showMainWindow;
        _stopCasting = stopCasting;
        _exitApplication = exitApplication;

        Title = SystemLanguage.Select("DLNA Cast 托盘菜单宿主", "DLNA Cast tray menu host");
        _anchor = new Grid();
        Content = _anchor;

        _windowHandle = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(_windowHandle);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        _appWindow.IsShownInSwitchers = false;
        _appWindow.Resize(new SizeInt32(HostSize, HostSize));
        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
        }

        _menu = CreateMenu();
        _menu.Opened += (_, _) => MakeHostWindowInvisible(_windowHandle);
        _menu.Closed += (_, _) => _appWindow.Hide();
    }

    private MenuFlyout CreateMenu()
    {
        var menu = new MenuFlyout
        {
            Placement = FlyoutPlacementMode.TopEdgeAlignedRight
        };

        menu.Items.Add(CreateMenuItem(
            SystemLanguage.Select("打开 DLNA Cast", "Open DLNA Cast"),
            "\uE8A7",
            (_, _) => _showMainWindow()));
        menu.Items.Add(CreateMenuItem(
            SystemLanguage.Select("停止投送", "Stop casting"),
            "\uE71A",
            async (_, _) => await _stopCasting()));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(CreateMenuItem(
            SystemLanguage.Select("退出", "Exit"),
            "\uE8BB",
            async (_, _) => await _exitApplication()));
        return menu;
    }

    private static MenuFlyoutItem CreateMenuItem(string text, string glyph, RoutedEventHandler click)
    {
        var item = new MenuFlyoutItem
        {
            Text = text,
            Icon = new FontIcon { Glyph = glyph }
        };
        item.Click += click;
        return item;
    }

    public void ShowAt(int cursorX, int cursorY)
    {
        if (_menu.IsOpen)
        {
            _menu.Hide();
        }

        var point = new PointInt32(cursorX, cursorY);
        var workArea = DisplayArea.GetFromPoint(point, DisplayAreaFallback.Nearest).WorkArea;
        var x = Math.Clamp(cursorX - HostSize, workArea.X, workArea.X + workArea.Width - HostSize);
        var y = Math.Clamp(cursorY - HostSize, workArea.Y, workArea.Y + workArea.Height - HostSize);

        _ = SetWindowRgn(_windowHandle, IntPtr.Zero, true);
        _appWindow.MoveAndResize(new RectInt32(x, y, HostSize, HostSize));
        _appWindow.Show();
        Activate();
        _menu.ShowAt(_anchor, new FlyoutShowOptions
        {
            Placement = FlyoutPlacementMode.TopEdgeAlignedRight,
            ShowMode = FlyoutShowMode.Standard
        });
    }

    private static void MakeHostWindowInvisible(IntPtr windowHandle)
    {
        var emptyRegion = CreateRectRgn(0, 0, 0, 0);
        if (emptyRegion == IntPtr.Zero)
        {
            throw new InvalidOperationException(SystemLanguage.Select(
                "无法创建托盘菜单宿主窗口区域。",
                "Unable to create the tray menu host window region."));
        }

        if (SetWindowRgn(windowHandle, emptyRegion, true) == 0)
        {
            _ = DeleteObject(emptyRegion);
            throw new InvalidOperationException(SystemLanguage.Select(
                "无法隐藏托盘菜单宿主窗口。",
                "Unable to hide the tray menu host window."));
        }
    }

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr windowHandle, IntPtr region, bool redraw);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr graphicsObject);
}
