using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;
using Tippy.App.Models;
using Tippy.App.Services;
using Tippy.Core.Input;

namespace Tippy.App;

public partial class HardwareTestStationWindow : Window
{
    public sealed record StationRow(string Time, string Kind, string Detail, string Latency);
    private readonly ObservableCollection<StationRow> _events = [];
    private readonly Dictionary<string, long> _lastPhysicalPress = new(StringComparer.Ordinal);
    private PedalDeviceInfo? _subject;
    private HardwareLoopbackSession? _session;
    private IReadOnlyCollection<PedalDeviceInfo> _devices = [];

    public HardwareTestStationWindow()
    {
        InitializeComponent();
        EventsGrid.ItemsSource = _events;
    }

    public void SetDevices(IReadOnlyCollection<PedalDeviceInfo> devices)
    {
        var selectedKey = (DeviceBox.SelectedItem as PedalDeviceInfo)?.DeviceKey;
        _devices = devices;
        DeviceBox.ItemsSource = devices.OrderBy(device => device.DisplayName).ToArray();
        DeviceBox.SelectedItem = devices.FirstOrDefault(device => device.DeviceKey.Equals(selectedKey, StringComparison.OrdinalIgnoreCase)) ?? devices.FirstOrDefault();
        if (_subject is null || _session is null) return;
        var connected = devices.Any(device => device.DeviceKey.Equals(_subject.DeviceKey, StringComparison.OrdinalIgnoreCase));
        _session.RecordConnection(connected);
        AddRow("Connection", connected ? "Pedal connected" : "Pedal disconnected", string.Empty);
        Refresh();
    }

    public void RecordInput(PedalStateEventArgs input, long routedTimestamp)
    {
        if (_subject is null || _session is null || !input.Device.DeviceKey.Equals(_subject.DeviceKey, StringComparison.OrdinalIgnoreCase)) return;
        var received = input.ReceivedTimestamp == 0 ? routedTimestamp : input.ReceivedTimestamp;
        var routingLatency = Math.Max(0, (routedTimestamp - received) * 1000d / Stopwatch.Frequency);
        _session.RecordInput(input.SwitchIndex, input.IsPressed, input.IsSynthetic, routingLatency);
        var trigger = $"{input.Device.DeviceKey}:{input.SwitchIndex}";
        if (input.IsPressed) _lastPhysicalPress[trigger] = received;
        AddRow("HID", $"Switch {input.SwitchIndex + 1} · {(input.IsSynthetic ? "synthetic release" : input.IsPressed ? "pressed" : "released")}", $"{routingLatency:0.00} ms route");
        Refresh();
    }

    public void RecordOutput(MacroOutputEventArgs output)
    {
        if (_session is null || !_lastPhysicalPress.TryGetValue(output.TriggerId, out var received)) return;
        var latency = Math.Max(0, (output.DispatchedTimestamp - received) * 1000d / Stopwatch.Frequency);
        _session.RecordOutput(latency);
        AddRow("Output", output.OutputKind, $"{latency:0.00} ms");
        Refresh();
    }

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        if (DeviceBox.SelectedItem is not PedalDeviceInfo device)
        {
            MessageBox.Show(this, "Connect and select a real pedal first.", "Hardware test station");
            return;
        }
        _subject = device;
        _session = new HardwareLoopbackSession(device.SwitchCount, 10);
        _events.Clear();
        _lastPhysicalPress.Clear();
        DeviceBox.IsEnabled = false;
        StartButton.IsEnabled = false;
        ExportButton.IsEnabled = true;
        StatusText.Text = "Physical test running. Use the selected pedal normally; Tippy is measuring the live path.";
        Refresh();
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        _subject = null;
        _session = null;
        _events.Clear();
        _lastPhysicalPress.Clear();
        DeviceBox.IsEnabled = true;
        StartButton.IsEnabled = true;
        ExportButton.IsEnabled = false;
        ResultText.Text = "Ready";
        StatusText.Text = "Choose a connected pedal.";
        Refresh();
    }

    private void Refresh()
    {
        if (_session is null)
        {
            SoakText.Text = "0 / 10"; HotPlugText.Text = "Not tested"; OutputText.Text = "No samples";
            return;
        }
        var snapshot = _session.Snapshot();
        var minimum = snapshot.Device.Cycles.Count == 0 ? 0 : snapshot.Device.Cycles.Min();
        SoakText.Text = $"{Math.Min(minimum, 10)} / 10 each";
        HotPlugText.Text = snapshot.Device.Reconnected && snapshot.Device.SyntheticReleaseObserved ? "Passed" : snapshot.Device.Disconnected ? "Reconnect now" : "Waiting";
        OutputText.Text = snapshot.OutputSamples == 0 ? "No samples" : $"p50 {snapshot.OutputMedianMs:0.00} · p99 {snapshot.OutputP99Ms:0.00} ms";
        ResultText.Text = snapshot.Complete ? "✓ HIL verified" : "In progress";
        CyclesCheckText.Text = $"{(snapshot.SoakPassed ? "✓" : "○")} 10 press/release cycles per switch";
        TogetherCheckText.Text = $"{(snapshot.Device.SimultaneousPassed ? "✓" : "○")} Simultaneous switches";
        ReconnectCheckText.Text = $"{(snapshot.Device.Reconnected ? "✓" : "○")} Disconnect and reconnect";
        CleanupCheckText.Text = $"{(snapshot.Device.UnpluggedWhileHeld && snapshot.Device.SyntheticReleaseObserved ? "✓" : "○")} Unplug while held; synthetic release";
        OutputCheckText.Text = $"{(snapshot.OutputLatencyPassed ? "✓" : "○")} Mapped output latency · {snapshot.OutputSamples} samples";
        if (snapshot.Complete) StatusText.Text = "Hardware-in-the-loop regression passed. Export the report for the device-support record.";
    }

    private void AddRow(string kind, string detail, string latency)
    {
        _events.Insert(0, new StationRow(DateTime.Now.ToString("HH:mm:ss.fff"), kind, detail, latency));
        while (_events.Count > 500) _events.RemoveAt(_events.Count - 1);
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_subject is null || _session is null) return;
        var dialog = new SaveFileDialog { Filter = "Tippy HIL report (*.tippy-hil.json)|*.tippy-hil.json", FileName = $"{SafeName(_subject.DisplayName)}-{DateTime.Now:yyyyMMdd}.tippy-hil.json" };
        if (dialog.ShowDialog(this) != true) return;
        var report = new
        {
            SchemaVersion = 1, Generated = DateTimeOffset.Now, AppVersion = typeof(HardwareTestStationWindow).Assembly.GetName().Version?.ToString(),
            Device = new { _subject.DisplayName, Vid = $"{_subject.VendorId:X4}", Pid = $"{_subject.ProductId:X4}", _subject.SwitchCount, _subject.DecoderName },
            Result = _session.Snapshot(), Events = _events.Take(300)
        };
        File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        StatusText.Text = $"HIL report exported · {Path.GetFileName(dialog.FileName)}";
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private static string SafeName(string value) => new(value.Where(character => char.IsLetterOrDigit(character) || character is '-' or '_').ToArray());
}
