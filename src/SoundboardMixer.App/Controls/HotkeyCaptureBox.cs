using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SoundboardMixer.App.Services.Hotkeys;

namespace SoundboardMixer.App.Controls;

public sealed class HotkeyCaptureBox : Button
{
    public static readonly DependencyProperty HotkeyTextProperty =
        DependencyProperty.Register(
            nameof(HotkeyText),
            typeof(string),
            typeof(HotkeyCaptureBox),
            new FrameworkPropertyMetadata(
                null,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnHotkeyTextChanged));

    public static readonly DependencyProperty IsCapturingProperty =
        DependencyProperty.Register(
            nameof(IsCapturing),
            typeof(bool),
            typeof(HotkeyCaptureBox),
            new PropertyMetadata(false, OnIsCapturingChanged));

    private const string EmptyDisplayText = "None";
    private readonly HotkeyCaptureSession _captureSession = new();

    public HotkeyCaptureBox()
    {
        Focusable = true;
        IsTabStop = true;
        UpdateDisplay();
    }

    public string? HotkeyText
    {
        get => (string?)GetValue(HotkeyTextProperty);
        set => SetValue(HotkeyTextProperty, value);
    }

    public bool IsCapturing
    {
        get => (bool)GetValue(IsCapturingProperty);
        private set => SetValue(IsCapturingProperty, value);
    }

    protected override void OnClick()
    {
        if (IsCapturing)
        {
            SetCurrentValue(HotkeyTextProperty, null);
            EndCapture();
            return;
        }

        BeginCapture();
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        EndCapture();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (!IsCapturing)
        {
            base.OnPreviewKeyDown(e);
            return;
        }

        e.Handled = true;

        if (_captureSession.ApplyKey(GetActualKey(e), Keyboard.Modifiers) &&
            !string.Equals(HotkeyText, _captureSession.CapturedHotkeyText, StringComparison.Ordinal))
        {
            SetCurrentValue(HotkeyTextProperty, _captureSession.CapturedHotkeyText);
        }

        SetDisplayText(_captureSession.PreviewText);
    }

    private void BeginCapture()
    {
        _captureSession.Reset();
        IsCapturing = true;
        Focus();
        _ = Keyboard.Focus(this);
        SetDisplayText(_captureSession.PreviewText);
    }

    private void EndCapture()
    {
        if (!IsCapturing)
        {
            return;
        }

        IsCapturing = false;
        _captureSession.Reset();
        UpdateDisplay();
    }

    private void UpdateDisplay()
    {
        if (IsCapturing)
        {
            SetDisplayText(_captureSession.PreviewText);
            return;
        }

        SetDisplayText(string.IsNullOrWhiteSpace(HotkeyText) ? EmptyDisplayText : HotkeyText.Trim());
    }

    private void SetDisplayText(string text)
    {
        SetCurrentValue(ContentProperty, text);
    }

    private static Key GetActualKey(KeyEventArgs eventArgs)
    {
        return eventArgs.Key switch
        {
            Key.System => eventArgs.SystemKey,
            Key.ImeProcessed => eventArgs.ImeProcessedKey,
            Key.DeadCharProcessed => eventArgs.DeadCharProcessedKey,
            _ => eventArgs.Key
        };
    }

    private static void OnHotkeyTextChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs eventArgs)
    {
        ((HotkeyCaptureBox)dependencyObject).UpdateDisplay();
    }

    private static void OnIsCapturingChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs eventArgs)
    {
        ((HotkeyCaptureBox)dependencyObject).UpdateDisplay();
    }
}
