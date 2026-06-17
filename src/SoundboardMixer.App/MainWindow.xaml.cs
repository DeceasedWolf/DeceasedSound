using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using SoundboardMixer.App.Services.Hotkeys;
using SoundboardMixer.App.ViewModels;

namespace SoundboardMixer.App;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly IGlobalHotkeyService _globalHotkeyService;
    private HwndSource? _hwndSource;

    internal MainWindow(MainWindowViewModel viewModel, IGlobalHotkeyService globalHotkeyService)
    {
        InitializeComponent();

        _viewModel = viewModel;
        _globalHotkeyService = globalHotkeyService;
        DataContext = _viewModel;

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
        _hwndSource.AddHook(WndProc);
        _globalHotkeyService.AttachWindow(_hwndSource.Handle);
        _viewModel.OnHotkeyWindowReady();
    }

    private void OnClosing(object? sender, CancelEventArgs eventArgs)
    {
        _viewModel.CaptureWindowSettings(this);
    }

    private void OnClosed(object? sender, EventArgs eventArgs)
    {
        if (_hwndSource is not null)
        {
            _hwndSource.RemoveHook(WndProc);
            _hwndSource = null;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        _globalHotkeyService.TryHandleWindowMessage(message, wParam, ref handled);
        return IntPtr.Zero;
    }

    private static void TryUseDarkTitleBar(IntPtr hwnd)
    {
        var enabled = 1;
        _ = DwmSetWindowAttribute(hwnd, DwmWindowAttributeUseImmersiveDarkMode, ref enabled, Marshal.SizeOf<int>());
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int attributeValue, int attributeSize);

    private const int DwmWindowAttributeUseImmersiveDarkMode = 20;
}
