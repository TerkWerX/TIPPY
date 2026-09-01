using System.Windows;
using System.Windows.Controls;
using Tippy.Core.Models;

namespace Tippy.App;

public partial class BankTargetWindow : Window
{
    private readonly List<(PedalDeviceProfile Device, CheckBox CheckBox)> _choices = [];

    public BankTargetWindow(string bankName, int requiredSwitches,
        IReadOnlyList<PedalDeviceProfile> compatibleDevices, string? initiallySelectedDeviceKey)
    {
        InitializeComponent();
        BankInfoText.Text = $"{bankName} · requires {requiredSwitches} switch{(requiredSwitches == 1 ? string.Empty : "es")}";
        foreach (var device in compatibleDevices)
        {
            var checkBox = new CheckBox
            {
                IsChecked = initiallySelectedDeviceKey is null ||
                    device.DeviceKey.Equals(initiallySelectedDeviceKey, StringComparison.OrdinalIgnoreCase),
                Margin = new Thickness(0, 0, 0, 10),
                Content = $"{device.DisplayName}  →  Bank {device.ActiveBankIndex + 1}",
                FontSize = 15
            };
            _choices.Add((device, checkBox));
            TargetsPanel.Children.Add(checkBox);
        }
    }

    public IReadOnlyList<PedalDeviceProfile> SelectedDevices { get; private set; } = [];

    private void Load_Click(object sender, RoutedEventArgs e)
    {
        SelectedDevices = _choices.Where(choice => choice.CheckBox.IsChecked == true)
            .Select(choice => choice.Device).ToArray();
        if (SelectedDevices.Count == 0)
        {
            MessageBox.Show(this, "Choose at least one pedal.", "Load bank", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        DialogResult = true;
    }
}
