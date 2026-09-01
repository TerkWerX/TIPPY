using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using Tippy.Core.Models;

namespace Tippy.App;

public partial class ApplicationProfilesWindow : Window
{
    private readonly List<ApplicationProfileRule> _working;
    private readonly IReadOnlyList<PedalDeviceProfile> _devices;
    private readonly Dictionary<string, ComboBox> _bankBoxes = new(StringComparer.OrdinalIgnoreCase);
    private int _editingIndex = -1;
    private bool _loading;

    public ApplicationProfilesWindow(
        IEnumerable<ApplicationProfileRule> profiles,
        IReadOnlyList<PedalDeviceProfile> devices)
    {
        InitializeComponent();
        _working = profiles.Select(profile => profile.Clone()).ToList();
        _devices = devices;
        Result = _working.Select(profile => profile.Clone()).ToList();
        Loaded += (_, _) => RefreshList(_working.Count > 0 ? 0 : -1);
    }

    public IReadOnlyList<ApplicationProfileRule> Result { get; private set; }

    private void RefreshList(int selectedIndex)
    {
        _loading = true;
        ProfilesList.ItemsSource = null;
        ProfilesList.ItemsSource = _working;
        ProfilesList.SelectedIndex = Math.Clamp(selectedIndex, -1, _working.Count - 1);
        _loading = false;
        LoadSelected();
    }

    private void ProfilesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        SaveCurrentEditor();
        LoadSelected();
    }

    private void LoadSelected()
    {
        _editingIndex = ProfilesList.SelectedIndex;
        var hasSelection = _editingIndex >= 0 && _editingIndex < _working.Count;
        EditorPanel.IsEnabled = hasSelection;
        EditorPanel.Opacity = hasSelection ? 1 : 0.45;
        _bankBoxes.Clear();
        DeviceBanksPanel.Children.Clear();
        if (!hasSelection)
        {
            ProfileNameBox.Clear();
            ExecutablePathBox.Clear();
            EnabledCheckBox.IsChecked = false;
            return;
        }

        var profile = _working[_editingIndex];
        ProfileNameBox.Text = profile.Name;
        ExecutablePathBox.Text = profile.ExecutablePath;
        EnabledCheckBox.IsChecked = profile.Enabled;
        foreach (var device in _devices)
        {
            var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock
            {
                Text = device.DisplayName,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 0, 12, 0)
            });
            var box = new ComboBox
            {
                Width = 120,
                ItemsSource = Enumerable.Range(1, AppProfile.MaxBanks).Select(index => $"Bank {index}").ToArray(),
                SelectedIndex = profile.GetBankIndex(device.DeviceKey, device.ActiveBankIndex)
            };
            Grid.SetColumn(box, 1);
            row.Children.Add(box);
            DeviceBanksPanel.Children.Add(row);
            _bankBoxes[device.DeviceKey] = box;
        }
    }

    private void SaveCurrentEditor()
    {
        if (_editingIndex < 0 || _editingIndex >= _working.Count) return;
        var profile = _working[_editingIndex];
        profile.Name = string.IsNullOrWhiteSpace(ProfileNameBox.Text)
            ? profile.DisplayProcess
            : ProfileNameBox.Text.Trim();
        profile.Enabled = EnabledCheckBox.IsChecked == true;
        profile.DeviceBanks = _devices.Select(device => new ApplicationDeviceBank
        {
            DeviceKey = device.DeviceKey,
            BankIndex = _bankBoxes.TryGetValue(device.DeviceKey, out var box) && box.SelectedIndex >= 0
                ? box.SelectedIndex
                : device.ActiveBankIndex
        }).ToList();
        profile.Normalize();
    }

    private void AddProfile_Click(object sender, RoutedEventArgs e)
    {
        SaveCurrentEditor();
        var dialog = new OpenFileDialog
        {
            Title = "Choose the application executable",
            Filter = "Windows applications (*.exe)|*.exe|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != true) return;
        var profile = new ApplicationProfileRule
        {
            Name = Path.GetFileNameWithoutExtension(dialog.FileName),
            ProcessName = Path.GetFileNameWithoutExtension(dialog.FileName),
            ExecutablePath = dialog.FileName,
            DeviceBanks = _devices.Select(device => new ApplicationDeviceBank
            {
                DeviceKey = device.DeviceKey,
                BankIndex = device.ActiveBankIndex
            }).ToList()
        };
        profile.Normalize();
        _working.Add(profile);
        RefreshList(_working.Count - 1);
    }

    private void AddRunningProfile_Click(object sender, RoutedEventArgs e)
    {
        SaveCurrentEditor();
        var applications = System.Diagnostics.Process.GetProcesses()
            .Where(process => process.Id != Environment.ProcessId && process.MainWindowHandle != IntPtr.Zero)
            .Select(process =>
            {
                try
                {
                    return (Label: $"{process.ProcessName}.exe · {process.MainWindowTitle}",
                        Name: process.ProcessName,
                        Path: process.MainModule?.FileName ?? string.Empty);
                }
                catch { return (Label: $"{process.ProcessName}.exe · {process.MainWindowTitle}", Name: process.ProcessName, Path: string.Empty); }
                finally { process.Dispose(); }
            })
            .DistinctBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (applications.Length == 0)
        {
            MessageBox.Show(this, "No other visible desktop applications were found.", "Running applications");
            return;
        }
        var choice = PromptDialog.Choose(this, "Running application", "Choose the application to capture",
            applications.Select(item => item.Label).ToArray());
        var selected = applications.FirstOrDefault(item => item.Label == choice);
        if (string.IsNullOrWhiteSpace(selected.Name)) return;
        var profile = new ApplicationProfileRule
        {
            Name = selected.Name,
            ProcessName = selected.Name,
            ExecutablePath = selected.Path,
            DeviceBanks = _devices.Select(device => new ApplicationDeviceBank
            {
                DeviceKey = device.DeviceKey,
                BankIndex = device.ActiveBankIndex
            }).ToList()
        };
        profile.Normalize();
        _working.Add(profile);
        RefreshList(_working.Count - 1);
    }

    private void RemoveProfile_Click(object sender, RoutedEventArgs e)
    {
        var index = ProfilesList.SelectedIndex;
        if (index < 0 || index >= _working.Count) return;
        _working.RemoveAt(index);
        RefreshList(Math.Min(index, _working.Count - 1));
    }

    private void UseCurrentBanks_Click(object sender, RoutedEventArgs e)
    {
        foreach (var device in _devices)
        {
            if (_bankBoxes.TryGetValue(device.DeviceKey, out var box))
            {
                box.SelectedIndex = device.ActiveBankIndex;
            }
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        SaveCurrentEditor();
        Result = _working.Select(profile => profile.Clone()).ToList();
        DialogResult = true;
    }
}
