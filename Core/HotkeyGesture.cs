using System.Windows.Input;

namespace TaskbarMonitor.Core;

/// <summary>A system-wide shortcut: modifiers plus one key. Stored in the settings as "Ctrl+Alt+M".</summary>
public readonly record struct HotkeyGesture(ModifierKeys Modifiers, Key Key)
{
    public static HotkeyGesture Default { get; } = new(ModifierKeys.Control | ModifierKeys.Alt, Key.M);

    /// <summary>Needs Ctrl, Alt or Win: a key with Shift alone would swallow ordinary typing everywhere.</summary>
    public bool IsValid =>
        Key != Key.None && !IsModifierKey(Key)
        && (Modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) != 0;

    /// <summary>Key cap labels in the order Windows shows them.</summary>
    public IReadOnlyList<string> Parts => Key == Key.None ? [.. ModifierNames()] : [.. ModifierNames(), KeyName(Key)];

    public string DisplayText => string.Join(" + ", Parts);

    public override string ToString() => string.Join("+", ModifierNames().Append(Key.ToString()));

    private IEnumerable<string> ModifierNames()
    {
        if (Modifiers.HasFlag(ModifierKeys.Windows))
            yield return "Win";
        if (Modifiers.HasFlag(ModifierKeys.Control))
            yield return "Ctrl";
        if (Modifiers.HasFlag(ModifierKeys.Alt))
            yield return "Alt";
        if (Modifiers.HasFlag(ModifierKeys.Shift))
            yield return "Shift";
    }

    public static bool TryParse(string? text, out HotkeyGesture gesture)
    {
        gesture = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var modifiers = ModifierKeys.None;
        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (int i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].ToLowerInvariant())
            {
                case "ctrl":
                    modifiers |= ModifierKeys.Control;
                    break;
                case "alt":
                    modifiers |= ModifierKeys.Alt;
                    break;
                case "shift":
                    modifiers |= ModifierKeys.Shift;
                    break;
                case "win":
                    modifiers |= ModifierKeys.Windows;
                    break;
                default:
                    return false;
            }
        }
        if (parts.Length == 0 || !Enum.TryParse(parts[^1], ignoreCase: true, out Key key))
            return false;
        gesture = new HotkeyGesture(modifiers, key);
        return gesture.IsValid;
    }

    public static bool IsModifierKey(Key key) => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
        or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System;

    private static string KeyName(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((int)(key - Key.D0)).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => $"Num {(int)(key - Key.NumPad0)}",
        Key.Space => "Пробел",
        Key.Enter => "Enter",
        Key.Back => "Backspace",
        Key.Delete => "Del",
        Key.Insert => "Ins",
        Key.PageUp => "Page Up",
        Key.PageDown => "Page Down",
        Key.Left => "←",
        Key.Up => "↑",
        Key.Right => "→",
        Key.Down => "↓",
        Key.Snapshot => "PrtScn",
        Key.Scroll => "Scroll Lock",
        Key.OemPlus => "=",
        Key.OemMinus => "-",
        Key.OemComma => ",",
        Key.OemPeriod => ".",
        Key.OemQuestion => "/",
        Key.OemSemicolon => ";",
        Key.OemTilde => "`",
        Key.OemOpenBrackets => "[",
        Key.OemCloseBrackets => "]",
        Key.OemQuotes => "'",
        Key.OemPipe => "\\",
        Key.Multiply => "Num *",
        Key.Add => "Num +",
        Key.Subtract => "Num -",
        Key.Divide => "Num /",
        _ => key.ToString(),
    };
}
