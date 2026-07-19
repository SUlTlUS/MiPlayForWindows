using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using WinRT.Interop;

namespace DLNACast.App;

internal sealed class TrayMenuWindow : Microsoft.UI.Xaml.Window
{
    private const int MenuWidth = 244;
    private const int MenuHeight = 176;
    private readonly Action _showMainWindow;
    private readonly Func<Task> _stopCasting;
    private readonly Func<Task> _exitApplication;
    private readonly AppWindow _appWindow;
    private bool _showing;

    public TrayMenuWindow(Action showMainWindow, Func<Task> stopCasting, Func<Task> exitApplication)
    {
        _showMainWindow = showMainWindow;
        _stopCasting = stopCasting;
        _exitApplication = exitApplication;
        Content = CreateContent();
        SystemBackdrop = new DesktopAcrylicBackdrop();

        var windowHandle = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(windowHandle);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        _appWindow.IsShownInSwitchers = false;
        _appWindow.Resize(new SizeInt32(MenuWidth, MenuHeight));
        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
        }

        Activated += (_, args) =>
        {
            if (_showing && args.WindowActivationState == WindowActivationState.Deactivated)
            {
                Hide();
            }
        };
    }

    private UIElement CreateContent()
    {
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(CreateButton("打开 DLNA Cast", "\uE8A7", (_, _) =>
        {
            Hide();
            _showMainWindow();
        }));
        panel.Children.Add(CreateButton("停止投送", "\uE71A", async (_, _) =>
        {
            await _stopCasting();
            Hide();
        }));
        panel.Children.Add(new Border
        {
            Height = 1,
            Margin = new Thickness(8, 5, 8, 5),
            Background = new SolidColorBrush(Colors.Gray) { Opacity = 0.35 }
        });
        panel.Children.Add(CreateButton("退出", "\uE8BB", async (_, _) => await _exitApplication()));

        return new Border
        {
            Padding = new Thickness(10),
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(235, 28, 33, 43)),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(72, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Child = panel
        };
    }

    private static Button CreateButton(string text, string glyph, RoutedEventHandler click)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        content.Children.Add(new FontIcon { Glyph = glyph, FontSize = 16, Width = 22 });
        content.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        var button = new Button
        {
            Content = content,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 8, 12, 8)
        };
        button.Click += click;
        return button;
    }

    public void ShowAt(int cursorX, int cursorY)
    {
        var point = new PointInt32(cursorX, cursorY);
        var display = DisplayArea.GetFromPoint(point, DisplayAreaFallback.Nearest);
        var workArea = display.WorkArea;
        var x = Math.Clamp(cursorX - MenuWidth, workArea.X, workArea.X + workArea.Width - MenuWidth);
        var y = Math.Clamp(cursorY - MenuHeight, workArea.Y, workArea.Y + workArea.Height - MenuHeight);
        _appWindow.MoveAndResize(new RectInt32(x, y, MenuWidth, MenuHeight));
        _showing = true;
        _appWindow.Show();
        Activate();
    }

    private void Hide()
    {
        _showing = false;
        _appWindow.Hide();
    }
}
