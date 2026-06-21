using System.Windows.Input;

namespace SoundboardMixer.App.Services.Hotkeys;

internal sealed class HotkeyGesture
{
    private HotkeyGesture(ModifierKeys modifiers, Key key)
    {
        Modifiers = NormalizeModifiers(modifiers);
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

            if (TryGetModifier(parsedKey, out _))
            {
                error = "A non-modifier key is required";
                return false;
            }

            key = parsedKey;
        }

        if (key is null)
        {
            error = "A non-modifier key is required";
            return false;
        }

        return TryCreate(modifiers, key.Value, out gesture, out error);
    }

    public static bool TryCreate(ModifierKeys modifiers, Key key, out HotkeyGesture? gesture, out string error)
    {
        gesture = null;
        error = string.Empty;

        modifiers = NormalizeModifiers(modifiers);
        key = NormalizeKey(key);

        if (key == Key.None || TryGetModifier(key, out _))
        {
            error = "A non-modifier key is required";
            return false;
        }

        if (KeyInterop.VirtualKeyFromKey(key) == 0)
        {
            error = $"Unsupported key '{key}'";
            return false;
        }

        gesture = new HotkeyGesture(modifiers, key);
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
        var parts = GetModifierDisplayParts(Modifiers);
        parts.Add(KeyToDisplayString(Key));
        return string.Join("+", parts);
    }

    public static bool TryGetModifier(Key key, out ModifierKeys modifier)
    {
        modifier = NormalizeKey(key) switch
        {
            Key.LeftCtrl or Key.RightCtrl => ModifierKeys.Control,
            Key.LeftAlt or Key.RightAlt => ModifierKeys.Alt,
            Key.LeftShift or Key.RightShift => ModifierKeys.Shift,
            Key.LWin or Key.RWin => ModifierKeys.Windows,
            _ => ModifierKeys.None
        };

        return modifier != ModifierKeys.None;
    }

    public static ModifierKeys NormalizeModifiers(ModifierKeys modifiers)
    {
        return modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift | ModifierKeys.Windows);
    }

    public static Key NormalizeKey(Key key)
    {
        return key;
    }

    public static string ModifiersToDisplayString(ModifierKeys modifiers)
    {
        return string.Join("+", GetModifierDisplayParts(NormalizeModifiers(modifiers)));
    }

    private static List<string> GetModifierDisplayParts(ModifierKeys modifiers)
    {
        var parts = new List<string>(4);

        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            parts.Add("Ctrl");
        }

        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            parts.Add("Alt");
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            parts.Add("Shift");
        }

        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            parts.Add("Win");
        }

        return parts;
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
            ["Enter"] = nameof(Key.Return),
            ["Backspace"] = nameof(Key.Back),
            ["Esc"] = nameof(Key.Escape),
            ["Space"] = nameof(Key.Space),
            ["Tab"] = nameof(Key.Tab),
            ["Plus"] = nameof(Key.OemPlus),
            ["Minus"] = nameof(Key.OemMinus),
            ["Tilde"] = nameof(Key.Oem3),
            ["Backtick"] = nameof(Key.Oem3),
            ["`"] = nameof(Key.Oem3),
            ["~"] = nameof(Key.Oem3),
            ["Comma"] = nameof(Key.OemComma),
            [","] = nameof(Key.OemComma),
            ["Period"] = nameof(Key.OemPeriod),
            ["."] = nameof(Key.OemPeriod),
            ["Slash"] = nameof(Key.Oem2),
            ["/"] = nameof(Key.Oem2),
            ["Question"] = nameof(Key.Oem2),
            ["Semicolon"] = nameof(Key.Oem1),
            [";"] = nameof(Key.Oem1),
            ["Quote"] = nameof(Key.Oem7),
            ["Apostrophe"] = nameof(Key.Oem7),
            ["'"] = nameof(Key.Oem7),
            ["LBracket"] = nameof(Key.Oem4),
            ["OpenBracket"] = nameof(Key.Oem4),
            ["["] = nameof(Key.Oem4),
            ["RBracket"] = nameof(Key.Oem6),
            ["CloseBracket"] = nameof(Key.Oem6),
            ["]"] = nameof(Key.Oem6),
            ["Backslash"] = nameof(Key.Oem5),
            ["\\"] = nameof(Key.Oem5),
            ["Pipe"] = nameof(Key.Oem5),
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
            Key.Return => "Enter",
            Key.Back => "Backspace",
            Key.Escape => "Esc",
            Key.Tab => "Tab",
            Key.OemPlus => "Plus",
            Key.OemMinus => "Minus",
            Key.Oem3 => "Tilde",
            Key.OemComma => "Comma",
            Key.OemPeriod => "Period",
            Key.Oem2 => "Slash",
            Key.Oem1 => "Semicolon",
            Key.Oem7 => "Quote",
            Key.Oem4 => "LBracket",
            Key.Oem6 => "RBracket",
            Key.Oem5 => "Backslash",
            _ => name
        };
    }
}
