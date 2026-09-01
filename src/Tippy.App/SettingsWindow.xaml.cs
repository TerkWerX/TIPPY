using System.Windows;
using System.Windows.Input;
using Tippy.App.Services;

namespace Tippy.App;

public partial class SettingsWindow : Window
{
    private readonly VirtualGamepadService _gamepad;
    private bool _capturing;

    public SettingsWindow(string bankHotkey, bool startMinimized, VirtualGamepadService gamepad)
    {
        InitializeComponent();
        BankHotkey = bankHotkey;
        _gamepad = gamepad;
        HotkeyBox.Text = bankHotkey;
        StartMinimizedCheckBox.IsChecked = startMinimized;
        GamepadStatusText.Text = gamepad.Status;
        PreviewKeyDown += SettingsWindow_PreviewKeyDown;
    }

    public string BankHotkey { get; private set; }
    public bool StartMinimized => StartMinimizedCheckBox.IsChecked == true;

    private void CaptureHotkey_Click(object sender, RoutedEventArgs e)
    {
        _capturing = true;
        CaptureHotkeyButton.Content = "Press keys…";
        HotkeyHint.Text = "Press a modified shortcut; Escape cancels.";
        Focus();
        Keyboard.Focus(this);
    }

    private void SettingsWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_capturing || ShortcutFormatter.IsModifier(e.Key)) return;
        e.Handled = true;
        if (e.Key == Key.Escape)
        {
            EndCapture("Change canceled.");
            return;
        }
        try
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            BankHotkey = ShortcutFormatter.FromKey(key, Keyboard.Modifiers, true);
            HotkeyBox.Text = BankHotkey;
            EndCapture("Shortcut captured. Save to apply it.");
        }
        catch (Exception exception)
        {
            EndCapture(exception.Message);
        }
    }

    private async void TestGamepad_Click(object sender, RoutedEventArgs e)
    {
        if (!_gamepad.TryInitialize())
        {
            GamepadStatusText.Text = _gamepad.Status;
            return;
        }
        try
        {
            GamepadStatusText.Text = "Controller connected. Pulsing the A button…";
            await _gamepad.PulseAsync("A", 80, CancellationToken.None);
            GamepadStatusText.Text = _gamepad.Status + " · test passed";
        }
        catch (Exception exception)
        {
            GamepadStatusText.Text = exception.Message;
        }
    }

    private void EndCapture(string hint)
    {
        _capturing = false;
        CaptureHotkeyButton.Content = "Change";
        HotkeyHint.Text = hint;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
