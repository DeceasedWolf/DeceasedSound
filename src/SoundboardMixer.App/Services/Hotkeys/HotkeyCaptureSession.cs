using System.Windows.Input;

namespace SoundboardMixer.App.Services.Hotkeys;

internal sealed class HotkeyCaptureSession
{
    public const string EmptyPreviewText = "Press shortcut";

    private ModifierKeys _modifiers;
    private Key? _key;

    public string? CapturedHotkeyText { get; private set; }

    public string PreviewText { get; private set; } = EmptyPreviewText;

    public void Reset()
    {
        _modifiers = ModifierKeys.None;
        _key = null;
        CapturedHotkeyText = null;
        PreviewText = EmptyPreviewText;
    }

    public bool ApplyKey(Key key, ModifierKeys currentModifiers)
    {
        key = HotkeyGesture.NormalizeKey(key);
        var nextModifiers = HotkeyGesture.NormalizeModifiers(currentModifiers);

        if (HotkeyGesture.TryGetModifier(key, out var modifier))
        {
            _modifiers = HotkeyGesture.NormalizeModifiers(_modifiers | nextModifiers | modifier);
            RefreshPreview();
            return CapturedHotkeyText is not null;
        }

        _modifiers = HotkeyGesture.NormalizeModifiers(_modifiers | nextModifiers);

        if (HotkeyGesture.TryCreate(_modifiers, key, out var gesture, out _) && gesture is not null)
        {
            _key = gesture.Key;
            CapturedHotkeyText = gesture.ToString();
            PreviewText = CapturedHotkeyText;
            return true;
        }

        RefreshPreview();
        return CapturedHotkeyText is not null;
    }

    private void RefreshPreview()
    {
        if (_key is not null &&
            HotkeyGesture.TryCreate(_modifiers, _key.Value, out var gesture, out _) &&
            gesture is not null)
        {
            CapturedHotkeyText = gesture.ToString();
            PreviewText = CapturedHotkeyText;
            return;
        }

        CapturedHotkeyText = null;
        var modifiersText = HotkeyGesture.ModifiersToDisplayString(_modifiers);
        PreviewText = string.IsNullOrWhiteSpace(modifiersText)
            ? EmptyPreviewText
            : $"{modifiersText}+...";
    }
}
