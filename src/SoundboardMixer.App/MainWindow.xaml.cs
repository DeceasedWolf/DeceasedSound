using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using SoundboardMixer.App.Services.Hotkeys;
using SoundboardMixer.App.ViewModels;

namespace SoundboardMixer.App;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly IGlobalHotkeyService _globalHotkeyService;
    private readonly ContextMenu _trayMenu;
    private HwndSource? _hwndSource;
    private bool _isExplicitExitRequested;
    private bool _isShutdownCloseReady;
    private bool _isShutdownInProgress;
    private bool _isTrayIconVisible;
    private uint _taskbarCreatedMessage;
    private WindowState _windowStateBeforeTray = WindowState.Normal;

    internal MainWindow(MainWindowViewModel viewModel, IGlobalHotkeyService globalHotkeyService)
    {
        InitializeComponent();

        _viewModel = viewModel;
        _globalHotkeyService = globalHotkeyService;
        DataContext = _viewModel;
        _trayMenu = BuildTrayMenu();
        _trayMenu.PlacementTarget = this;

        SourceInitialized += OnSourceInitialized;
        Closing += OnClosing;
        Closed += OnClosed;
    }

    private void OnSourceInitialized(object? sender, EventArgs eventArgs)
    {
        _hwndSource = (HwndSource?)PresentationSource.FromVisual(this);
        if (_hwndSource is null)
        {
            return;
        }

        TryUseDarkTitleBar(_hwndSource.Handle);
        _taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");
        _hwndSource.AddHook(WndProc);
        _globalHotkeyService.AttachWindow(_hwndSource.Handle);
        _viewModel.OnHotkeyWindowReady();
    }

    private async void OnClosing(object? sender, CancelEventArgs eventArgs)
    {
        if (_isShutdownCloseReady)
        {
            return;
        }

        _viewModel.CaptureWindowSettings(this);

        if (_viewModel.IsMinimizeToSystemTrayOnClose && !_isExplicitExitRequested && TryHideToTray())
        {
            eventArgs.Cancel = true;
            return;
        }

        eventArgs.Cancel = true;

        if (_isShutdownInProgress)
        {
            return;
        }

        _isExplicitExitRequested = true;
        _isShutdownInProgress = true;
        IsEnabled = false;

        try
        {
            await _viewModel.ShutdownAsync();
        }
        finally
        {
            _isShutdownCloseReady = true;
            _isShutdownInProgress = false;
            Close();
        }
    }

    private void OnClosed(object? sender, EventArgs eventArgs)
    {
        RemoveTrayIcon();

        if (_hwndSource is not null)
        {
            _hwndSource.RemoveHook(WndProc);
            _hwndSource = null;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == TrayCallbackMessage)
        {
            HandleTrayMessage(lParam.ToInt32());
            handled = true;
            return IntPtr.Zero;
        }

        if (_taskbarCreatedMessage != 0 && message == (int)_taskbarCreatedMessage && _isTrayIconVisible)
        {
            _isTrayIconVisible = false;
            _ = TryAddTrayIcon();
            return IntPtr.Zero;
        }

        _globalHotkeyService.TryHandleWindowMessage(message, wParam, ref handled);
        return IntPtr.Zero;
    }

    private ContextMenu BuildTrayMenu()
    {
        var menu = new ContextMenu
        {
            Style = (Style)FindResource(typeof(ContextMenu))
        };
        var menuItemStyle = (Style)FindResource(typeof(MenuItem));

        var openItem = new MenuItem { Header = "Open", Style = menuItemStyle };
        openItem.Click += (_, _) => RestoreFromTray();
        menu.Items.Add(openItem);

        var exitItem = new MenuItem { Header = "Exit", Style = menuItemStyle };
        exitItem.Click += (_, _) => ExitFromTray();
        menu.Items.Add(exitItem);

        return menu;
    }

    private bool TryHideToTray()
    {
        if (!TryAddTrayIcon())
        {
            return false;
        }

        _windowStateBeforeTray = WindowState == WindowState.Minimized ? WindowState.Normal : WindowState;
        ShowInTaskbar = false;
        Hide();
        return true;
    }

    private void RestoreFromTray()
    {
        RemoveTrayIcon();
        ShowInTaskbar = true;
        Show();
        WindowState = _windowStateBeforeTray == WindowState.Minimized ? WindowState.Normal : _windowStateBeforeTray;
        Activate();
    }

    private void ExitFromTray()
    {
        _isExplicitExitRequested = true;
        RemoveTrayIcon();
        ShowInTaskbar = true;
        Close();
    }

    private void HandleTrayMessage(int trayMessage)
    {
        switch (trayMessage)
        {
            case WindowMessageLeftButtonUp:
            case WindowMessageLeftButtonDoubleClick:
                RestoreFromTray();
                break;
            case WindowMessageRightButtonUp:
                ShowTrayMenu();
                break;
        }
    }

    private void ShowTrayMenu()
    {
        if (_hwndSource is not null)
        {
            _ = SetForegroundWindow(_hwndSource.Handle);
        }

        _trayMenu.IsOpen = false;
        _trayMenu.Placement = PlacementMode.MousePoint;
        _trayMenu.IsOpen = true;
    }

    private bool TryAddTrayIcon()
    {
        if (_hwndSource is null)
        {
            return false;
        }

        var data = CreateNotifyIconData(_hwndSource.Handle);
        var command = _isTrayIconVisible ? NotifyIconModify : NotifyIconAdd;
        if (!Shell_NotifyIcon(command, ref data))
        {
            return false;
        }

        _isTrayIconVisible = true;

        return true;
    }

    private void RemoveTrayIcon()
    {
        if (!_isTrayIconVisible || _hwndSource is null)
        {
            return;
        }

        var data = CreateNotifyIconData(_hwndSource.Handle);
        _ = Shell_NotifyIcon(NotifyIconDelete, ref data);
        _isTrayIconVisible = false;
    }

    private static NotifyIconData CreateNotifyIconData(IntPtr hwnd)
    {
        return new NotifyIconData
        {
            cbSize = (uint)Marshal.SizeOf<NotifyIconData>(),
            hWnd = hwnd,
            uID = TrayIconId,
            uFlags = NotifyIconMessage | NotifyIconIcon | NotifyIconTip,
            uCallbackMessage = TrayCallbackMessage,
            hIcon = LoadIcon(IntPtr.Zero, new IntPtr(DefaultApplicationIcon)),
            szTip = "DeceasedSound",
            szInfo = string.Empty,
            szInfoTitle = string.Empty
        };
    }

    private static void TryUseDarkTitleBar(IntPtr hwnd)
    {
        var enabled = 1;
        _ = DwmSetWindowAttribute(hwnd, DwmWindowAttributeUseImmersiveDarkMode, ref enabled, Marshal.SizeOf<int>());
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int attributeValue, int attributeSize);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadIcon(IntPtr instance, IntPtr iconName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterWindowMessage(string message);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;

        public uint dwState;
        public uint dwStateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;

        public uint uVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;

        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    private const int DwmWindowAttributeUseImmersiveDarkMode = 20;
    private const int DefaultApplicationIcon = 32512;
    private const int WindowMessageLeftButtonUp = 0x0202;
    private const int WindowMessageLeftButtonDoubleClick = 0x0203;
    private const int WindowMessageRightButtonUp = 0x0205;
    private const uint NotifyIconAdd = 0x00000000;
    private const uint NotifyIconModify = 0x00000001;
    private const uint NotifyIconDelete = 0x00000002;
    private const uint NotifyIconMessage = 0x00000001;
    private const uint NotifyIconIcon = 0x00000002;
    private const uint NotifyIconTip = 0x00000004;
    private const uint TrayIconId = 1;
    private const uint TrayCallbackMessage = 0x8001;
}
