using System.Windows.Input;

namespace Tippy.App.Services;

public static class ShortcutFormatter
{
    public static string FromKey(Key key, ModifierKeys modifiers, bool requireModifier)
    {
        var keyName = KeyName(key);
        if (string.IsNullOrEmpty(keyName))
        {
            throw new ArgumentException("That key is not supported for this shortcut.");
        }
        List<string> parts = [];
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        if (requireModifier && parts.Count == 0)
        {
            throw new ArgumentException("Use at least one modifier (Ctrl, Alt, Shift, or Win)." );
        }
        parts.Add(keyName);
        return string.Join("+", parts);
    }

    public static bool IsModifier(Key key) => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
        or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;

    public static string? KeyName(Key key)
    {
        if (key is >= Key.A and <= Key.Z) return key.ToString();
        if (key is >= Key.D0 and <= Key.D9) return ((int)(key - Key.D0)).ToString();
        if (key is >= Key.F1 and <= Key.F24) return key.ToString();
        if (key is >= Key.NumPad0 and <= Key.NumPad9) return $"Numpad{(int)(key - Key.NumPad0)}";
        return key switch
        {
            Key.LeftCtrl or Key.RightCtrl => "Ctrl", Key.LeftAlt or Key.RightAlt => "Alt",
            Key.LeftShift or Key.RightShift => "Shift", Key.LWin or Key.RWin => "Win",
            Key.Enter => "Enter", Key.Escape => "Escape", Key.Tab => "Tab", Key.Space => "Space",
            Key.Back => "Backspace", Key.Delete => "Delete", Key.Insert => "Insert",
            Key.Home => "Home", Key.End => "End", Key.PageUp => "PageUp", Key.PageDown => "PageDown",
            Key.Left => "Left", Key.Right => "Right", Key.Up => "Up", Key.Down => "Down",
            Key.CapsLock => "CapsLock", Key.NumLock => "NumLock", Key.Scroll => "ScrollLock",
            Key.PrintScreen => "PrintScreen", Key.Pause => "Pause", Key.Apps => "Apps",
            Key.Multiply => "Multiply", Key.Add => "Add", Key.Subtract => "Subtract",
            Key.Decimal => "Decimal", Key.Divide => "Divide",
            Key.OemSemicolon => ";", Key.OemPlus => "=", Key.OemComma => ",", Key.OemMinus => "-",
            Key.OemPeriod => ".", Key.OemQuestion => "/", Key.OemTilde => "`",
            Key.OemOpenBrackets => "[", Key.OemPipe => "\\", Key.OemCloseBrackets => "]",
            Key.OemQuotes => "'", _ => null
        };
    }
}
