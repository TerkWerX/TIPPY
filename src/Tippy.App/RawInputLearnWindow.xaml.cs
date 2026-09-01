using System.Windows;
using System.Windows.Controls;
using Tippy.App.Services;
using Tippy.Core.Models;

namespace Tippy.App;

public partial class RawInputLearnWindow : Window
{
    private readonly RawInputService _rawInput;
    private readonly List<RawInputSwitchMapping> _mappings = [];
    private bool _capturing;

    public RawInputLearnWindow(RawInputService rawInput)
    {
        InitializeComponent();
        _rawInput = rawInput;
        DeviceBox.ItemsSource = rawInput.Devices;
        SwitchCountBox.ItemsSource = Enumerable.Range(1, 32).ToArray();
        SwitchCountBox.SelectedIndex = 2;
        if (rawInput.Devices.Count > 0) DeviceBox.SelectedIndex = 0;
        _rawInput.KeyChanged += RawInput_KeyChanged;
        Closed += (_, _) => _rawInput.KeyChanged -= RawInput_KeyChanged;
        RefreshMappings();
    }

    public RawInputPedalDefinition? Result { get; private set; }

    private void RawInput_KeyChanged(object? sender, RawInputKeyEvent e)
    {
        if (!_capturing || !e.IsPressed || DeviceBox.SelectedItem is not RawInputKeyboardDevice device ||
            !device.DevicePath.Equals(e.DevicePath, StringComparison.OrdinalIgnoreCase)) return;
        _capturing = false;
        var index = _mappings.Count;
        _mappings.Add(new RawInputSwitchMapping { SwitchIndex = index, VirtualKey = e.VirtualKey });
        CaptureText.Text = $"Captured switch {index + 1} as virtual key 0x{e.VirtualKey:X2}.";
        CaptureButton.Content = "Capture next pedal switch";
        RefreshMappings();
    }

    private void Capture_Click(object sender, RoutedEventArgs e)
    {
        if (DeviceBox.SelectedItem is null) return;
        if (_mappings.Count >= SelectedSwitchCount()) return;
        _capturing = true;
        CaptureButton.Content = $"Waiting for switch {_mappings.Count + 1}…";
        CaptureText.Text = "Press and release the requested foot switch now. Normal keyboard input is not saved.";
    }

    private void DeviceBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _mappings.Clear();
        RefreshMappings();
    }

    private void SwitchCountBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        while (_mappings.Count > SelectedSwitchCount()) _mappings.RemoveAt(_mappings.Count - 1);
        RefreshMappings();
    }

    private int SelectedSwitchCount() => SwitchCountBox.SelectedItem is int count ? count : 3;

    private void RefreshMappings()
    {
        if (MappingsList is null) return;
        MappingsList.ItemsSource = Enumerable.Range(0, SelectedSwitchCount())
            .Select(index => index < _mappings.Count
                ? $"Pedal {index + 1} · virtual key 0x{_mappings[index].VirtualKey:X2}"
                : $"Pedal {index + 1} · not captured")
            .ToArray();
        if (CaptureButton is not null) CaptureButton.IsEnabled = _mappings.Count < SelectedSwitchCount();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (DeviceBox.SelectedItem is not RawInputKeyboardDevice device || _mappings.Count != SelectedSwitchCount())
        {
            MessageBox.Show(this, "Capture every configured pedal switch first.", "Keyboard pedal");
            return;
        }
        Result = new RawInputPedalDefinition
        {
            DevicePath = device.DevicePath,
            DisplayName = NameBox.Text,
            Switches = _mappings.Select(mapping => new RawInputSwitchMapping
            {
                SwitchIndex = mapping.SwitchIndex,
                VirtualKey = mapping.VirtualKey
            }).ToList()
        };
        Result.Normalize();
        DialogResult = true;
    }
}
