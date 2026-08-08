using System.Runtime.InteropServices;
using DLNACast.Core.Localization;

namespace DLNACast.App;

/// <summary>
/// Registers the tray icon directly with the Windows Shell. The popup itself is
/// a WinUI 3 window; no WinForms or WPF UI stack is loaded into the process.
/// </summary>
internal sealed class TrayIconService : IDisposable
{
    private const int GwlpWndProc = -4;
    private const uint WmAppTrayIcon = 0x8001;
    private const uint WmLButtonUp = 0x0202;
    private const uint WmLButtonDoubleClick = 0x0203;
    private const uint WmRButtonUp = 0x0205;
    private const uint WmContextMenu = 0x007B;
    private const uint NimAdd = 0x00000000;
    private const uint NimDelete = 0x00000002;
    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;
    private const uint IdiApplication = 32512;

    private readonly Action _showMainWindow;
    private readonly Action<int, int> _showMenu;
    private readonly WindowProcedure _windowProcedure;
    private readonly IntPtr _windowHandle;
    private readonly IntPtr _originalWindowProcedure;
    private NotifyIconData _iconData;
    private bool _disposed;

    public TrayIconService(IntPtr windowHandle, Action showMainWindow, Action<int, int> showMenu)
    {
        _windowHandle = windowHandle;
        _showMainWindow = showMainWindow;
        _showMenu = showMenu;
        _windowProcedure = WindowProc;
        _originalWindowProcedure = GetWindowLongPtr(_windowHandle, GwlpWndProc);
        var callbackPointer = Marshal.GetFunctionPointerForDelegate(_windowProcedure);
        if (SetWindowLongPtr(_windowHandle, GwlpWndProc, callbackPointer) == IntPtr.Zero)
        {
            throw new InvalidOperationException(SystemLanguage.Select(
                "无法注册系统托盘回调。",
                "Unable to register the system tray callback."));
        }

        _iconData = new NotifyIconData
        {
            Size = (uint)Marshal.SizeOf<NotifyIconData>(),
            WindowHandle = _windowHandle,
            Id = 1,
            Flags = NifMessage | NifIcon | NifTip,
            CallbackMessage = WmAppTrayIcon,
            IconHandle = LoadIcon(IntPtr.Zero, new IntPtr(IdiApplication)),
            Tip = "DLNA Cast for Windows",
            Info = string.Empty,
            InfoTitle = string.Empty
        };

        if (!ShellNotifyIcon(NimAdd, ref _iconData))
        {
            SetWindowLongPtr(_windowHandle, GwlpWndProc, _originalWindowProcedure);
            throw new InvalidOperationException(SystemLanguage.Select(
                "无法在 Windows 通知区创建图标。",
                "Unable to create an icon in the Windows notification area."));
        }
    }

    private IntPtr WindowProc(IntPtr windowHandle, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == WmAppTrayIcon)
        {
            var mouseMessage = unchecked((uint)lParam.ToInt64());
            if (mouseMessage is WmLButtonUp or WmLButtonDoubleClick)
            {
                _showMainWindow();
                return IntPtr.Zero;
            }

            if (mouseMessage is WmRButtonUp or WmContextMenu)
            {
                if (GetCursorPos(out var cursor))
                {
                    _showMenu(cursor.X, cursor.Y);
                }
                return IntPtr.Zero;
            }
        }

        return CallWindowProc(_originalWindowProcedure, windowHandle, message, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ShellNotifyIcon(NimDelete, ref _iconData);
        if (_windowHandle != IntPtr.Zero && _originalWindowProcedure != IntPtr.Zero)
        {
            SetWindowLongPtr(_windowHandle, GwlpWndProc, _originalWindowProcedure);
        }
        GC.KeepAlive(_windowProcedure);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size;
        public IntPtr WindowHandle;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public IntPtr IconHandle;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State;
        public uint StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint TimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags;
        public Guid ItemGuid;
        public IntPtr BalloonIconHandle;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    private delegate IntPtr WindowProcedure(IntPtr windowHandle, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "Shell_NotifyIconW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellNotifyIcon(uint message, ref NotifyIconData data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "LoadIconW")]
    private static extern IntPtr LoadIcon(IntPtr instance, IntPtr iconName);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr windowHandle, int index, IntPtr value);

    [DllImport("user32.dll")]
    private static extern IntPtr CallWindowProc(
        IntPtr previousWindowProcedure,
        IntPtr windowHandle,
        uint message,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);
}
