using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;
using Tippy.App.Models;

namespace Tippy.App;

public partial class HardwarePassportWindow : Window
{
    public sealed record PassportEvent(string Time, int Switch, string State, string Latency, string Raw);

    private readonly ObservableCollection<PassportEvent> _events = [];
    private readonly HashSet<int> _pressedSeen = [];
    private readonly HashSet<int> _releasedSeen = [];
    private readonly HashSet<int> _down = [];
    private readonly Dictionary<int, int> _cycles = [];
    private IReadOnlyCollection<PedalDeviceInfo> _devices = [];
    private PedalDeviceInfo? _subject;
    private bool _running;
    private bool _disconnected;
    private bool _reconnected;
    private int _maximumTogether;
    private double _latencyTotal;
    private double _latencyMaximum;
    private int _latencyCount;

    public HardwarePassportWindow()
    {
        InitializeComponent();
        EventsGrid.ItemsSource = _events;
    }

    public void SetDevices(IReadOnlyCollection<PedalDeviceInfo> devices)
    {
        var previousSelectedKey = (DeviceBox.SelectedItem as PedalDeviceInfo)?.DeviceKey;
        _devices = devices;
        DeviceBox.ItemsSource = devices.OrderBy(device => device.DisplayName).ToArray();
        DeviceBox.SelectedItem = devices.FirstOrDefault(device =>
            device.DeviceKey.Equals(previousSelectedKey, StringComparison.OrdinalIgnoreCase)) ?? devices.FirstOrDefault();
        if (!_running || _subject is null) return;
        var connected = devices.Any(device => device.DeviceKey.Equals(_subject.DeviceKey, StringComparison.OrdinalIgnoreCase));
        if (!connected) _disconnected = true;
        else if (_disconnected) _reconnected = true;
        UpdateProgress();
    }

    public void Record(PedalStateEventArgs input, long routedTimestamp)
    {
        if (!_running || _subject is null ||
            !input.Device.DeviceKey.Equals(_subject.DeviceKey, StringComparison.OrdinalIgnoreCase)) return;
        var received = input.ReceivedTimestamp == 0 ? routedTimestamp : input.ReceivedTimestamp;
        var latency = Math.Max(0, (routedTimestamp - received) * 1000d / Stopwatch.Frequency);
        _latencyTotal += latency;
        _latencyMaximum = Math.Max(_latencyMaximum, latency);
        _latencyCount++;
        if (input.IsPressed)
        {
            _pressedSeen.Add(input.SwitchIndex);
            _down.Add(input.SwitchIndex);
            _maximumTogether = Math.Max(_maximumTogether, _down.Count);
        }
        else
        {
            _releasedSeen.Add(input.SwitchIndex);
            _down.Remove(input.SwitchIndex);
            _cycles[input.SwitchIndex] = _cycles.GetValueOrDefault(input.SwitchIndex) + 1;
        }
        _events.Insert(0, new PassportEvent(DateTime.Now.ToString("HH:mm:ss.fff"), input.SwitchIndex + 1,
            input.IsSynthetic ? "Synthetic up" : input.IsPressed ? "Pressed" : "Released",
            $"{latency:0.00} ms", Convert.ToHexString(input.RawReport)));
        while (_events.Count > 250) _events.RemoveAt(_events.Count - 1);
        UpdateProgress();
    }

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        if (DeviceBox.SelectedItem is not PedalDeviceInfo device)
        {
            MessageBox.Show(this, "Connect and select a pedal first.", "Hardware Passport");
            return;
        }
        ResetState();
        _subject = device;
        _running = true;
        DeviceBox.IsEnabled = false;
        StartButton.IsEnabled = false;
        ExportButton.IsEnabled = true;
        UpdateProgress();
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        ResetState();
        _subject = null;
        _running = false;
        DeviceBox.IsEnabled = true;
        StartButton.IsEnabled = true;
        ExportButton.IsEnabled = false;
        StatusText.Text = "Choose a pedal and start";
        UpdateProgress();
    }

    private void ResetState()
    {
        _events.Clear();
        _pressedSeen.Clear();
        _releasedSeen.Clear();
        _down.Clear();
        _cycles.Clear();
        _disconnected = false;
        _reconnected = false;
        _maximumTogether = 0;
        _latencyTotal = 0;
        _latencyMaximum = 0;
        _latencyCount = 0;
    }

    private void UpdateProgress()
    {
        if (_subject is null)
        {
            EverySwitchText.Text = "○ Press and release every switch twice";
            TogetherText.Text = "○ Press at least two switches together";
            ReconnectText.Text = "○ Unplug and reconnect the pedal";
            ReleaseText.Text = "○ Finish with every switch released";
            LatencyText.Text = "—";
            SimultaneousText.Text = "Not tested";
            return;
        }
        var everySwitch = Enumerable.Range(0, _subject.SwitchCount).All(index =>
            _pressedSeen.Contains(index) && _releasedSeen.Contains(index) && _cycles.GetValueOrDefault(index) >= 2);
        var together = _subject.SwitchCount == 1 || _maximumTogether >= 2;
        var released = _down.Count == 0;
        var complete = everySwitch && together && _reconnected && released;
        EverySwitchText.Text = $"{(everySwitch ? "✓" : "○")} Press and release every switch twice ({_cycles.Count(pair => pair.Value >= 2)}/{_subject.SwitchCount})";
        TogetherText.Text = $"{(together ? "✓" : "○")} Press at least two switches together";
        ReconnectText.Text = $"{(_reconnected ? "✓" : "○")} Unplug and reconnect the pedal";
        ReleaseText.Text = $"{(released ? "✓" : "○")} Finish with every switch released";
        LatencyText.Text = _latencyCount == 0 ? "—" : $"avg {_latencyTotal / _latencyCount:0.00} · max {_latencyMaximum:0.00} ms";
        SimultaneousText.Text = _subject.SwitchCount == 1 ? "Single switch" : _maximumTogether < 2 ? "Not yet" : $"{_maximumTogether} switches";
        StatusText.Text = complete ? "✓ Hardware passport complete" :
            _disconnected && !_reconnected ? "Pedal disconnected — reconnect it now" :
            $"Testing {_subject.DisplayName}";
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_subject is null) return;
        var dialog = new SaveFileDialog
        {
            Filter = "Tippy hardware passport (*.tippy-passport.json)|*.tippy-passport.json",
            FileName = $"{SafeName(_subject.DisplayName)}-{DateTime.Now:yyyyMMdd}.tippy-passport.json",
            AddExtension = true,
            DefaultExt = ".tippy-passport.json"
        };
        if (dialog.ShowDialog(this) != true) return;
        var everySwitch = Enumerable.Range(0, _subject.SwitchCount).All(index => _cycles.GetValueOrDefault(index) >= 2);
        var complete = everySwitch && (_subject.SwitchCount == 1 || _maximumTogether >= 2) && _reconnected && _down.Count == 0;
        var descriptorHash = new Services.HidLearningService().ListCandidates().FirstOrDefault(candidate =>
            candidate.DevicePath.Equals(_subject.DevicePath, StringComparison.OrdinalIgnoreCase))?.ReportDescriptorHash ?? string.Empty;
        var report = new
        {
            SchemaVersion = 1,
            Generated = DateTimeOffset.Now,
            Result = complete ? "verified" : "partial",
            AppVersion = typeof(HardwarePassportWindow).Assembly.GetName().Version?.ToString(),
            Os = Environment.OSVersion.VersionString,
            Device = new
            {
                _subject.DisplayName,
                Vid = $"{_subject.VendorId:X4}",
                Pid = $"{_subject.ProductId:X4}",
                _subject.Manufacturer,
                _subject.SwitchCount,
                _subject.DecoderName,
                ReportDescriptorHash = descriptorHash,
                DevicePathHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_subject.DevicePath)))
            },
            Tests = new { EverySwitchRepeated = everySwitch, MaximumSimultaneous = _maximumTogether, Reconnected = _reconnected, AllReleased = _down.Count == 0 },
            RoutingLatency = new { AverageMs = _latencyCount == 0 ? 0 : _latencyTotal / _latencyCount, MaximumMs = _latencyMaximum },
            Events = _events.Take(200)
        };
        File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        StatusText.Text = $"Passport exported · {Path.GetFileName(dialog.FileName)}";
    }

    private static string SafeName(string value) => new(value.Where(character => char.IsLetterOrDigit(character) || character is '-' or '_').ToArray());
}
