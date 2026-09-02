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
    private readonly bool _openDiscoveryOnLoad;
    private int _editingIndex = -1;
    private bool _loading;

    public ApplicationProfilesWindow(
        IEnumerable<ApplicationProfileRule> profiles,
        IReadOnlyList<PedalDeviceProfile> devices,
        bool openDiscoveryOnLoad = false)
    {
        InitializeComponent();
        _working = profiles.Select(profile => profile.Clone()).ToList();
        _devices = devices;
        _openDiscoveryOnLoad = openDiscoveryOnLoad;
        foreach (var profile in _working)
            foreach (var device in _devices)
                profile.EnsureDeviceScene(device);
        Result = _working.Select(profile => profile.Clone()).ToList();
        Loaded += (_, _) =>
        {
            RefreshList(_working.Count > 0 ? 0 : -1);
            if (_openDiscoveryOnLoad)
                Dispatcher.BeginInvoke(new Action(OpenCompatibleApplicationDiscovery));
        };
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
            WindowTitleBox.Clear();
            EnabledCheckBox.IsChecked = false;
            return;
        }

        var profile = _working[_editingIndex];
        ProfileNameBox.Text = profile.Name;
        ExecutablePathBox.Text = profile.ExecutablePath;
        WindowTitleBox.Text = profile.WindowTitleContains;
        EnabledCheckBox.IsChecked = profile.Enabled;
        foreach (var device in _devices)
        {
            var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var scene = profile.EnsureDeviceScene(device);
            var label = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
            label.Children.Add(new TextBlock
            {
                Text = device.DisplayName,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            label.Children.Add(new TextBlock
            {
                Text = $"3 banks · {device.SwitchCount} switch{(device.SwitchCount == 1 ? string.Empty : "es")} per bank",
                Style = (Style)FindResource("SmallMutedText"), Margin = new Thickness(0, 2, 0, 0)
            });
            row.Children.Add(label);
            var box = new ComboBox
            {
                Width = 120,
                ItemsSource = Enumerable.Range(1, AppProfile.MaxBanks).Select(index => $"Bank {index}").ToArray(),
                SelectedIndex = scene.ActiveBankIndex,
                Margin = new Thickness(0, 0, 8, 0)
            };
            Grid.SetColumn(box, 1);
            row.Children.Add(box);
            var capture = new Button { Content = "Recapture banks", Padding = new Thickness(9, 5, 9, 5), Tag = device.DeviceKey };
            capture.Click += CaptureDeviceBanks_Click;
            Grid.SetColumn(capture, 2);
            row.Children.Add(capture);
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
        profile.WindowTitleContains = WindowTitleBox.Text.Trim();
        profile.DeviceBanks = _devices.Select(device => new ApplicationDeviceBank
        {
            DeviceKey = device.DeviceKey,
            BankIndex = _bankBoxes.TryGetValue(device.DeviceKey, out var box) && box.SelectedIndex >= 0
                ? box.SelectedIndex
                : device.ActiveBankIndex
        }).ToList();
        foreach (var device in _devices)
        {
            var scene = profile.EnsureDeviceScene(device);
            if (_bankBoxes.TryGetValue(device.DeviceKey, out var box) && box.SelectedIndex >= 0)
                scene.ActiveBankIndex = box.SelectedIndex;
        }
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
        _working.Add(CreateProfile(Path.GetFileNameWithoutExtension(dialog.FileName),
            Path.GetFileNameWithoutExtension(dialog.FileName), dialog.FileName));
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
        _working.Add(CreateProfile(selected.Name, selected.Name, selected.Path));
        RefreshList(_working.Count - 1);
    }

    private void FindCompatibleApps_Click(object sender, RoutedEventArgs e) => OpenCompatibleApplicationDiscovery();

    private void OpenCompatibleApplicationDiscovery()
    {
        SaveCurrentEditor();
        var permission = MessageBox.Show(this,
            "Tippy can scan this PC for applications in its keyboard-shortcut catalog.\n\n" +
            "The scan reads installed-program names, Start Menu shortcuts, and visible running apps. " +
            "It does not inspect documents, browser history, application data, or send an inventory anywhere.\n\n" +
            "Scan now?",
            "Find compatible applications", MessageBoxButton.YesNo, MessageBoxImage.Information);
        if (permission != MessageBoxResult.Yes) return;

        var discovery = new CompatibleApplicationsWindow(_working) { Owner = this };
        if (discovery.ShowDialog() != true) return;
        foreach (var match in discovery.Result)
        {
            if (_working.Any(profile =>
                    (!string.IsNullOrWhiteSpace(match.ProcessName) &&
                     profile.ProcessName.Equals(match.ProcessName, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(match.ExecutablePath) &&
                     profile.ExecutablePath.Equals(match.ExecutablePath, StringComparison.OrdinalIgnoreCase))))
                continue;
            _working.Add(CreateProfile(match.CatalogProfile.Name, match.ProcessName, match.ExecutablePath));
        }
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
        if (_editingIndex < 0 || _editingIndex >= _working.Count) return;
        var profile = _working[_editingIndex];
        foreach (var device in _devices)
        {
            var scene = profile.EnsureDeviceScene(device);
            scene.Banks = device.Banks.Select(bank => bank.Clone()).ToList();
            scene.DisplayName = device.DisplayName;
            scene.ActiveBankIndex = device.ActiveBankIndex;
            scene.Normalize();
            if (_bankBoxes.TryGetValue(device.DeviceKey, out var box))
            {
                box.SelectedIndex = device.ActiveBankIndex;
            }
        }
    }

    private void CaptureDeviceBanks_Click(object sender, RoutedEventArgs e)
    {
        if (_editingIndex < 0 || _editingIndex >= _working.Count || sender is not Button { Tag: string deviceKey }) return;
        var device = _devices.FirstOrDefault(item => item.DeviceKey.Equals(deviceKey, StringComparison.OrdinalIgnoreCase));
        if (device is null) return;
        var scene = _working[_editingIndex].EnsureDeviceScene(device);
        scene.Banks = device.Banks.Select(bank => bank.Clone()).ToList();
        scene.DisplayName = device.DisplayName;
        scene.ActiveBankIndex = device.ActiveBankIndex;
        scene.Normalize();
        if (_bankBoxes.TryGetValue(device.DeviceKey, out var box)) box.SelectedIndex = device.ActiveBankIndex;
    }

    private static ApplicationDeviceScene CreateScene(PedalDeviceProfile device) => new()
    {
        DeviceKey = device.DeviceKey,
        DisplayName = device.DisplayName,
        ActiveBankIndex = device.ActiveBankIndex,
        Banks = device.Banks.Select(bank => bank.Clone()).ToList()
    };

    private ApplicationProfileRule CreateProfile(string name, string processName, string executablePath)
    {
        var profile = new ApplicationProfileRule
        {
            Name = name,
            ProcessName = processName,
            ExecutablePath = executablePath,
            DeviceBanks = _devices.Select(device => new ApplicationDeviceBank
            {
                DeviceKey = device.DeviceKey,
                BankIndex = device.ActiveBankIndex
            }).ToList(),
            DeviceScenes = _devices.Select(CreateScene).ToList()
        };
        profile.Normalize();
        return profile;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        SaveCurrentEditor();
        Result = _working.Select(profile => profile.Clone()).ToList();
        DialogResult = true;
    }
}
