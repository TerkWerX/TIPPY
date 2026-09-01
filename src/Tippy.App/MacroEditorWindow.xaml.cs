using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Tippy.App.Models;
using Tippy.App.Services;
using Tippy.Core.Models;
using Tippy.Core.Output;

namespace Tippy.App;

public partial class MacroEditorWindow : Window
{
    private readonly PedalBinding _working;
    private readonly bool _releaseOnly;
    private readonly string? _gestureTarget;
    private readonly IReadOnlyList<KnownWindowsAction> _knownActions;
    private readonly IReadOnlyList<KnownWindowsAction> _windowsShortcuts;
    private readonly IReadOnlyList<KnownWindowsAction> _keyboardKeys;
    private readonly IReadOnlyList<ApplicationShortcutProfile> _applications = ApplicationShortcutCatalog.Create();
    private readonly OscOutputSettings _oscSettings;
    private readonly Stopwatch _recordingClock = new();
    private readonly HashSet<Key> _recordedDown = [];
    private ICollectionView? _actionsView;
    private ICollectionView? _keysView;
    private ICollectionView? _applicationsView;
    private ICollectionView? _applicationShortcutsView;
    private ApplicationShortcutProfile? _selectedApplication;
    private bool _capturing;
    private bool _recording;
    private long _lastRecordedMilliseconds;

    public MacroEditorWindow(PedalBinding source, OscOutputSettings? oscSettings = null) : this(source, false, null, oscSettings)
    {
    }

    private MacroEditorWindow(PedalBinding source, bool releaseOnly, string? gestureTarget = null,
        OscOutputSettings? oscSettings = null)
    {
        _knownActions = WindowsActionCatalog.Create();
        _windowsShortcuts = _knownActions.Where(action => !action.IsDirectKey).ToArray();
        _keyboardKeys = _knownActions.Where(action => action.IsDirectKey).ToArray();
        _releaseOnly = releaseOnly;
        _gestureTarget = gestureTarget;
        _oscSettings = oscSettings?.Clone() ?? new OscOutputSettings();
        _oscSettings.Normalize();
        _working = source.Clone();
        _working.Normalize();
        Result = source.Clone();
        InitializeComponent();
        Loaded += (_, _) =>
        {
            BindingTypeBox.SelectedIndex = (int)_working.Type;
            TriggerModeBox.SelectedIndex = CurrentMacro.TriggerMode == MacroTriggerMode.WhileHeld ? 1 : 0;
            ShiftBankBox.SelectedIndex = _working.ShiftBankIndex;
            NameBox.Text = CurrentMacro.Name;
            ActionsList.ItemsSource = _windowsShortcuts;
            _actionsView = CollectionViewSource.GetDefaultView(ActionsList.ItemsSource);
            ActionCategoryList.ItemsSource = CategoryFilters(
                _windowsShortcuts, action => action.Category, "All shortcuts");
            ActionCategoryList.SelectedIndex = 0;
            ActionsList.SelectedIndex = 0;
            UpdateActionCount();
            KeysList.ItemsSource = _keyboardKeys;
            _keysView = CollectionViewSource.GetDefaultView(KeysList.ItemsSource);
            KeyCategoryList.ItemsSource = CategoryFilters(
                _keyboardKeys, action => action.Category, "All keys");
            KeyCategoryList.SelectedIndex = 0;
            KeysList.SelectedIndex = 0;
            UpdateKeyCount();
            ApplicationsList.ItemsSource = _applications;
            _applicationsView = CollectionViewSource.GetDefaultView(ApplicationsList.ItemsSource);
            ApplicationCategoryList.ItemsSource = CategoryFilters(
                _applications, application => application.Category, "All applications");
            ApplicationCategoryList.SelectedIndex = 0;
            ApplicationsList.SelectedIndex = 0;
            UpdateApplicationCount();
            RefreshSteps();
            RefreshReleaseSummary();
            RepeatCheckBox.IsChecked = _working.Gestures.RepeatWhileHeld;
            ToggleCheckBox.IsChecked = _working.Gestures.Toggle;
            LoadGestureTiming();
            RefreshGestureSummary();
            UpdateMacroVisibility();
            if (_releaseOnly)
            {
                AssignmentHeading.Text = "Release action";
                AssignmentSubtitle.Text = "Build the action that runs when this switch is released.";
                BindingTypePanel.Visibility = Visibility.Collapsed;
                TriggerModePanel.Visibility = Visibility.Collapsed;
                ReleaseActionBorder.Visibility = Visibility.Collapsed;
                GestureActionsBorder.Visibility = Visibility.Collapsed;
            }
            else if (_gestureTarget is not null)
            {
                AssignmentHeading.Text = _gestureTarget == "double" ? "Double-tap action" : "Long-press action";
                AssignmentSubtitle.Text = "Build the action for this foot gesture.";
                BindingTypePanel.Visibility = Visibility.Collapsed;
                TriggerModePanel.Visibility = Visibility.Collapsed;
                ReleaseActionBorder.Visibility = Visibility.Collapsed;
                GestureActionsBorder.Visibility = Visibility.Collapsed;
            }
        };
        PreviewKeyDown += MacroEditorWindow_PreviewKeyDown;
        PreviewKeyUp += MacroEditorWindow_PreviewKeyUp;
    }

    public PedalBinding Result { get; private set; }

    private MacroDefinition CurrentMacro => _releaseOnly
        ? _working.ReleaseMacro
        : _gestureTarget == "double"
            ? _working.Gestures.DoubleTapMacro
            : _gestureTarget == "long"
                ? _working.Gestures.LongPressMacro
                : _working.Macro;

    private void BindingTypeBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateMacroVisibility();

    private void UpdateMacroVisibility()
    {
        if (MacroArea is null) return;
        var type = _releaseOnly || _gestureTarget is not null ? PedalBindingType.Macro : SelectedBindingType();
        var isMacro = type == PedalBindingType.Macro;
        MacroArea.IsEnabled = isMacro;
        TriggerModeBox.IsEnabled = isMacro && !_releaseOnly;
        MacroArea.Opacity = isMacro ? 1 : 0.45;
        ShiftBankPanel.Visibility = !_releaseOnly && type == PedalBindingType.ShiftLayer
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (!_releaseOnly && _gestureTarget is null)
        {
            ReleaseActionBorder.Visibility = isMacro ? Visibility.Visible : Visibility.Collapsed;
            GestureActionsBorder.Visibility = isMacro ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void CaptureShortcut_Click(object sender, RoutedEventArgs e)
    {
        _capturing = true;
        CaptureButton.Content = "Press shortcut now…";
        CaptureHint.Text = "Press the key combination to add. Escape cancels.";
        Focus();
        Keyboard.Focus(this);
    }

    private void MacroEditorWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_recording)
        {
            e.Handled = true;
            var recordedKey = e.Key == Key.System ? e.SystemKey : e.Key;
            if (recordedKey == Key.Escape)
            {
                StopRecording("Recording stopped. Timing was preserved.");
                return;
            }
            if (e.IsRepeat || !_recordedDown.Add(recordedKey)) return;
            AddRecordedEvent(MacroStepType.KeyDown, recordedKey);
            return;
        }
        if (!_capturing || ShortcutFormatter.IsModifier(e.Key)) return;
        e.Handled = true;
        if (e.Key == Key.Escape)
        {
            EndCapture("Capture canceled.");
            return;
        }
        try
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            var shortcut = ShortcutFormatter.FromKey(key, Keyboard.Modifiers, false);
            var keys = shortcut.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
            CurrentMacro.Steps.Add(new MacroStep { Type = MacroStepType.KeyChord, Keys = keys, DurationMs = 25 });
            RefreshSteps(CurrentMacro.Steps.Count - 1);
            EndCapture($"Added {shortcut}");
        }
        catch (Exception exception)
        {
            EndCapture(exception.Message);
        }
    }

    private void MacroEditorWindow_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (!_recording) return;
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (!_recordedDown.Remove(key)) return;
        AddRecordedEvent(MacroStepType.KeyUp, key);
    }

    private void Record_Click(object sender, RoutedEventArgs e)
    {
        if (_recording)
        {
            StopRecording("Recording stopped. Timing was preserved.");
            return;
        }
        _capturing = false;
        _recording = true;
        _recordedDown.Clear();
        _lastRecordedMilliseconds = 0;
        _recordingClock.Restart();
        RecordButton.Content = "Stop recording";
        CaptureHint.Text = "Recording keyboard press/release timing — press Escape or Stop when finished.";
        Focus();
        Keyboard.Focus(this);
    }

    private void AddRecordedEvent(MacroStepType type, Key key)
    {
        var keyName = ShortcutFormatter.KeyName(key);
        if (keyName is null) return;
        var now = _recordingClock.ElapsedMilliseconds;
        var delay = (int)Math.Clamp(now - _lastRecordedMilliseconds, 0, 60_000);
        if (CurrentMacro.Steps.Count > 0 && delay > 0)
            CurrentMacro.Steps.Add(new MacroStep { Type = MacroStepType.Delay, DurationMs = delay });
        CurrentMacro.Steps.Add(new MacroStep { Type = type, Keys = [keyName] });
        _lastRecordedMilliseconds = now;
        RefreshSteps(CurrentMacro.Steps.Count - 1);
    }

    private void StopRecording(string message)
    {
        _recording = false;
        _recordingClock.Stop();
        if (_recordedDown.Count > 0)
        {
            foreach (var key in _recordedDown.Reverse().ToArray())
            {
                AddRecordedEvent(MacroStepType.KeyUp, key);
            }
            message += " Held keys were closed with matching release events.";
        }
        _recordedDown.Clear();
        RecordButton.Content = "Start timed recording";
        CaptureHint.Text = message;
    }

    private void ActionSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var query = ActionSearchBox.Text.Trim();
        SelectAllCategoryForSearch(ActionCategoryList, query);
        RefreshActionFilter(query);
    }

    private void ActionCategoryList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        RefreshActionFilter(ActionSearchBox?.Text.Trim() ?? string.Empty);

    private void RefreshActionFilter(string query)
    {
        if (_actionsView is null) return;
        var category = SelectedCategory(ActionCategoryList);
        _actionsView.Filter = item => item is KnownWindowsAction action &&
            (category is null || action.Category == category) && Matches(action.SearchText, query);
        _actionsView.Refresh();
        ActionsList.SelectedIndex = ActionsList.Items.Count > 0 ? 0 : -1;
        UpdateActionCount();
    }

    private void UpdateActionCount()
    {
        if (ActionCountText is not null)
            ActionCountText.Text = $"{ActionsList.Items.Count} shown · {_windowsShortcuts.Count} Windows shortcuts total";
    }

    private void ActionsList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => AssignSelectedAction();

    private void AssignAction_Click(object sender, RoutedEventArgs e) => AssignSelectedAction();

    private void AssignSelectedAction()
    {
        if (ActionsList.SelectedItem is not KnownWindowsAction action) return;
        AssignKnownAction(action);
    }

    private void KeySearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var query = KeySearchBox.Text.Trim();
        SelectAllCategoryForSearch(KeyCategoryList, query);
        RefreshKeyFilter(query);
    }

    private void KeyCategoryList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        RefreshKeyFilter(KeySearchBox?.Text.Trim() ?? string.Empty);

    private void RefreshKeyFilter(string query)
    {
        if (_keysView is null) return;
        var category = SelectedCategory(KeyCategoryList);
        _keysView.Filter = item => item is KnownWindowsAction action &&
            (category is null || action.Category == category) && Matches(action.SearchText, query);
        _keysView.Refresh();
        KeysList.SelectedIndex = KeysList.Items.Count > 0 ? 0 : -1;
        UpdateKeyCount();
    }

    private void UpdateKeyCount()
    {
        if (KeyCountText is not null)
            KeyCountText.Text = $"{KeysList.Items.Count} shown · {_keyboardKeys.Count} individual keys total";
    }

    private void KeysList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => AssignSelectedKey();

    private void AssignKey_Click(object sender, RoutedEventArgs e) => AssignSelectedKey();

    private void AssignSelectedKey()
    {
        if (KeysList.SelectedItem is not KnownWindowsAction key) return;
        AssignKnownAction(key);
    }

    private void AssignKnownAction(KnownWindowsAction action)
    {
        _working.Type = PedalBindingType.Macro;
        CurrentMacro.Name = action.Name;
        CurrentMacro.TriggerMode = _releaseOnly ? MacroTriggerMode.ReleaseOnce : MacroTriggerMode.PressOnce;
        CurrentMacro.Steps.Clear();
        CurrentMacro.Steps.Add(new MacroStep { Type = MacroStepType.KeyChord, Keys = action.Keys.ToList(), DurationMs = 25 });
        BindingTypeBox.SelectedIndex = (int)PedalBindingType.Macro;
        TriggerModeBox.SelectedIndex = (int)MacroTriggerMode.PressOnce;
        NameBox.Text = action.Name;
        RefreshSteps(0);
        MacroArea.SelectedIndex = 3;
        CaptureHint.Text = $"Assigned {action.Name} ({action.Shortcut}).";
    }

    private void ApplicationSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var query = ApplicationSearchBox.Text.Trim();
        SelectAllCategoryForSearch(ApplicationCategoryList, query);
        RefreshApplicationFilter(query);
    }

    private void ApplicationCategoryList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        RefreshApplicationFilter(ApplicationSearchBox?.Text.Trim() ?? string.Empty);

    private void RefreshApplicationFilter(string query)
    {
        if (_applicationsView is null) return;
        var category = SelectedCategory(ApplicationCategoryList);
        _applicationsView.Filter = item => item is ApplicationShortcutProfile application &&
            (category is null || application.Category == category) && Matches(application.SearchText, query);
        _applicationsView.Refresh();
        ApplicationsList.SelectedIndex = ApplicationsList.Items.Count > 0 ? 0 : -1;
        UpdateApplicationCount();
    }

    private void UpdateApplicationCount()
    {
        if (ApplicationCountText is not null)
            ApplicationCountText.Text = $"{ApplicationsList.Items.Count} applications shown · " +
                $"{ApplicationsList.Items.OfType<ApplicationShortcutProfile>().Sum(application => application.Shortcuts.Count)} shortcuts in view";
    }

    private void ApplicationsList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => ViewSelectedApplication();

    private void ViewApplication_Click(object sender, RoutedEventArgs e) => ViewSelectedApplication();

    private void ViewSelectedApplication()
    {
        if (ApplicationsList.SelectedItem is not ApplicationShortcutProfile application) return;
        _selectedApplication = application;
        SelectedApplicationText.Text = $"{application.Name} · {application.Publisher}";
        SelectedApplicationNoteText.Text = application.VersionNote;
        ApplicationShortcutSearchBox.Clear();
        ApplicationShortcutsList.ItemsSource = application.Shortcuts;
        _applicationShortcutsView = CollectionViewSource.GetDefaultView(ApplicationShortcutsList.ItemsSource);
        ApplicationShortcutCategoryList.ItemsSource = CategoryFilters(
            application.Shortcuts, shortcut => shortcut.Category, "All commands");
        ApplicationShortcutCategoryList.SelectedIndex = 0;
        ApplicationShortcutsList.SelectedIndex = 0;
        ApplicationsPanel.Visibility = Visibility.Collapsed;
        ApplicationShortcutsPanel.Visibility = Visibility.Visible;
        UpdateApplicationShortcutCount();
    }

    private void BackToApplications_Click(object sender, RoutedEventArgs e)
    {
        ApplicationShortcutsPanel.Visibility = Visibility.Collapsed;
        ApplicationsPanel.Visibility = Visibility.Visible;
        _selectedApplication = null;
        ApplicationSearchBox.Focus();
    }

    private void ApplicationShortcutSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var query = ApplicationShortcutSearchBox.Text.Trim();
        SelectAllCategoryForSearch(ApplicationShortcutCategoryList, query);
        RefreshApplicationShortcutFilter(query);
    }

    private void ApplicationShortcutCategoryList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        RefreshApplicationShortcutFilter(ApplicationShortcutSearchBox?.Text.Trim() ?? string.Empty);

    private void RefreshApplicationShortcutFilter(string query)
    {
        if (_applicationShortcutsView is null) return;
        var category = SelectedCategory(ApplicationShortcutCategoryList);
        _applicationShortcutsView.Filter = item => item is ApplicationShortcut shortcut &&
            (category is null || shortcut.Category == category) && Matches(shortcut.SearchText, query);
        _applicationShortcutsView.Refresh();
        ApplicationShortcutsList.SelectedIndex = ApplicationShortcutsList.Items.Count > 0 ? 0 : -1;
        UpdateApplicationShortcutCount();
    }

    private void UpdateApplicationShortcutCount()
    {
        if (ApplicationShortcutCountText is not null && _selectedApplication is not null)
            ApplicationShortcutCountText.Text = $"{ApplicationShortcutsList.Items.Count} matching shortcuts · Source: official { _selectedApplication.Publisher } documentation";
    }

    private void ApplicationShortcutsList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => AssignSelectedApplicationShortcut();

    private void AssignApplicationShortcut_Click(object sender, RoutedEventArgs e) => AssignSelectedApplicationShortcut();

    private void OpenApplicationSource_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedApplication is null) return;
        Process.Start(new ProcessStartInfo(_selectedApplication.SourceUrl) { UseShellExecute = true });
    }

    private void AssignSelectedApplicationShortcut()
    {
        if (_selectedApplication is null || ApplicationShortcutsList.SelectedItem is not ApplicationShortcut shortcut) return;
        _working.Type = PedalBindingType.Macro;
        CurrentMacro.Name = $"{_selectedApplication.Name}: {shortcut.Name}";
        CurrentMacro.TriggerMode = _releaseOnly ? MacroTriggerMode.ReleaseOnce : MacroTriggerMode.PressOnce;
        CurrentMacro.Steps.Clear();
        CurrentMacro.Steps.Add(new MacroStep { Type = MacroStepType.KeyChord, Keys = shortcut.Keys.ToList(), DurationMs = 25 });
        BindingTypeBox.SelectedIndex = (int)PedalBindingType.Macro;
        TriggerModeBox.SelectedIndex = (int)MacroTriggerMode.PressOnce;
        NameBox.Text = CurrentMacro.Name;
        RefreshSteps(0);
        MacroArea.SelectedIndex = 3;
        CaptureHint.Text = $"Assigned {_selectedApplication.Name} · {shortcut.Name} ({shortcut.Shortcut}).";
    }

    private void AddString_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(TextStringBox.Text)) return;
        CurrentMacro.Steps.Add(new MacroStep { Type = MacroStepType.Text, Value = TextStringBox.Text });
        TextStringBox.Clear();
        RefreshSteps(CurrentMacro.Steps.Count - 1);
    }

    private void AddText_Click(object sender, RoutedEventArgs e)
    {
        var value = PromptDialog.Ask(this, "Type text", "Text to type", string.Empty);
        if (value is null) return;
        CurrentMacro.Steps.Add(new MacroStep { Type = MacroStepType.Text, Value = value });
        RefreshSteps(CurrentMacro.Steps.Count - 1);
    }

    private void AddDelay_Click(object sender, RoutedEventArgs e)
    {
        var value = PromptDialog.Ask(this, "Add delay", "Milliseconds (0–60000)", "100");
        if (value is null) return;
        if (!int.TryParse(value, out var milliseconds) || milliseconds is < 0 or > 60_000)
        {
            MessageBox.Show(this, "Enter a whole number from 0 to 60000.", "Delay", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        CurrentMacro.Steps.Add(new MacroStep { Type = MacroStepType.Delay, DurationMs = milliseconds });
        RefreshSteps(CurrentMacro.Steps.Count - 1);
    }

    private void AddMouse_Click(object sender, RoutedEventArgs e)
    {
        var choice = PromptDialog.Choose(this, "Mouse action", "Choose a mouse action",
            ["Left click", "Right click", "Middle click", "Wheel up", "Wheel down", "Scroll left", "Scroll right",
             "Move up", "Move down", "Move left", "Move right"]);
        if (choice is null) return;
        var step = choice switch
        {
            "Wheel up" => new MacroStep { Type = MacroStepType.MouseWheel, Amount = 120 },
            "Wheel down" => new MacroStep { Type = MacroStepType.MouseWheel, Amount = -120 },
            "Scroll left" => new MacroStep { Type = MacroStepType.MouseWheel, Value = "Horizontal", Amount = -120, DurationMs = 90 },
            "Scroll right" => new MacroStep { Type = MacroStepType.MouseWheel, Value = "Horizontal", Amount = 120, DurationMs = 90 },
            "Move up" => new MacroStep { Type = MacroStepType.MouseMove, Value = "Up", Amount = 8 },
            "Move down" => new MacroStep { Type = MacroStepType.MouseMove, Value = "Down", Amount = 8 },
            "Move left" => new MacroStep { Type = MacroStepType.MouseMove, Value = "Left", Amount = 8 },
            "Move right" => new MacroStep { Type = MacroStepType.MouseMove, Value = "Right", Amount = 8 },
            _ => new MacroStep { Type = MacroStepType.MouseButton, Value = choice.Replace(" click", string.Empty) }
        };
        CurrentMacro.Steps.Add(step);
        RefreshSteps(CurrentMacro.Steps.Count - 1);
    }

    private void AddMidi_Click(object sender, RoutedEventArgs e)
    {
        var value = PromptDialog.Ask(this, "MIDI output",
            "note/noteon:channel:note:velocity · noteoff:channel:note:releaseVelocity · cc:channel:controller:value · pc:channel:program\nFor a held note, put note-on in Press Action and matching note-off in Release Action.",
            "note:1:60:100");
        if (value is null) return;
        try
        {
            _ = MidiMessageParser.Parse(value);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Invalid MIDI message", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        CurrentMacro.Steps.Add(new MacroStep { Type = MacroStepType.Midi, Value = value });
        RefreshSteps(CurrentMacro.Steps.Count - 1);
    }

    private void AddOsc_Click(object sender, RoutedEventArgs e)
    {
        var address = PromptDialog.Ask(this, "OSC output", "OSC address (must begin with /)", "/tippy/pedal");
        if (address is null) return;
        var values = PromptDialog.Ask(this, "OSC values", "Comma-separated text, integer, or decimal values", "1");
        if (values is null) return;
        var choices = _oscSettings.Endpoints.Select(endpoint => endpoint.ToString()).Append("Custom endpoint…").ToArray();
        var selected = PromptDialog.Choose(this, "OSC destination", "Reusable endpoint preset", choices);
        if (selected is null) return;
        OscEndpointPreset? preset = null;
        string host;
        int port;
        if (selected == "Custom endpoint…")
        {
            var endpoint = PromptDialog.Ask(this, "OSC destination", "Host:port", "127.0.0.1:8000");
            if (endpoint is null) return;
            var separator = endpoint.LastIndexOf(':');
            host = separator > 0 ? endpoint[..separator] : endpoint;
            port = separator > 0 && int.TryParse(endpoint[(separator + 1)..], out var parsed) ? parsed : 8000;
        }
        else
        {
            preset = _oscSettings.Endpoints.First(endpoint => endpoint.ToString() == selected);
            host = preset.Host;
            port = preset.Port;
        }
        CurrentMacro.Steps.Add(new MacroStep
        {
            Type = MacroStepType.Osc, Value = address, Arguments = values,
            WorkingDirectory = host, Amount = Math.Clamp(port, 1, 65535), EndpointPresetId = preset?.Id
        });
        RefreshSteps(CurrentMacro.Steps.Count - 1);
    }

    private void AddGamepad_Click(object sender, RoutedEventArgs e)
    {
        var choice = PromptDialog.Choose(this, "Virtual gamepad", "Xbox 360 button, stick direction, or trigger",
            ["A", "B", "X", "Y", "LB", "RB", "Back", "Start", "L3", "R3", "DPad Up", "DPad Down", "DPad Left", "DPad Right",
             "Left stick left", "Left stick right", "Left stick up", "Left stick down",
             "Right stick left", "Right stick right", "Right stick up", "Right stick down", "Left trigger", "Right trigger"]);
        if (choice is null) return;
        var analog = choice.Contains("stick", StringComparison.OrdinalIgnoreCase) ||
                     choice.Contains("trigger", StringComparison.OrdinalIgnoreCase);
        if (!analog)
        {
            CurrentMacro.Steps.Add(new MacroStep { Type = MacroStepType.GamepadButton, Value = choice, DurationMs = 25 });
            RefreshSteps(CurrentMacro.Steps.Count - 1);
            return;
        }
        var intensityText = PromptDialog.Ask(this, "Analog intensity", "Percentage from 1 to 100", "100");
        if (intensityText is null) return;
        if (!int.TryParse(intensityText, out var intensity) || intensity is < 1 or > 100)
        {
            MessageBox.Show(this, "Enter a whole-number intensity from 1 to 100.", "Virtual gamepad",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var (axis, sign) = choice switch
        {
            "Left stick left" => ("Left X", -1), "Left stick right" => ("Left X", 1),
            "Left stick up" => ("Left Y", 1), "Left stick down" => ("Left Y", -1),
            "Right stick left" => ("Right X", -1), "Right stick right" => ("Right X", 1),
            "Right stick up" => ("Right Y", 1), "Right stick down" => ("Right Y", -1),
            "Left trigger" => ("Left Trigger", 1), _ => ("Right Trigger", 1)
        };
        CurrentMacro.Steps.Add(new MacroStep
            { Type = MacroStepType.GamepadAxis, Value = axis, Amount = intensity * sign, DurationMs = 100 });
        RefreshSteps(CurrentMacro.Steps.Count - 1);
    }

    private void AddProgram_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose a program, script, or shortcut",
            Filter = "Programs and scripts (*.exe;*.com;*.bat;*.cmd;*.lnk)|*.exe;*.com;*.bat;*.cmd;*.lnk|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != true) return;
        var arguments = PromptDialog.Ask(this, "Program arguments",
            "Optional command-line arguments (leave blank for none)", string.Empty);
        if (arguments is null) return;
        CurrentMacro.Steps.Add(new MacroStep
        {
            Type = MacroStepType.LaunchProgram,
            Value = dialog.FileName,
            Arguments = arguments,
            WorkingDirectory = Path.GetDirectoryName(dialog.FileName)
        });
        RefreshSteps(CurrentMacro.Steps.Count - 1);
    }

    private void EditReleaseAction_Click(object sender, RoutedEventArgs e)
    {
        _working.Macro.Name = string.IsNullOrWhiteSpace(NameBox.Text) ? "Unnamed macro" : NameBox.Text.Trim();
        var editor = new MacroEditorWindow(_working, true, null, _oscSettings)
        {
            Owner = this,
            Title = $"Release action · {Title}"
        };
        if (editor.ShowDialog() != true) return;
        _working.ReleaseMacro = editor.Result.ReleaseMacro.Clone();
        RefreshReleaseSummary();
    }

    private void EditDoubleTap_Click(object sender, RoutedEventArgs e) => EditGesture("double");

    private void EditLongPress_Click(object sender, RoutedEventArgs e) => EditGesture("long");

    private void EditGesture(string target)
    {
        _working.Macro.Name = string.IsNullOrWhiteSpace(NameBox.Text) ? "Unnamed macro" : NameBox.Text.Trim();
        var editor = new MacroEditorWindow(_working, false, target, _oscSettings)
        {
            Owner = this,
            Title = $"{(target == "double" ? "Double tap" : "Long press")} · {Title}"
        };
        if (editor.ShowDialog() != true) return;
        _working.Gestures = editor.Result.Gestures.Clone();
        RefreshGestureSummary();
    }

    private void ClearGestures_Click(object sender, RoutedEventArgs e)
    {
        _working.Gestures = new PedalGestureSettings();
        RepeatCheckBox.IsChecked = false;
        ToggleCheckBox.IsChecked = false;
        LoadGestureTiming();
        RefreshGestureSummary();
    }

    private void LoadGestureTiming()
    {
        if (DoubleWindowBox is null) return;
        DoubleWindowBox.Text = _working.Gestures.DoubleTapWindowMs.ToString();
        LongThresholdBox.Text = _working.Gestures.LongPressThresholdMs.ToString();
        RepeatDelayBox.Text = _working.Gestures.RepeatDelayMs.ToString();
        RepeatIntervalBox.Text = _working.Gestures.RepeatIntervalMs.ToString();
    }

    private void RefreshGestureSummary()
    {
        if (GestureSummary is null) return;
        var actions = new List<string>();
        if (_working.Gestures.DoubleTapMacro.Steps.Count > 0) actions.Add($"Double: {_working.Gestures.DoubleTapMacro.Name}");
        if (_working.Gestures.LongPressMacro.Steps.Count > 0) actions.Add($"Hold: {_working.Gestures.LongPressMacro.Name}");
        if (_working.Gestures.RepeatWhileHeld) actions.Add("repeat");
        if (_working.Gestures.Toggle) actions.Add("toggle");
        GestureSummary.Text = actions.Count == 0 ? "Standard press/release" : string.Join(" · ", actions);
    }

    private void ClearReleaseAction_Click(object sender, RoutedEventArgs e)
    {
        _working.ReleaseMacro = new MacroDefinition
        {
            Name = "On release",
            TriggerMode = MacroTriggerMode.ReleaseOnce
        };
        RefreshReleaseSummary();
    }

    private void RefreshReleaseSummary()
    {
        if (ReleaseActionSummary is null) return;
        var hasAction = _working.ReleaseMacro.Steps.Count > 0;
        ReleaseActionSummary.Text = hasAction
            ? $"{_working.ReleaseMacro.Name} · {_working.ReleaseMacro.Summary}"
            : "No action when released";
        ClearReleaseButton.IsEnabled = hasAction;
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e) => MoveSelected(-1);
    private void MoveDown_Click(object sender, RoutedEventArgs e) => MoveSelected(1);

    private void MoveSelected(int direction)
    {
        var current = StepsList.SelectedIndex;
        var target = current + direction;
        if (current < 0 || target < 0 || target >= CurrentMacro.Steps.Count) return;
        (CurrentMacro.Steps[current], CurrentMacro.Steps[target]) = (CurrentMacro.Steps[target], CurrentMacro.Steps[current]);
        RefreshSteps(target);
    }

    private void RemoveStep_Click(object sender, RoutedEventArgs e)
    {
        var index = StepsList.SelectedIndex;
        if (index < 0) return;
        CurrentMacro.Steps.RemoveAt(index);
        RefreshSteps(Math.Min(index, CurrentMacro.Steps.Count - 1));
    }

    private void RefreshSteps(int selectedIndex = -1)
    {
        StepsList.Items.Clear();
        for (var index = 0; index < CurrentMacro.Steps.Count; index++)
        {
            StepsList.Items.Add(new ListBoxItem
            {
                Content = $"{index + 1}.   {CurrentMacro.Steps[index].ToSummary()}",
                Padding = new Thickness(10, 8, 10, 8)
            });
        }
        if (selectedIndex >= 0 && selectedIndex < StepsList.Items.Count) StepsList.SelectedIndex = selectedIndex;
    }

    private void EndCapture(string message)
    {
        _capturing = false;
        CaptureButton.Content = "Capture shortcut";
        CaptureHint.Text = message;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        CurrentMacro.Name = string.IsNullOrWhiteSpace(NameBox.Text) ? "Unnamed macro" : NameBox.Text.Trim();
        if (_releaseOnly || _gestureTarget is not null)
        {
            CurrentMacro.TriggerMode = _releaseOnly ? MacroTriggerMode.ReleaseOnce : MacroTriggerMode.PressOnce;
            if (CurrentMacro.Steps.Count == 0 && MessageBox.Show(this,
                    "This action has no steps. Save it anyway?", "Empty action",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            Result = _working.Clone();
            DialogResult = true;
            return;
        }

        _working.Type = SelectedBindingType();
        _working.ShiftBankIndex = Math.Clamp(ShiftBankBox.SelectedIndex, 0, AppProfile.MaxBanks - 1);
        _working.Macro.TriggerMode = SelectedTriggerMode();
        _working.Gestures.RepeatWhileHeld = RepeatCheckBox.IsChecked == true;
        _working.Gestures.Toggle = ToggleCheckBox.IsChecked == true;
        _working.Gestures.DoubleTapWindowMs = ParseTiming(DoubleWindowBox.Text, 320);
        _working.Gestures.LongPressThresholdMs = ParseTiming(LongThresholdBox.Text, 550);
        _working.Gestures.RepeatDelayMs = ParseTiming(RepeatDelayBox.Text, 450);
        _working.Gestures.RepeatIntervalMs = ParseTiming(RepeatIntervalBox.Text, 110);
        var hasDouble = _working.Gestures.DoubleTapMacro.Steps.Count > 0;
        var hasLong = _working.Gestures.LongPressMacro.Steps.Count > 0;
        if (_working.Gestures.Toggle && (_working.Gestures.RepeatWhileHeld || hasDouble || hasLong))
        {
            MessageBox.Show(this, "Toggle is a complete press behavior. Turn off repeat and clear double-tap/long-press actions before enabling toggle.",
                "Conflicting foot behaviors", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (_working.Gestures.RepeatWhileHeld && (hasDouble || hasLong))
        {
            MessageBox.Show(this, "Repeat cannot be combined with double-tap or long-press recognition on the same switch.",
                "Conflicting foot behaviors", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if ((hasDouble || hasLong) && _working.Macro.TriggerMode == MacroTriggerMode.WhileHeld)
        {
            MessageBox.Show(this, "Double-tap and long-press recognition require a run-once tap action. Choose Run once when pressed.",
                "Conflicting foot behaviors", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (_working.Type == PedalBindingType.Macro &&
            _working.Macro.Steps.Count == 0 && _working.ReleaseMacro.Steps.Count == 0 &&
            MessageBox.Show(this, "This switch has no press or release steps. Save it anyway?", "Empty assignment",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }
        if (_working.Type == PedalBindingType.Macro &&
            _working.Macro.TriggerMode == MacroTriggerMode.WhileHeld &&
            _working.Macro.Steps.Any(step => step.Type is not (MacroStepType.KeyChord or MacroStepType.KeyDown or MacroStepType.GamepadButton or MacroStepType.GamepadAxis or MacroStepType.MouseButton or MacroStepType.MouseMove or MacroStepType.MouseWheel)))
        {
            MessageBox.Show(this,
                "Hold-until-release actions support keyboard, gamepad, mouse-button, mouse-movement, and scrolling steps. Remove text, waits, program, MIDI, and OSC steps, or choose Run once.",
                "Hold macro", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _working.Normalize();
        Result = _working.Clone();
        DialogResult = true;
    }

    private static IReadOnlyList<CatalogCategoryFilter> CategoryFilters<T>(
        IReadOnlyCollection<T> items, Func<T, string> categorySelector, string allLabel)
    {
        var filters = new List<CatalogCategoryFilter> { new(allLabel, items.Count) };
        filters.AddRange(items
            .GroupBy(categorySelector)
            .Select(group => new CatalogCategoryFilter(group.Key, group.Count())));
        return filters;
    }

    private static string? SelectedCategory(ListBox list) =>
        list.SelectedIndex <= 0 ? null : (list.SelectedItem as CatalogCategoryFilter)?.Name;

    private static void SelectAllCategoryForSearch(ListBox list, string query)
    {
        if (query.Length > 0 && list.SelectedIndex > 0) list.SelectedIndex = 0;
    }

    private static bool Matches(string searchText, string query) =>
        query.Length == 0 || searchText.Contains(query, StringComparison.OrdinalIgnoreCase);

    private PedalBindingType SelectedBindingType() =>
        Enum.TryParse<PedalBindingType>((BindingTypeBox.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out var type)
            ? type : PedalBindingType.Macro;

    private MacroTriggerMode SelectedTriggerMode() =>
        _releaseOnly ? MacroTriggerMode.ReleaseOnce :
        Enum.TryParse<MacroTriggerMode>((TriggerModeBox.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out var mode)
            ? mode : MacroTriggerMode.PressOnce;

    private static int ParseTiming(string value, int fallback) => int.TryParse(value, out var parsed) ? parsed : fallback;
}
