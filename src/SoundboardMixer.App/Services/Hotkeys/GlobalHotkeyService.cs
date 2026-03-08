using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SoundboardMixer.App.Services.Hotkeys;

internal interface IGlobalHotkeyService : IDisposable
{
    event EventHandler<string>? HotkeyPressed;

    void AttachWindow(IntPtr handle);

    IReadOnlyDictionary<string, string> UpdateBindings(IEnumerable<HotkeyBindingDefinition> bindings);

    bool TryHandleWindowMessage(int message, IntPtr wParam, ref bool handled);
}

internal sealed record HotkeyBindingDefinition(string ClipId, string? HotkeyText);

internal sealed class GlobalHotkeyService : IGlobalHotkeyService
{
    private const int WmHotKey = 0x0312;
    private readonly ILogService _logService;
    private readonly Dictionary<int, string> _registeredHotkeys = [];
    private List<HotkeyBindingDefinition> _bindings = [];
    private IntPtr _windowHandle;
    private int _nextHotkeyId = 0x2800;

    public GlobalHotkeyService(ILogService logService)
    {
        _logService = logService;
    }

    public event EventHandler<string>? HotkeyPressed;

    public void AttachWindow(IntPtr handle)
    {
        _windowHandle = handle;
        ApplyBindings();
    }

    public IReadOnlyDictionary<string, string> UpdateBindings(IEnumerable<HotkeyBindingDefinition> bindings)
    {
        _bindings = bindings.ToList();
        return ApplyBindings();
    }

    public bool TryHandleWindowMessage(int message, IntPtr wParam, ref bool handled)
    {
        if (message != WmHotKey)
        {
            return false;
        }

        var registrationId = wParam.ToInt32();
        if (_registeredHotkeys.TryGetValue(registrationId, out var clipId))
        {
            handled = true;
            HotkeyPressed?.Invoke(this, clipId);
            return true;
        }

        return false;
    }

    public void Dispose()
    {
        UnregisterAll();
    }

    private IReadOnlyDictionary<string, string> ApplyBindings()
    {
        UnregisterAll();
        _nextHotkeyId = 0x2800;
        var statuses = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (_windowHandle == IntPtr.Zero)
        {
            foreach (var binding in _bindings)
            {
                statuses[binding.ClipId] = string.IsNullOrWhiteSpace(binding.HotkeyText) ? string.Empty : "Pending window";
            }

            return statuses;
        }

        var seenGestures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var binding in _bindings)
        {
            if (string.IsNullOrWhiteSpace(binding.HotkeyText))
            {
                statuses[binding.ClipId] = string.Empty;
                continue;
            }

            if (!HotkeyGesture.TryParse(binding.HotkeyText, out var gesture, out var parseError))
            {
                statuses[binding.ClipId] = parseError;
                continue;
            }

            if (gesture is null)
            {
                statuses[binding.ClipId] = "Hotkey parse failed";
                continue;
            }

            var gestureText = gesture.ToString();
            if (!seenGestures.Add(gestureText))
            {
                statuses[binding.ClipId] = "Duplicate in app";
                continue;
            }

            var registrationId = _nextHotkeyId++;
            if (!RegisterHotKey(_windowHandle, registrationId, gesture.GetNativeModifiers(), gesture.GetVirtualKey()))
            {
                var win32Error = Marshal.GetLastWin32Error();
                var status = win32Error == 1409 ? "Already in use" : $"Win32 error {win32Error}";
                statuses[binding.ClipId] = status;
                _logService.Warning($"Failed to register global hotkey '{gestureText}'. {status}");
                continue;
            }

            _registeredHotkeys[registrationId] = binding.ClipId;
            statuses[binding.ClipId] = "Registered";
        }

        return statuses;
    }

    private void UnregisterAll()
    {
        if (_windowHandle == IntPtr.Zero)
        {
            _registeredHotkeys.Clear();
            return;
        }

        foreach (var registrationId in _registeredHotkeys.Keys.ToList())
        {
            if (!UnregisterHotKey(_windowHandle, registrationId))
            {
                var win32Error = Marshal.GetLastWin32Error();
                if (win32Error != 0)
                {
                    _logService.Warning(new Win32Exception(win32Error).Message);
                }
            }
        }

        _registeredHotkeys.Clear();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
