using System.Windows;
using System.Windows.Input;
using Tippy.App.Services;
using Tippy.Core.Models;

namespace Tippy.App;

public partial class SettingsWindow : Window
{
    private readonly VirtualGamepadService _gamepad;
    private bool _capturing;
    private List<TippyVariable> _variables = [];
    private readonly string _profileName;

    public SettingsWindow(
        string bankHotkey,
        string profileName,
        bool startMinimized,
        bool startWithWindows,
        bool checkForUpdates,
        VirtualGamepadService gamepad,
        MacroSafetySettings safety,
        OverlaySettings overlay,
        IEnumerable<TippyVariable> variables)
    {
        InitializeComponent();
        BankHotkey = bankHotkey;
        _profileName = profileName;
        _gamepad = gamepad;
        HotkeyBox.Text = bankHotkey;
        StartMinimizedCheckBox.IsChecked = startMinimized;
        StartWithWindowsCheckBox.IsChecked = startWithWindows;
        CheckUpdatesCheckBox.IsChecked = checkForUpdates;
        GamepadStatusText.Text = gamepad.Status;
        OverlayEnabledCheckBox.IsChecked = overlay.Enabled;
        OverlaySecondsBox.Text = overlay.VisibleSeconds.ToString();
        OverlayLeftBox.Text = overlay.Left.ToString("0");
        OverlayTopBox.Text = overlay.Top.ToString("0");
        MacroSecondsBox.Text = safety.MaximumMacroSeconds.ToString();
        RepeatSecondsBox.Text = safety.MaximumRepeatSeconds.ToString();
        MaximumStepsBox.Text = safety.MaximumSteps.ToString();
        EmergencyHotkeyBox.Text = safety.EmergencyStopHotkey;
        _variables = variables.Select(variable => new TippyVariable { Name = variable.Name, Value = variable.Value }).ToList();
        UpdateVariableSummary();
        PreviewKeyDown += SettingsWindow_PreviewKeyDown;
    }

    public string BankHotkey { get; private set; }
    public bool StartMinimized => StartMinimizedCheckBox.IsChecked == true;
    public bool StartWithWindows => StartWithWindowsCheckBox.IsChecked == true;
    public bool CheckForUpdates => CheckUpdatesCheckBox.IsChecked == true;
    public MacroSafetySettings Safety { get; private set; } = new();
    public OverlaySettings Overlay { get; private set; } = new();
    public IReadOnlyList<TippyVariable> Variables { get; private set; } = [];

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
            GamepadStatusText.Text = "Controller connected. Testing A, left stick, and both triggers…";
            await _gamepad.PulseAsync("A", 80, CancellationToken.None);
            await _gamepad.PulseAxisAsync("Left X", 100, 140, CancellationToken.None);
            await _gamepad.PulseAxisAsync("Left Trigger", 100, 100, CancellationToken.None);
            await _gamepad.PulseAxisAsync("Right Trigger", 100, 100, CancellationToken.None);
            GamepadStatusText.Text = _gamepad.Status + " · digital buttons, analog stick, and triggers passed";
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

    private void ManageVariables_Click(object sender, RoutedEventArgs e)
    {
        var manager = new VariableManagerWindow(_variables, _profileName) { Owner = this };
        if (manager.ShowDialog() != true) return;
        _variables = manager.Result.Select(variable => new TippyVariable { Name = variable.Name, Value = variable.Value }).ToList();
        UpdateVariableSummary();
    }

    private void UpdateVariableSummary() => VariablesSummaryText.Text = _variables.Count == 0
        ? "No custom variables yet. Built-in date, time, app, profile, device, pedal, bank, and clipboard tokens remain available."
        : $"{_variables.Count} custom variable{(_variables.Count == 1 ? string.Empty : "s")} · " + string.Join(", ", _variables.Take(4).Select(variable => $"{{{variable.Name}}}"));

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        Safety = new MacroSafetySettings
        {
            MaximumMacroSeconds = Parse(MacroSecondsBox.Text, 30),
            MaximumRepeatSeconds = Parse(RepeatSecondsBox.Text, 20),
            MaximumSteps = Parse(MaximumStepsBox.Text, 500),
            EmergencyStopHotkey = EmergencyHotkeyBox.Text
        };
        Safety.Normalize();
        Overlay = new OverlaySettings
        {
            Enabled = OverlayEnabledCheckBox.IsChecked == true,
            VisibleSeconds = Parse(OverlaySecondsBox.Text, 3),
            Left = ParseDouble(OverlayLeftBox.Text, 24),
            Top = ParseDouble(OverlayTopBox.Text, 24)
        };
        Overlay.Normalize();
        Variables = _variables.Select(variable => new TippyVariable { Name = variable.Name, Value = variable.Value }).ToArray();
        DialogResult = true;
    }

    private static int Parse(string value, int fallback) => int.TryParse(value, out var parsed) ? parsed : fallback;
    private static double ParseDouble(string value, double fallback) => double.TryParse(value, out var parsed) ? parsed : fallback;
}
