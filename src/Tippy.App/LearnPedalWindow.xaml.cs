using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Tippy.App.Models;
using Tippy.App.Services;
using Tippy.Core.Input;
using Tippy.Core.Models;

namespace Tippy.App;

public partial class LearnPedalWindow : Window
{
    private const int MaximumLearnedSwitches = 32;
    private readonly HidLearningService _learning = new();
    private byte[]?[] _pressed = [];
    private byte[]?[] _released = [];
    private Button[] _captureButtons = [];
    private TextBlock[] _stepStatuses = [];
    private CancellationTokenSource? _captureCancellation;
    private bool _busy;

    public LearnPedalWindow()
    {
        InitializeComponent();
        SwitchCountBox.ItemsSource = Enumerable.Range(1, MaximumLearnedSwitches);
        SwitchCountBox.SelectedItem = 3;
        ConfigureSwitchCount(3);
        Loaded += (_, _) => RefreshCandidates();
        Closed += (_, _) => _captureCancellation?.Cancel();
    }

    public LearnedPedalDefinition? Result { get; private set; }

    private HidCandidateInfo? SelectedCandidate => CandidateBox.SelectedItem as HidCandidateInfo;

    private void BuildCaptureStep(int index)
    {
        var host = new Border
        {
            Background = (Brush)FindResource("SurfaceAltBrush"),
            BorderBrush = (Brush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(13),
            Margin = new Thickness(0, 0, 0, 8)
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new TextBlock
        {
            Text = SwitchLabel(index), FontSize = 11, FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("MutedTextBrush"), VerticalAlignment = VerticalAlignment.Center
        });
        var status = new TextBlock
        {
            Text = "Not captured", Foreground = (Brush)FindResource("MutedTextBrush"),
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis
        };
        _stepStatuses[index] = status;
        Grid.SetColumn(status, 1);
        grid.Children.Add(status);
        var button = new Button { Content = "Capture", Padding = new Thickness(12, 5, 12, 5) };
        button.Click += async (_, _) => await CaptureAsync(index);
        _captureButtons[index] = button;
        Grid.SetColumn(button, 2);
        grid.Children.Add(button);
        host.Child = grid;
        SwitchStepsPanel.Children.Add(host);
    }

    private void SwitchCountBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_busy || SwitchCountBox.SelectedItem is not int switchCount)
        {
            return;
        }
        ConfigureSwitchCount(switchCount);
    }

    private void ConfigureSwitchCount(int switchCount)
    {
        switchCount = Math.Clamp(switchCount, 1, MaximumLearnedSwitches);
        _pressed = new byte[switchCount][];
        _released = new byte[switchCount][];
        _captureButtons = new Button[switchCount];
        _stepStatuses = new TextBlock[switchCount];
        SwitchStepsPanel.Children.Clear();
        for (var index = 0; index < switchCount; index++)
        {
            BuildCaptureStep(index);
        }
        FinishButton.IsEnabled = false;
        ResultHint.Text = $"Capture all {switchCount} switch{(switchCount == 1 ? string.Empty : "es")} to finish.";
    }

    private string SwitchLabel(int index) => _captureButtons.Length == 3
        ? index switch { 0 => "LEFT SWITCH", 1 => "CENTER SWITCH", _ => "RIGHT SWITCH" }
        : $"SWITCH {index + 1}";

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshCandidates();

    private void RefreshCandidates()
    {
        var previousPath = SelectedCandidate?.DevicePath;
        var candidates = _learning.ListCandidates();
        CandidateBox.ItemsSource = candidates;
        CandidateBox.SelectedItem = candidates.FirstOrDefault(item => item.DevicePath == previousPath)
                                    ?? candidates.FirstOrDefault(item => item.LooksLikePedal)
                                    ?? candidates.FirstOrDefault();
        if (candidates.Count == 0)
        {
            DeviceDetailsText.Text = "No readable HID input devices found.";
        }
    }

    private void CandidateBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var candidate = SelectedCandidate;
        if (candidate is null) return;
        DeviceDetailsText.Text = $"{candidate.ProductName}\n{candidate.Manufacturer}\nVID_{candidate.VendorId:X4} · PID_{candidate.ProductId:X4} · {candidate.ReportLength}-byte reports\nPath …{candidate.DevicePath[^Math.Min(28, candidate.DevicePath.Length)..]}";
        MappingNameBox.Text = candidate.ProductName.Contains("pedal", StringComparison.OrdinalIgnoreCase)
            ? candidate.ProductName
            : $"{candidate.ProductName} foot control";
        ResetSamples();
    }

    private async Task CaptureAsync(int index)
    {
        var candidate = SelectedCandidate;
        if (candidate is null || _busy) return;
        _busy = true;
        SetControlsEnabled(false);
        _captureButtons[index].IsEnabled = true;
        _captureButtons[index].Content = "Listening…";
        _stepStatuses[index].Text = "Keep this switch released while Tippy arms";
        _stepStatuses[index].Foreground = (Brush)FindResource("PressedBrush");
        LearningHint.Text = "Wait for the armed message before pressing the switch.";
        _captureCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var progress = new Progress<HidCaptureProgress>(capture =>
        {
            _stepStatuses[index].Text = capture.Message;
            LearningHint.Text = capture.PressCaptured
                ? "Press captured. Release the same switch."
                : capture.Message;
        });

        try
        {
            var pair = await _learning.CapturePressReleaseAsync(candidate, progress, _captureCancellation.Token);
            _pressed[index] = pair.Pressed;
            _released[index] = pair.Released;
            _stepStatuses[index].Text = $"✓ {Convert.ToHexString(pair.Pressed)} → {Convert.ToHexString(pair.Released)}";
            _stepStatuses[index].Foreground = (Brush)FindResource("SuccessBrush");
            LearningHint.Text = "Captured. Release all switches before the next capture.";
        }
        catch (OperationCanceledException)
        {
            _stepStatuses[index].Text = "Timed out—release all switches and try again";
            _stepStatuses[index].Foreground = (Brush)FindResource("DangerBrush");
        }
        catch (Exception exception)
        {
            _stepStatuses[index].Text = exception.Message;
            _stepStatuses[index].Foreground = (Brush)FindResource("DangerBrush");
        }
        finally
        {
            _captureCancellation.Dispose();
            _captureCancellation = null;
            _captureButtons[index].Content = _pressed[index] is null ? "Capture" : "Recapture";
            _busy = false;
            SetControlsEnabled(true);
            FinishButton.IsEnabled = _pressed.All(report => report is not null) && _released.All(report => report is not null);
            ResultHint.Text = FinishButton.IsEnabled
                ? "All switches captured. Save when ready."
                : $"Capture all {_pressed.Length} switch{(_pressed.Length == 1 ? string.Empty : "es")} to finish.";
        }
    }

    private void SetControlsEnabled(bool enabled)
    {
        CandidateBox.IsEnabled = enabled;
        MappingNameBox.IsEnabled = enabled;
        SwitchCountBox.IsEnabled = enabled;
        foreach (var button in _captureButtons) button.IsEnabled = enabled;
    }

    private void ResetSamples()
    {
        if (_busy) return;
        Array.Clear(_pressed);
        Array.Clear(_released);
        for (var index = 0; index < _stepStatuses.Length; index++)
        {
            if (_stepStatuses[index] is null) continue;
            _stepStatuses[index].Text = "Not captured";
            _stepStatuses[index].Foreground = (Brush)FindResource("MutedTextBrush");
            _captureButtons[index].Content = "Capture";
        }
        FinishButton.IsEnabled = false;
        ResultHint.Text = $"Capture all {_pressed.Length} switch{(_pressed.Length == 1 ? string.Empty : "es")} to finish.";
    }

    private void Finish_Click(object sender, RoutedEventArgs e)
    {
        var candidate = SelectedCandidate;
        if (candidate is null) return;
        try
        {
            var definition = new LearnedDefinitionBuilder().Build(
                MappingNameBox.Text,
                candidate.ProductName,
                candidate.VendorId,
                candidate.ProductId,
                _pressed.Select(report => report!).ToArray(),
                _released.Select(report => report!).ToArray());
            definition.ReportDescriptorHash = candidate.ReportDescriptorHash;
            Result = definition;
            var eventStyle = definition.Switches.Any(rule => rule.Selectors.Count > 0);
            ResultHint.Text = eventStyle ? "Learned event-style reports." : "Learned simultaneous button-state reports.";
            DialogResult = true;
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"Tippy could not derive a stable mapping from those samples. Recapture each switch carefully.\n\n{exception.Message}",
                "Could not learn pedal", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
