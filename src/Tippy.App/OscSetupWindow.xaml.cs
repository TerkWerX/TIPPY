using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Tippy.App.Services;
using Tippy.Core.Models;

namespace Tippy.App;

public partial class OscSetupWindow : Window
{
    private readonly ObservableCollection<OscEndpointPreset> _endpoints;
    private string _defaultId;

    public OscSetupWindow(OscOutputSettings settings)
    {
        InitializeComponent();
        var working = settings.Clone();
        working.Normalize();
        _endpoints = new ObservableCollection<OscEndpointPreset>(working.Endpoints.Select(endpoint => endpoint.Clone()));
        _defaultId = working.DefaultEndpointId;
        EndpointsGrid.ItemsSource = _endpoints;
        EndpointsGrid.SelectedItem = _endpoints.FirstOrDefault(endpoint => endpoint.Id.Equals(_defaultId, StringComparison.OrdinalIgnoreCase)) ?? _endpoints.FirstOrDefault();
        Result = working;
    }

    public OscOutputSettings Result { get; private set; }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var endpoint = new OscEndpointPreset { Name = "New endpoint", Host = "127.0.0.1", Port = 8000 };
        _endpoints.Add(endpoint);
        EndpointsGrid.SelectedItem = endpoint;
        EndpointsGrid.ScrollIntoView(endpoint);
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (EndpointsGrid.SelectedItem is not OscEndpointPreset endpoint || _endpoints.Count <= 1) return;
        var index = _endpoints.IndexOf(endpoint);
        _endpoints.Remove(endpoint);
        if (endpoint.Id.Equals(_defaultId, StringComparison.OrdinalIgnoreCase)) _defaultId = _endpoints[0].Id;
        EndpointsGrid.SelectedIndex = Math.Clamp(index, 0, _endpoints.Count - 1);
    }

    private void EndpointsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DefaultCheckBox is null) return;
        DefaultCheckBox.IsChecked = EndpointsGrid.SelectedItem is OscEndpointPreset endpoint &&
                                    endpoint.Id.Equals(_defaultId, StringComparison.OrdinalIgnoreCase);
    }

    private void DefaultCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (DefaultCheckBox.IsChecked == true && EndpointsGrid.SelectedItem is OscEndpointPreset endpoint)
            _defaultId = endpoint.Id;
    }

    private void Test_Click(object sender, RoutedEventArgs e)
    {
        if (!TrySelected(out var endpoint)) return;
        try
        {
            using var osc = new OscOutputService();
            osc.Send(endpoint.Host, endpoint.Port, AddressBox.Text, ValuesBox.Text);
            StatusText.Foreground = (Brush)FindResource("SuccessBrush");
            StatusText.Text = $"Sent {AddressBox.Text} to {endpoint.Host}:{endpoint.Port} ({OscOutputService.BuildPacket(AddressBox.Text, ValuesBox.Text).Length} bytes).";
        }
        catch (Exception exception)
        {
            StatusText.Foreground = (Brush)FindResource("DangerBrush");
            StatusText.Text = exception.Message;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        EndpointsGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        EndpointsGrid.CommitEdit(DataGridEditingUnit.Row, true);
        foreach (var endpoint in _endpoints)
        {
            endpoint.Normalize();
            if (string.IsNullOrWhiteSpace(endpoint.Host))
            {
                MessageBox.Show(this, "Every OSC endpoint needs a host name or IP address.", "OSC endpoints", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }
        Result = new OscOutputSettings { DefaultEndpointId = _defaultId, Endpoints = _endpoints.Select(endpoint => endpoint.Clone()).ToList() };
        Result.Normalize();
        DialogResult = true;
    }

    private bool TrySelected(out OscEndpointPreset endpoint)
    {
        EndpointsGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        EndpointsGrid.CommitEdit(DataGridEditingUnit.Row, true);
        if (EndpointsGrid.SelectedItem is OscEndpointPreset selected)
        {
            selected.Normalize();
            endpoint = selected;
            return true;
        }
        endpoint = null!;
        return false;
    }
}
