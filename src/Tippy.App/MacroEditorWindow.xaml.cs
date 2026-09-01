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

namespace Tippy.App;

public partial class MacroEditorWindow : Window
{
    private readonly PedalBinding _working;
    private readonly bool _releaseOnly;
    private readonly IReadOnlyList<KnownWindowsAction> _knownActions = WindowsActionCatalog.Create();
    private readonly IReadOnlyList<ApplicationShortcutProfile> _applications = ApplicationShortcutCatalog.Create();
    private readonly Stopwatch _recordingClock = new();
    private readonly HashSet<Key> _recordedDown = [];
    private ICollectionView? _actionsView;
    private ICollectionView? _applicationsView;
    private ICollectionView? _applicationShortcutsView;
    private ApplicationShortcutProfile? _selectedApplication;
    private bool _capturing;
    private bool _recording;
    private long _lastRecordedMilliseconds;

    public MacroEditorWindow(PedalBinding source) : this(source, false)
    {
    }

    private MacroEditorWindow(PedalBinding source, bool releaseOnly)
    {
        _releaseOnly = releaseOnly;
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
            ActionsList.ItemsSource = _knownActions;
            _actionsView = CollectionViewSource.GetDefaultView(ActionsList.ItemsSource);
            ActionsList.SelectedIndex = 0;
            UpdateActionCount();
            ApplicationsList.ItemsSource = _applications;
            _applicationsView = CollectionViewSource.GetDefaultView(ApplicationsList.ItemsSource);
            ApplicationsList.SelectedIndex = 0;
            UpdateApplicationCount();
            RefreshSteps();
            RefreshReleaseSummary();
            UpdateMacroVisibility();
            if (_releaseOnly)
            {
                AssignmentHeading.Text = "Release action";
                AssignmentSubtitle.Text = "Build the action that runs when this switch is released.";
                BindingTypePanel.Visibility = Visibility.Collapsed;
                TriggerModePanel.Visibility = Visibility.Collapsed;
                ReleaseActionBorder.Visibility = Visibility.Collapsed;
            }
        };
        PreviewKeyDown += MacroEditorWindow_PreviewKeyDown;
        PreviewKeyUp += MacroEditorWindow_PreviewKeyUp;
    }

    public PedalBinding Result { get; private set; }

    private MacroDefinition CurrentMacro => _releaseOnly ? _working.ReleaseMacro : _working.Macro;

    private void BindingTypeBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateMacroVisibility();

    private void UpdateMacroVisibility()
    {
        if (MacroArea is null) return;
        var type = _releaseOnly ? PedalBindingType.Macro : SelectedBindingType();
        var isMacro = type == PedalBindingType.Macro;
        MacroArea.IsEnabled = isMacro;
        TriggerModeBox.IsEnabled = isMacro && !_releaseOnly;
        MacroArea.Opacity = isMacro ? 1 : 0.45;
        ShiftBankPanel.Visibility = !_releaseOnly && type == PedalBindingType.ShiftLayer
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (!_releaseOnly)
        {
            ReleaseActionBorder.Visibility = isMacro ? Visibility.Visible : Visibility.Collapsed;
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
        if (_actionsView is null) return;
        var query = ActionSearchBox.Text.Trim();
        _actionsView.Filter = item => item is KnownWindowsAction action &&
            (query.Length == 0 || action.SearchText.Contains(query, StringComparison.OrdinalIgnoreCase));
        _actionsView.Refresh();
        UpdateActionCount();
    }

    private void UpdateActionCount()
    {
        if (ActionCountText is not null) ActionCountText.Text = $"{ActionsList.Items.Count} matching actions and keys";
    }

    private void ActionsList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => AssignSelectedAction();

    private void AssignAction_Click(object sender, RoutedEventArgs e) => AssignSelectedAction();

    private void AssignSelectedAction()
    {
        if (ActionsList.SelectedItem is not KnownWindowsAction action) return;
        _working.Type = PedalBindingType.Macro;
        CurrentMacro.Name = action.Name;
        CurrentMacro.TriggerMode = _releaseOnly ? MacroTriggerMode.ReleaseOnce : MacroTriggerMode.PressOnce;
        CurrentMacro.Steps.Clear();
        CurrentMacro.Steps.Add(new MacroStep { Type = MacroStepType.KeyChord, Keys = action.Keys.ToList(), DurationMs = 25 });
        BindingTypeBox.SelectedIndex = (int)PedalBindingType.Macro;
        TriggerModeBox.SelectedIndex = (int)MacroTriggerMode.PressOnce;
        NameBox.Text = action.Name;
        RefreshSteps(0);
        MacroArea.SelectedIndex = 2;
        CaptureHint.Text = $"Assigned {action.Name} ({action.Shortcut}).";
    }

    private void ApplicationSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_applicationsView is null) return;
        var query = ApplicationSearchBox.Text.Trim();
        _applicationsView.Filter = item => item is ApplicationShortcutProfile application &&
            (query.Length == 0 || application.SearchText.Contains(query, StringComparison.OrdinalIgnoreCase));
        _applicationsView.Refresh();
        UpdateApplicationCount();
    }

    private void UpdateApplicationCount()
    {
        if (ApplicationCountText is not null)
            ApplicationCountText.Text = $"{ApplicationsList.Items.Count} applications · {_applications.Sum(application => application.Shortcuts.Count)} cataloged shortcuts";
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
        if (_applicationShortcutsView is null) return;
        var query = ApplicationShortcutSearchBox.Text.Trim();
        _applicationShortcutsView.Filter = item => item is ApplicationShortcut shortcut &&
            (query.Length == 0 || shortcut.SearchText.Contains(query, StringComparison.OrdinalIgnoreCase));
        _applicationShortcutsView.Refresh();
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
        MacroArea.SelectedIndex = 2;
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
            ["Left click", "Right click", "Middle click", "Wheel up", "Wheel down"]);
        if (choice is null) return;
        var step = choice switch
        {
            "Wheel up" => new MacroStep { Type = MacroStepType.MouseWheel, Amount = 120 },
            "Wheel down" => new MacroStep { Type = MacroStepType.MouseWheel, Amount = -120 },
            _ => new MacroStep { Type = MacroStepType.MouseButton, Value = choice.Replace(" click", string.Empty) }
        };
        CurrentMacro.Steps.Add(step);
        RefreshSteps(CurrentMacro.Steps.Count - 1);
    }

    private void AddGamepad_Click(object sender, RoutedEventArgs e)
    {
        var choice = PromptDialog.Choose(this, "Gamepad button", "Virtual Xbox 360 button",
            ["A", "B", "X", "Y", "LB", "RB", "Back", "Start", "L3", "R3", "DPad Up", "DPad Down", "DPad Left", "DPad Right"]);
        if (choice is null) return;
        CurrentMacro.Steps.Add(new MacroStep { Type = MacroStepType.GamepadButton, Value = choice, DurationMs = 25 });
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
        var editor = new MacroEditorWindow(_working, true)
        {
            Owner = this,
            Title = $"Release action · {Title}"
        };
        if (editor.ShowDialog() != true) return;
        _working.ReleaseMacro = editor.Result.ReleaseMacro.Clone();
        RefreshReleaseSummary();
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
        if (_releaseOnly)
        {
            CurrentMacro.TriggerMode = MacroTriggerMode.ReleaseOnce;
            if (CurrentMacro.Steps.Count == 0 && MessageBox.Show(this,
                    "This release action has no steps. Save it anyway?", "Empty release action",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            Result = _working.Clone();
            DialogResult = true;
            return;
        }

        _working.Type = SelectedBindingType();
        _working.ShiftBankIndex = Math.Clamp(ShiftBankBox.SelectedIndex, 0, AppProfile.MaxBanks - 1);
        _working.Macro.TriggerMode = SelectedTriggerMode();
        if (_working.Type == PedalBindingType.Macro &&
            _working.Macro.Steps.Count == 0 && _working.ReleaseMacro.Steps.Count == 0 &&
            MessageBox.Show(this, "This switch has no press or release steps. Save it anyway?", "Empty assignment",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }
        if (_working.Type == PedalBindingType.Macro &&
            _working.Macro.TriggerMode == MacroTriggerMode.WhileHeld &&
            _working.Macro.Steps.Any(step => step.Type is not (MacroStepType.KeyChord or MacroStepType.KeyDown or MacroStepType.GamepadButton)))
        {
            MessageBox.Show(this,
                "Hold-until-release press actions can contain only keyboard shortcuts and gamepad buttons. Remove text, wait, mouse, program, and wheel steps, or choose Run once.",
                "Hold macro", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _working.Normalize();
        Result = _working.Clone();
        DialogResult = true;
    }

    private PedalBindingType SelectedBindingType() =>
        Enum.TryParse<PedalBindingType>((BindingTypeBox.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out var type)
            ? type : PedalBindingType.Macro;

    private MacroTriggerMode SelectedTriggerMode() =>
        _releaseOnly ? MacroTriggerMode.ReleaseOnce :
        Enum.TryParse<MacroTriggerMode>((TriggerModeBox.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out var mode)
            ? mode : MacroTriggerMode.PressOnce;
}
