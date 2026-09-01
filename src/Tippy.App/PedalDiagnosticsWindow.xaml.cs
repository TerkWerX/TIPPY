using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;
using Tippy.App.Models;

namespace Tippy.App;

public partial class PedalDiagnosticsWindow : Window
{
    public sealed record DiagnosticRow(string Time, string Device, int Switch, string State, string Latency, string Raw);
    private readonly ObservableCollection<DiagnosticRow> _events = [];
    private readonly HashSet<string> _pressed = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyCollection<PedalDeviceInfo> _devices = [];
    private double _latencyTotal;
    private double _latencyMaximum;

    public PedalDiagnosticsWindow()
    {
        InitializeComponent();
        EventsGrid.ItemsSource = _events;
    }

    public void SetDevices(IReadOnlyCollection<PedalDeviceInfo> devices)
    {
        _devices = devices;
        ConnectedText.Text = devices.Count.ToString();
    }

    public void Record(PedalStateEventArgs input, long routedTimestamp)
    {
        var received = input.ReceivedTimestamp == 0 ? routedTimestamp : input.ReceivedTimestamp;
        var latencyMs = (routedTimestamp - received) * 1000d / Stopwatch.Frequency;
        var trigger = $"{input.Device.DeviceKey}:{input.SwitchIndex}";
        if (input.IsPressed) _pressed.Add(trigger); else _pressed.Remove(trigger);
        _latencyTotal += Math.Max(0, latencyMs);
        _latencyMaximum = Math.Max(_latencyMaximum, latencyMs);
        _events.Insert(0, new DiagnosticRow(DateTime.Now.ToString("HH:mm:ss.fff"), input.Device.DisplayName,
            input.SwitchIndex + 1, input.IsSynthetic ? "Synthetic up" : input.IsPressed ? "Pressed" : "Released",
            $"{latencyMs:0.00} ms", Convert.ToHexString(input.RawReport)));
        while (_events.Count > 500) _events.RemoveAt(_events.Count - 1);
        EventCountText.Text = _events.Count.ToString();
        PressedCountText.Text = _pressed.Count.ToString();
        LatencyText.Text = _events.Count == 0 ? "—" : $"avg {_latencyTotal / _events.Count:0.00} · max {_latencyMaximum:0.00} ms";
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        _events.Clear();
        _pressed.Clear();
        _latencyTotal = 0;
        _latencyMaximum = 0;
        EventCountText.Text = "0";
        PressedCountText.Text = "0";
        LatencyText.Text = "—";
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "Tippy support report (*.json)|*.json",
            FileName = $"tippy-support-{DateTime.Now:yyyyMMdd-HHmmss}.json"
        };
        if (dialog.ShowDialog(this) != true) return;
        var report = new
        {
            Generated = DateTimeOffset.Now,
            AppVersion = typeof(PedalDiagnosticsWindow).Assembly.GetName().Version?.ToString(),
            Os = Environment.OSVersion.VersionString,
            Devices = _devices.Select(device => new
            {
                device.DisplayName, Vid = $"{device.VendorId:X4}", Pid = $"{device.ProductId:X4}",
                device.Manufacturer, device.SwitchCount, device.DecoderName
            }),
            RoutingLatency = new { AverageMs = _events.Count == 0 ? 0 : _latencyTotal / _events.Count, MaximumMs = _latencyMaximum },
            Events = _events.Take(200)
        };
        File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }
}
