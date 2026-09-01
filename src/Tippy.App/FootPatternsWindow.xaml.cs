using System.Windows;
using System.Windows.Controls;
using Tippy.Core.Models;
using Tippy.App.Services;

namespace Tippy.App;

public partial class FootPatternsWindow : Window
{
    private readonly List<PedalPatternDefinition> _working;
    private readonly IReadOnlyList<PedalDeviceProfile> _devices;
    private int _editingIndex = -1;
    private bool _loading;
    private readonly PedalActivityHub _activity;
    private bool _capturing;

    public FootPatternsWindow(IEnumerable<PedalPatternDefinition> patterns, IReadOnlyList<PedalDeviceProfile> devices,
        PedalActivityHub activity)
    {
        InitializeComponent();
        _working = patterns.Select(pattern => pattern.Clone()).ToList();
        _devices = devices;
        _activity = activity;
        _activity.Pressed += Activity_Pressed;
        Closed += (_, _) => _activity.Pressed -= Activity_Pressed;
        Result = _working.Select(pattern => pattern.Clone()).ToList();
        Loaded += (_, _) => RefreshList(_working.Count > 0 ? 0 : -1);
    }

    public IReadOnlyList<PedalPatternDefinition> Result { get; private set; }

    private void RefreshList(int index)
    {
        _loading = true;
        PatternsList.ItemsSource = null;
        PatternsList.ItemsSource = _working;
        PatternsList.SelectedIndex = Math.Clamp(index, -1, _working.Count - 1);
        _loading = false;
        LoadSelected();
    }

    private void PatternsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        SaveEditor();
        LoadSelected();
    }

    private void LoadSelected()
    {
        _editingIndex = PatternsList.SelectedIndex;
        var selected = _editingIndex >= 0 && _editingIndex < _working.Count;
        Editor.IsEnabled = selected;
        Editor.Opacity = selected ? 1 : 0.45;
        if (!selected) return;
        var pattern = _working[_editingIndex];
        NameBox.Text = pattern.Name;
        TypeBox.SelectedIndex = pattern.Type == PedalPatternType.Combination ? 0 : 1;
        EnabledCheckBox.IsChecked = pattern.Enabled;
        WindowBox.Text = pattern.WindowMs.ToString();
        RefreshTriggers(pattern);
    }

    private void SaveEditor()
    {
        if (_editingIndex < 0 || _editingIndex >= _working.Count) return;
        var pattern = _working[_editingIndex];
        pattern.Name = NameBox.Text;
        pattern.Type = (TypeBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "Sequence"
            ? PedalPatternType.Sequence : PedalPatternType.Combination;
        pattern.Enabled = EnabledCheckBox.IsChecked == true;
        if (int.TryParse(WindowBox.Text, out var window)) pattern.WindowMs = window;
        pattern.Normalize();
    }

    private void RefreshTriggers(PedalPatternDefinition pattern)
    {
        TriggersList.ItemsSource = pattern.Triggers.Select((trigger, index) =>
        {
            var device = _devices.FirstOrDefault(item => item.DeviceKey.Equals(trigger.DeviceKey, StringComparison.OrdinalIgnoreCase));
            return $"{index + 1}.  {device?.DisplayName ?? trigger.DeviceKey} · Pedal {trigger.SwitchIndex + 1}";
        }).ToArray();
        ActionSummary.Text = pattern.Macro.Steps.Count == 0
            ? "No pattern action assigned"
            : $"{pattern.Macro.Name} · {pattern.Macro.Summary}";
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        SaveEditor();
        _working.Add(new PedalPatternDefinition { Name = $"Foot pattern {_working.Count + 1}" });
        RefreshList(_working.Count - 1);
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        var index = PatternsList.SelectedIndex;
        if (index < 0) return;
        _working.RemoveAt(index);
        RefreshList(Math.Min(index, _working.Count - 1));
    }

    private void AddTrigger_Click(object sender, RoutedEventArgs e)
    {
        if (_editingIndex < 0 || _devices.Count == 0)
        {
            MessageBox.Show(this, "Connect or retain a pedal profile before creating a pattern.", "Foot patterns");
            return;
        }
        var labels = _devices.Select((device, index) => $"{index + 1}. {device.DisplayName}").ToArray();
        var selected = PromptDialog.Choose(this, "Pattern pedal", "Choose a physical pedal", labels);
        if (selected is null) return;
        var deviceIndex = Array.IndexOf(labels, selected);
        if (deviceIndex < 0) return;
        var device = _devices[deviceIndex];
        var pedals = Enumerable.Range(1, device.SwitchCount).Select(index => $"Pedal {index}").ToArray();
        var pedal = PromptDialog.Choose(this, "Pattern switch", "Choose the switch", pedals);
        if (pedal is null) return;
        _working[_editingIndex].Triggers.Add(new PedalTriggerReference
        {
            DeviceKey = device.DeviceKey,
            SwitchIndex = Array.IndexOf(pedals, pedal)
        });
        RefreshTriggers(_working[_editingIndex]);
    }

    private void RemoveTrigger_Click(object sender, RoutedEventArgs e)
    {
        if (_editingIndex < 0 || TriggersList.SelectedIndex < 0) return;
        _working[_editingIndex].Triggers.RemoveAt(TriggersList.SelectedIndex);
        RefreshTriggers(_working[_editingIndex]);
    }

    private void CapturePattern_Click(object sender, RoutedEventArgs e)
    {
        if (_editingIndex < 0) return;
        _capturing = !_capturing;
        CapturePatternButton.Content = _capturing ? "Stop capture" : "Capture with feet";
        if (_capturing)
        {
            SaveEditor();
            _working[_editingIndex].Triggers.Clear();
            RefreshTriggers(_working[_editingIndex]);
        }
    }

    private void Activity_Pressed(object? sender, ObservedPedalPress press)
    {
        if (!_capturing || _editingIndex < 0) return;
        var pattern = _working[_editingIndex];
        if (pattern.Type == PedalPatternType.Combination && pattern.Triggers.Any(trigger =>
                trigger.DeviceKey.Equals(press.DeviceKey, StringComparison.OrdinalIgnoreCase) &&
                trigger.SwitchIndex == press.SwitchIndex)) return;
        if (pattern.Triggers.Count >= 16) return;
        pattern.Triggers.Add(new PedalTriggerReference
        {
            DeviceKey = press.DeviceKey,
            SwitchIndex = press.SwitchIndex
        });
        RefreshTriggers(pattern);
    }

    private void EditAction_Click(object sender, RoutedEventArgs e)
    {
        if (_editingIndex < 0) return;
        var pattern = _working[_editingIndex];
        var binding = new PedalBinding { Macro = pattern.Macro.Clone() };
        var editor = new MacroEditorWindow(binding) { Owner = this, Title = $"Pattern · {pattern.Name}" };
        if (editor.ShowDialog() != true) return;
        pattern.Macro = editor.Result.Macro.Clone();
        pattern.Macro.TriggerMode = MacroTriggerMode.PressOnce;
        RefreshTriggers(pattern);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        SaveEditor();
        foreach (var pattern in _working) pattern.Normalize();
        Result = _working.Select(pattern => pattern.Clone()).ToList();
        DialogResult = true;
    }
}
