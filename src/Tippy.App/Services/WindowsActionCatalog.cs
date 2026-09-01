using Tippy.App.Models;

namespace Tippy.App.Services;

public static class WindowsActionCatalog
{
    public static IReadOnlyList<KnownWindowsAction> Create()
    {
        var actions = new List<KnownWindowsAction>
        {
            Action("Editing", "Copy", "Ctrl+C", "Copy the selected item or text"),
            Action("Editing", "Cut", "Ctrl+X", "Cut the selected item or text"),
            Action("Editing", "Paste", "Ctrl+V", "Paste from the clipboard"),
            Action("Editing", "Undo", "Ctrl+Z", "Undo the last action"),
            Action("Editing", "Redo", "Ctrl+Y", "Redo the last action"),
            Action("Editing", "Select all", "Ctrl+A", "Select everything in the active view"),
            Action("Editing", "Find", "Ctrl+F", "Find text or items"),
            Action("Editing", "Save", "Ctrl+S", "Save the active document"),
            Action("Editing", "Save as", "Ctrl+Shift+S", "Open Save As in many applications"),
            Action("Editing", "Open", "Ctrl+O", "Open a file"),
            Action("Editing", "New", "Ctrl+N", "Create a new document or window"),
            Action("Editing", "Print", "Ctrl+P", "Print the active document"),
            Action("Windows", "Show desktop", "Win+D", "Show or restore the desktop"),
            Action("Windows", "Open File Explorer", "Win+E", "Open File Explorer"),
            Action("Windows", "Open Settings", "Win+I", "Open Windows Settings"),
            Action("Windows", "Lock computer", "Win+L", "Lock the current Windows session"),
            Action("Windows", "Run command", "Win+R", "Open the Run dialog"),
            Action("Windows", "Windows Search", "Win+S", "Open Windows Search"),
            Action("Windows", "Quick Link menu", "Win+X", "Open the power-user menu"),
            Action("Windows", "Clipboard history", "Win+V", "Open clipboard history"),
            Action("Windows", "Emoji and symbols", "Win+.", "Open the emoji and symbols panel"),
            Action("Windows", "Screen snipping", "Win+Shift+S", "Open the screen snipping overlay"),
            Action("Windows", "Task Manager", "Ctrl+Shift+Escape", "Open Task Manager"),
            Action("Windows", "Switch apps", "Alt+Tab", "Move to the next open application"),
            Action("Windows", "Switch apps backward", "Alt+Shift+Tab", "Move to the previous open application"),
            Action("Windows", "Close active window", "Alt+F4", "Close the active application window"),
            Action("Windows", "Minimize all windows", "Win+M", "Minimize all windows"),
            Action("Windows", "Restore minimized windows", "Win+Shift+M", "Restore windows minimized with Win+M"),
            Action("Windows", "Snap window left", "Win+Left", "Snap the active window to the left"),
            Action("Windows", "Snap window right", "Win+Right", "Snap the active window to the right"),
            Action("Windows", "Maximize window", "Win+Up", "Maximize the active window"),
            Action("Windows", "Minimize window", "Win+Down", "Minimize or restore the active window"),
            Action("Virtual desktops", "New virtual desktop", "Win+Ctrl+D", "Create a virtual desktop"),
            Action("Virtual desktops", "Desktop to the left", "Win+Ctrl+Left", "Switch to the desktop on the left"),
            Action("Virtual desktops", "Desktop to the right", "Win+Ctrl+Right", "Switch to the desktop on the right"),
            Action("Virtual desktops", "Close virtual desktop", "Win+Ctrl+F4", "Close the current virtual desktop"),
            Action("Browser", "New tab", "Ctrl+T", "Open a new browser tab"),
            Action("Browser", "Reopen closed tab", "Ctrl+Shift+T", "Restore the most recently closed tab"),
            Action("Browser", "Close tab", "Ctrl+W", "Close the current tab"),
            Action("Browser", "Next tab", "Ctrl+Tab", "Move to the next tab"),
            Action("Browser", "Previous tab", "Ctrl+Shift+Tab", "Move to the previous tab"),
            Action("Browser", "Address bar", "Ctrl+L", "Focus the address bar"),
            Action("Browser", "Refresh", "Ctrl+R", "Refresh the current page"),
            Action("Browser", "Zoom in", "Ctrl+Shift+=", "Increase page or document zoom"),
            Action("Browser", "Zoom out", "Ctrl+-", "Decrease page or document zoom"),
            Action("Browser", "Reset zoom", "Ctrl+0", "Reset page or document zoom"),
            Action("Media", "Play or pause", "MediaPlayPause", "Toggle media playback"),
            Action("Media", "Next track", "MediaNext", "Skip to the next media track"),
            Action("Media", "Previous track", "MediaPrevious", "Return to the previous media track"),
            Action("Media", "Stop playback", "MediaStop", "Stop media playback"),
            Action("Media", "Volume up", "VolumeUp", "Raise system volume"),
            Action("Media", "Volume down", "VolumeDown", "Lower system volume"),
            Action("Media", "Mute volume", "VolumeMute", "Toggle system mute"),
            Action("Navigation", "Back", "Alt+Left", "Navigate back"),
            Action("Navigation", "Forward", "Alt+Right", "Navigate forward"),
            Action("Navigation", "Refresh key", "F5", "Refresh the active view"),
            Action("Navigation", "Help key", "F1", "Open help in many applications"),
            Action("Navigation", "Rename selected item", "F2", "Rename the selected item in Explorer"),
            Action("Navigation", "Address bar dropdown", "F4", "Open the address bar list in Explorer"),
            Action("Navigation", "Cycle screen elements", "F6", "Cycle through elements in the active window"),
            Action("Navigation", "Full screen", "F11", "Toggle full-screen mode in many applications")
        };

        foreach (char letter in Enumerable.Range('A', 26).Select(value => (char)value))
            actions.Add(Key($"Letter {letter}", letter.ToString()));
        foreach (char digit in Enumerable.Range('0', 10).Select(value => (char)value))
            actions.Add(Key($"Number {digit}", digit.ToString()));
        for (int index = 1; index <= 24; index++) actions.Add(Key($"Function key F{index}", $"F{index}"));
        for (int index = 0; index <= 9; index++) actions.Add(Key($"Numeric keypad {index}", $"Numpad{index}"));

        string[] namedKeys =
        [
            "Enter", "Escape", "Tab", "Space", "Backspace", "Delete", "Insert", "Home", "End",
            "PageUp", "PageDown", "Left", "Right", "Up", "Down", "CapsLock", "NumLock", "ScrollLock",
            "PrintScreen", "Pause", "Apps", "Multiply", "Add", "Subtract", "Decimal", "Divide",
            "BrowserBack", "BrowserForward", "BrowserRefresh", "BrowserStop", "BrowserSearch", "BrowserFavorites",
            "BrowserHome", "LaunchMail", "LaunchApp1", "LaunchApp2", "MediaSelect", "VolumeMute", "VolumeDown",
            "VolumeUp", "MediaNext", "MediaPrevious", "MediaStop", "MediaPlayPause",
            ";", "=", ",", "-", ".", "/", "`", "[", "\\", "]", "'"
        ];
        actions.AddRange(namedKeys.Select(key => Key($"{key} key", key)));
        return actions.OrderBy(action => action.Category).ThenBy(action => action.Name).ToArray();
    }

    private static KnownWindowsAction Action(string category, string name, string shortcut, string description) =>
        new(category, name, shortcut, description);

    private static KnownWindowsAction Key(string name, string shortcut) =>
        new("Keyboard keys", name, shortcut, $"Press the {shortcut} key");
}
