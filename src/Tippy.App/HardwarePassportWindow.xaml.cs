using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;
using Tippy.App.Models;
using Tippy.Core.Input;

namespace Tippy.App;

public partial class HardwarePassportWindow : Window
{
    public sealed record PassportEvent(string Time, int Switch, string State, string Latency, string Raw);

    private readonly ObservableCollection<PassportEvent> _events = [];
    private IReadOnlyCollection<PedalDeviceInfo> _devices = [];
    private PedalDeviceInfo? _subject;
    private HardwareCertificationSession? _session;
    private bool _running;

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
        _session?.RecordConnection(connected);
        UpdateProgress();
    }

    public void Record(PedalStateEventArgs input, long routedTimestamp)
    {
        if (!_running || _subject is null ||
            !input.Device.DeviceKey.Equals(_subject.DeviceKey, StringComparison.OrdinalIgnoreCase)) return;
        var received = input.ReceivedTimestamp == 0 ? routedTimestamp : input.ReceivedTimestamp;
        var latency = Math.Max(0, (routedTimestamp - received) * 1000d / Stopwatch.Frequency);
        _session?.RecordInput(input.SwitchIndex, input.IsPressed, input.IsSynthetic, latency);
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
        _session = new HardwareCertificationSession(device.SwitchCount);
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
        _session = null;
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
        _session = null;
    }

    private void UpdateProgress()
    {
        if (_subject is null)
        {
            EverySwitchText.Text = "○ Press and release every switch twice";
            TogetherText.Text = "○ Press at least two switches together";
            ReconnectText.Text = "○ Unplug and reconnect the pedal";
            UnplugHeldText.Text = "○ Unplug once while a switch is held";
            ReleaseText.Text = "○ Finish with every switch released";
            PerformanceText.Text = "○ Meet the routing-latency target";
            LatencyText.Text = "—";
            SimultaneousText.Text = "Not tested";
            return;
        }
        var snapshot = _session?.Snapshot() ?? new HardwareCertificationSession(_subject.SwitchCount).Snapshot();
        EverySwitchText.Text = $"{(snapshot.EverySwitchRepeated ? "✓" : "○")} Press and release every switch twice ({snapshot.Cycles.Count(cycle => cycle >= 2)}/{_subject.SwitchCount})";
        TogetherText.Text = $"{(snapshot.SimultaneousPassed ? "✓" : "○")} Press at least two switches together";
        ReconnectText.Text = $"{(snapshot.Reconnected ? "✓" : "○")} Unplug and reconnect the pedal";
        UnplugHeldText.Text = $"{(snapshot.UnpluggedWhileHeld && snapshot.SyntheticReleaseObserved ? "✓" : "○")} Unplug while held; verify synthetic release";
        ReleaseText.Text = $"{(snapshot.AllReleased ? "✓" : "○")} Finish with every switch released";
        PerformanceText.Text = $"{(snapshot.PerformancePassed ? "✓" : "○")} Median ≤ 1 ms and p99 ≤ 5 ms";
        LatencyText.Text = snapshot.LatencySamples == 0 ? "—" : $"p50 {snapshot.MedianLatencyMs:0.00} · p99 {snapshot.P99LatencyMs:0.00} ms";
        SimultaneousText.Text = _subject.SwitchCount == 1 ? "Single switch" : snapshot.MaximumSimultaneous < 2 ? "Not yet" : $"{snapshot.MaximumSimultaneous} switches";
        StatusText.Text = snapshot.Certified ? "✓ Certified · functional + performance" :
            snapshot.FunctionalPassed ? "Functional pass · performance review" :
            snapshot.Disconnected && !snapshot.Reconnected ? "Pedal disconnected — reconnect it now" :
            $"Automated test running · {_subject.DisplayName}";
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
        var snapshot = _session?.Snapshot() ?? new HardwareCertificationSession(_subject.SwitchCount).Snapshot();
        var descriptorHash = new Services.HidLearningService().ListCandidates().FirstOrDefault(candidate =>
            candidate.DevicePath.Equals(_subject.DevicePath, StringComparison.OrdinalIgnoreCase))?.ReportDescriptorHash ?? string.Empty;
        var report = new
        {
            SchemaVersion = 2,
            CertificateId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{_subject.DeviceKey}|{DateTimeOffset.UtcNow:O}|{snapshot.Result}")))[..20],
            Generated = DateTimeOffset.Now,
            Result = snapshot.Result,
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
            Requirements = new { CyclesPerSwitch = 2, MedianLatencyTargetMs = 1.0, P99LatencyTargetMs = 5.0, UnplugWhileHeld = true },
            Tests = snapshot,
            RoutingLatency = new { snapshot.AverageLatencyMs, snapshot.MedianLatencyMs, snapshot.P95LatencyMs, snapshot.P99LatencyMs, snapshot.MaximumLatencyMs },
            Events = _events.Take(200)
        };
        File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        StatusText.Text = $"Passport exported · {Path.GetFileName(dialog.FileName)}";
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private static string SafeName(string value) => new(value.Where(character => char.IsLetterOrDigit(character) || character is '-' or '_').ToArray());
}
