using System.Windows.Input;

namespace SoundboardMixer.App.Services.Hotkeys;

internal sealed class HotkeyGesture
{
    private HotkeyGesture(ModifierKeys modifiers, Key key)
    {
        Modifiers = modifiers;
        Key = key;
    }

    public ModifierKeys Modifiers { get; }

    public Key Key { get; }

    public static bool TryParse(string? text, out HotkeyGesture? gesture, out string error)
    {
        gesture = null;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(text))
        {
            error = "Hotkey is empty";
            return false;
        }

        var tokens = text
            .Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .ToArray();

        if (tokens.Length == 0)
        {
            error = "Hotkey is empty";
            return false;
        }

        var modifiers = ModifierKeys.None;
        Key? key = null;

        foreach (var token in tokens)
        {
            if (TryParseModifier(token, out var modifier))
            {
                if ((modifiers & modifier) == modifier)
                {
                    error = $"Duplicate modifier '{token}'";
                    return false;
                }

                modifiers |= modifier;
                continue;
            }

            if (key is not null)
            {
                error = "Specify only one non-modifier key";
                return false;
            }

            if (!TryParseKey(token, out var parsedKey))
            {
                error = $"Unsupported key '{token}'";
                return false;
            }

            key = parsedKey;
        }

        if (key is null)
        {
            error = "A non-modifier key is required";
            return false;
        }

        gesture = new HotkeyGesture(modifiers, key.Value);
        return true;
    }

    public uint GetNativeModifiers()
    {
        var modifiers = 0u;

        if (Modifiers.HasFlag(ModifierKeys.Alt))
        {
            modifiers |= 0x0001;
        }

        if (Modifiers.HasFlag(ModifierKeys.Control))
        {
            modifiers |= 0x0002;
        }

        if (Modifiers.HasFlag(ModifierKeys.Shift))
        {
            modifiers |= 0x0004;
        }

        if (Modifiers.HasFlag(ModifierKeys.Windows))
        {
            modifiers |= 0x0008;
        }

        return modifiers;
    }

    public uint GetVirtualKey() => unchecked((uint)KeyInterop.VirtualKeyFromKey(Key));

    public override string ToString()
    {
        var parts = new List<string>(5);

        if (Modifiers.HasFlag(ModifierKeys.Control))
        {
            parts.Add("Ctrl");
        }

        if (Modifiers.HasFlag(ModifierKeys.Alt))
        {
            parts.Add("Alt");
        }

        if (Modifiers.HasFlag(ModifierKeys.Shift))
        {
            parts.Add("Shift");
        }

        if (Modifiers.HasFlag(ModifierKeys.Windows))
        {
            parts.Add("Win");
        }

        parts.Add(KeyToDisplayString(Key));
        return string.Join("+", parts);
    }

    private static bool TryParseModifier(string token, out ModifierKeys modifier)
    {
        modifier = token.ToLowerInvariant() switch
        {
            "ctrl" or "control" => ModifierKeys.Control,
            "alt" => ModifierKeys.Alt,
            "shift" => ModifierKeys.Shift,
            "win" or "windows" => ModifierKeys.Windows,
            _ => ModifierKeys.None
        };

        return modifier != ModifierKeys.None;
    }

    private static bool TryParseKey(string token, out Key key)
    {
        var normalized = token.Replace(" ", string.Empty, StringComparison.OrdinalIgnoreCase);

        if (normalized.Length == 1 && char.IsLetter(normalized[0]))
        {
            return Enum.TryParse(normalized.ToUpperInvariant(), ignoreCase: true, out key);
        }

        if (normalized.Length == 1 && char.IsDigit(normalized[0]))
        {
            return Enum.TryParse($"D{normalized}", ignoreCase: true, out key);
        }

        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["PgUp"] = nameof(Key.PageUp),
            ["PageUp"] = nameof(Key.PageUp),
            ["PgDn"] = nameof(Key.PageDown),
            ["PageDown"] = nameof(Key.PageDown),
            ["Del"] = nameof(Key.Delete),
            ["Ins"] = nameof(Key.Insert),
            ["Esc"] = nameof(Key.Escape),
            ["Space"] = nameof(Key.Space),
            ["Plus"] = nameof(Key.OemPlus),
            ["Minus"] = nameof(Key.OemMinus),
            ["Num0"] = nameof(Key.NumPad0),
            ["Num1"] = nameof(Key.NumPad1),
            ["Num2"] = nameof(Key.NumPad2),
            ["Num3"] = nameof(Key.NumPad3),
            ["Num4"] = nameof(Key.NumPad4),
            ["Num5"] = nameof(Key.NumPad5),
            ["Num6"] = nameof(Key.NumPad6),
            ["Num7"] = nameof(Key.NumPad7),
            ["Num8"] = nameof(Key.NumPad8),
            ["Num9"] = nameof(Key.NumPad9)
        };

        if (aliases.TryGetValue(normalized, out var alias))
        {
            return Enum.TryParse(alias, ignoreCase: true, out key);
        }

        return Enum.TryParse(normalized, ignoreCase: true, out key);
    }

    private static string KeyToDisplayString(Key key)
    {
        var name = key.ToString();

        if (name.Length == 2 && name[0] == 'D' && char.IsDigit(name[1]))
        {
            return name[1].ToString();
        }

        if (name.StartsWith("NumPad", StringComparison.OrdinalIgnoreCase))
        {
            return $"Num{name["NumPad".Length..]}";
        }

        return key switch
        {
            Key.PageUp => "PgUp",
            Key.PageDown => "PgDn",
            Key.Delete => "Del",
            Key.Insert => "Ins",
            Key.Escape => "Esc",
            Key.OemPlus => "Plus",
            Key.OemMinus => "Minus",
            _ => name
        };
    }
}
