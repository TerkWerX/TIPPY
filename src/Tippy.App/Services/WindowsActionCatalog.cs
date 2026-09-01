using Tippy.App.Models;

namespace Tippy.App.Services;

public static class WindowsActionCatalog
{
    public static IReadOnlyList<KnownWindowsAction> Create()
    {
        var actions = new List<KnownWindowsAction>
        {
            Action("Editing & files", "Copy", "Ctrl+C", "Copy the selected item or text"),
            Action("Editing & files", "Cut", "Ctrl+X", "Cut the selected item or text"),
            Action("Editing & files", "Paste", "Ctrl+V", "Paste from the clipboard"),
            Action("Editing & files", "Undo", "Ctrl+Z", "Undo the last action"),
            Action("Editing & files", "Redo", "Ctrl+Y", "Redo the last action"),
            Action("Editing & files", "Select all", "Ctrl+A", "Select everything in the active view"),
            Action("Editing & files", "Find", "Ctrl+F", "Find text or items"),
            Action("Editing & files", "Save", "Ctrl+S", "Save the active document"),
            Action("Editing & files", "Save as", "Ctrl+Shift+S", "Open Save As in many applications"),
            Action("Editing & files", "Open", "Ctrl+O", "Open a file"),
            Action("Editing & files", "New", "Ctrl+N", "Create a new document or window"),
            Action("Editing & files", "Print", "Ctrl+P", "Print the active document"),

            Action("Windows & system", "Show desktop", "Win+D", "Show or restore the desktop"),
            Action("Windows & system", "Open File Explorer", "Win+E", "Open File Explorer"),
            Action("Windows & system", "Open Settings", "Win+I", "Open Windows Settings"),
            Action("Windows & system", "Lock computer", "Win+L", "Lock the current Windows session"),
            Action("Windows & system", "Run command", "Win+R", "Open the Run dialog"),
            Action("Windows & system", "Windows Search", "Win+S", "Open Windows Search"),
            Action("Windows & system", "Quick Link menu", "Win+X", "Open the power-user menu"),
            Action("Windows & system", "Clipboard history", "Win+V", "Open clipboard history"),
            Action("Windows & system", "Emoji and symbols", "Win+.", "Open the emoji and symbols panel"),
            Action("Windows & system", "Screen snipping", "Win+Shift+S", "Open the screen snipping overlay"),
            Action("Windows & system", "Task Manager", "Ctrl+Shift+Escape", "Open Task Manager"),
            Action("Windows & system", "Switch apps", "Alt+Tab", "Move to the next open application"),
            Action("Windows & system", "Switch apps backward", "Alt+Shift+Tab", "Move to the previous open application"),
            Action("Windows & system", "Close active window", "Alt+F4", "Close the active application window"),
            Action("Windows & system", "Minimize all windows", "Win+M", "Minimize all windows"),
            Action("Windows & system", "Restore minimized windows", "Win+Shift+M", "Restore windows minimized with Win+M"),
            Action("Windows & system", "Snap window left", "Win+Left", "Snap the active window to the left"),
            Action("Windows & system", "Snap window right", "Win+Right", "Snap the active window to the right"),
            Action("Windows & system", "Maximize window", "Win+Up", "Maximize the active window"),
            Action("Windows & system", "Minimize window", "Win+Down", "Minimize or restore the active window"),

            Action("Virtual desktops", "New virtual desktop", "Win+Ctrl+D", "Create a virtual desktop"),
            Action("Virtual desktops", "Desktop to the left", "Win+Ctrl+Left", "Switch to the desktop on the left"),
            Action("Virtual desktops", "Desktop to the right", "Win+Ctrl+Right", "Switch to the desktop on the right"),
            Action("Virtual desktops", "Close virtual desktop", "Win+Ctrl+F4", "Close the current virtual desktop"),

            Action("Web browsers & tabs", "New tab", "Ctrl+T", "Open a new browser tab"),
            Action("Web browsers & tabs", "Reopen closed tab", "Ctrl+Shift+T", "Restore the most recently closed tab"),
            Action("Web browsers & tabs", "Close tab", "Ctrl+W", "Close the current tab"),
            Action("Web browsers & tabs", "Next tab", "Ctrl+Tab", "Move to the next tab"),
            Action("Web browsers & tabs", "Previous tab", "Ctrl+Shift+Tab", "Move to the previous tab"),
            Action("Web browsers & tabs", "Address bar", "Ctrl+L", "Focus the address bar"),
            Action("Web browsers & tabs", "Refresh", "Ctrl+R", "Refresh the current page"),
            Action("Web browsers & tabs", "Zoom in", "Ctrl+Shift+=", "Increase page or document zoom"),
            Action("Web browsers & tabs", "Zoom out", "Ctrl+-", "Decrease page or document zoom"),
            Action("Web browsers & tabs", "Reset zoom", "Ctrl+0", "Reset page or document zoom"),

            Action("Media & volume", "Play or pause", "MediaPlayPause", "Toggle media playback"),
            Action("Media & volume", "Next track", "MediaNext", "Skip to the next media track"),
            Action("Media & volume", "Previous track", "MediaPrevious", "Return to the previous media track"),
            Action("Media & volume", "Stop playback", "MediaStop", "Stop media playback"),
            Action("Media & volume", "Volume up", "VolumeUp", "Raise system volume"),
            Action("Media & volume", "Volume down", "VolumeDown", "Lower system volume"),
            Action("Media & volume", "Mute volume", "VolumeMute", "Toggle system mute"),

            Action("Navigation & display", "Back", "Alt+Left", "Navigate back"),
            Action("Navigation & display", "Forward", "Alt+Right", "Navigate forward"),
            Action("Navigation & display", "Refresh key", "F5", "Refresh the active view"),
            Action("Navigation & display", "Help key", "F1", "Open help in many applications"),
            Action("Navigation & display", "Rename selected item", "F2", "Rename the selected item in Explorer"),
            Action("Navigation & display", "Address bar dropdown", "F4", "Open the address bar list in Explorer"),
            Action("Navigation & display", "Cycle screen elements", "F6", "Cycle through elements in the active window"),
            Action("Navigation & display", "Full screen", "F11", "Toggle full-screen mode in many applications")
        };

        foreach (char letter in Enumerable.Range('A', 26).Select(value => (char)value))
            actions.Add(Key("Letters", $"Letter {letter}", letter.ToString()));
        foreach (char digit in Enumerable.Range('0', 10).Select(value => (char)value))
            actions.Add(Key("Number row", $"Number {digit}", digit.ToString()));
        for (var index = 1; index <= 24; index++)
            actions.Add(Key("Function keys", $"Function key F{index}", $"F{index}"));
        for (var index = 0; index <= 9; index++)
            actions.Add(Key("Numeric keypad", $"Numeric keypad {index}", $"Numpad{index}"));

        (string Category, string Key)[] namedKeys =
        [
            ("Navigation & editing keys", "Enter"), ("Navigation & editing keys", "Escape"),
            ("Navigation & editing keys", "Tab"), ("Navigation & editing keys", "Space"),
            ("Navigation & editing keys", "Backspace"), ("Navigation & editing keys", "Delete"),
            ("Navigation & editing keys", "Insert"), ("Navigation & editing keys", "Home"),
            ("Navigation & editing keys", "End"), ("Navigation & editing keys", "PageUp"),
            ("Navigation & editing keys", "PageDown"), ("Navigation & editing keys", "Left"),
            ("Navigation & editing keys", "Right"), ("Navigation & editing keys", "Up"),
            ("Navigation & editing keys", "Down"),
            ("Lock & system keys", "CapsLock"), ("Lock & system keys", "NumLock"),
            ("Lock & system keys", "ScrollLock"), ("Lock & system keys", "PrintScreen"),
            ("Lock & system keys", "Pause"), ("Lock & system keys", "Apps"),
            ("Numeric keypad", "Multiply"), ("Numeric keypad", "Add"),
            ("Numeric keypad", "Subtract"), ("Numeric keypad", "Decimal"), ("Numeric keypad", "Divide"),
            ("Browser & launch keys", "BrowserBack"), ("Browser & launch keys", "BrowserForward"),
            ("Browser & launch keys", "BrowserRefresh"), ("Browser & launch keys", "BrowserStop"),
            ("Browser & launch keys", "BrowserSearch"), ("Browser & launch keys", "BrowserFavorites"),
            ("Browser & launch keys", "BrowserHome"), ("Browser & launch keys", "LaunchMail"),
            ("Browser & launch keys", "LaunchApp1"), ("Browser & launch keys", "LaunchApp2"),
            ("Media & volume keys", "MediaSelect"), ("Media & volume keys", "VolumeMute"),
            ("Media & volume keys", "VolumeDown"), ("Media & volume keys", "VolumeUp"),
            ("Media & volume keys", "MediaNext"), ("Media & volume keys", "MediaPrevious"),
            ("Media & volume keys", "MediaStop"), ("Media & volume keys", "MediaPlayPause"),
            ("Punctuation & symbols", ";"), ("Punctuation & symbols", "="),
            ("Punctuation & symbols", ","), ("Punctuation & symbols", "-"),
            ("Punctuation & symbols", "."), ("Punctuation & symbols", "/"),
            ("Punctuation & symbols", "`"), ("Punctuation & symbols", "["),
            ("Punctuation & symbols", "\\"), ("Punctuation & symbols", "]"),
            ("Punctuation & symbols", "'")
        ];
        actions.AddRange(namedKeys.Select(item => Key(item.Category, $"{item.Key} key", item.Key)));
        return actions.ToArray();
    }

    private static KnownWindowsAction Action(string category, string name, string shortcut, string description) =>
        new(category, name, shortcut, description);

    private static KnownWindowsAction Key(string category, string name, string shortcut) =>
        new(category, name, shortcut, $"Press the {shortcut} key", true);
}
