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
    private readonly HidLearningService _learning = new();
    private readonly byte[]?[] _pressed = new byte[3][];
    private readonly byte[]?[] _released = new byte[3][];
    private readonly Button[] _captureButtons = new Button[3];
    private readonly TextBlock[] _stepStatuses = new TextBlock[3];
    private CancellationTokenSource? _captureCancellation;
    private bool _busy;

    public LearnPedalWindow()
    {
        InitializeComponent();
        BuildCaptureStep(LeftStep, 0, "LEFT SWITCH");
        BuildCaptureStep(CenterStep, 1, "CENTER SWITCH");
        BuildCaptureStep(RightStep, 2, "RIGHT SWITCH");
        Loaded += (_, _) => RefreshCandidates();
        Closed += (_, _) => _captureCancellation?.Cancel();
    }

    public LearnedPedalDefinition? Result { get; private set; }

    private HidCandidateInfo? SelectedCandidate => CandidateBox.SelectedItem as HidCandidateInfo;

    private void BuildCaptureStep(Border host, int index, string label)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new TextBlock
        {
            Text = label, FontSize = 11, FontWeight = FontWeights.Bold,
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
    }

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
        _stepStatuses[index].Text = "Press and hold this switch now";
        _stepStatuses[index].Foreground = (Brush)FindResource("PressedBrush");
        LearningHint.Text = "Listening for the first changed HID report…";
        _captureCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var progress = new Progress<byte[]>(report =>
        {
            _stepStatuses[index].Text = $"Pressed {Convert.ToHexString(report)} · release it now";
            LearningHint.Text = "Press captured. Release the same switch.";
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
            ResultHint.Text = FinishButton.IsEnabled ? "All switches captured. Save when ready." : "Capture all three switches to finish.";
        }
    }

    private void SetControlsEnabled(bool enabled)
    {
        CandidateBox.IsEnabled = enabled;
        MappingNameBox.IsEnabled = enabled;
        foreach (var button in _captureButtons) button.IsEnabled = enabled;
    }

    private void ResetSamples()
    {
        if (_busy) return;
        Array.Clear(_pressed);
        Array.Clear(_released);
        for (var index = 0; index < 3; index++)
        {
            if (_stepStatuses[index] is null) continue;
            _stepStatuses[index].Text = "Not captured";
            _stepStatuses[index].Foreground = (Brush)FindResource("MutedTextBrush");
            _captureButtons[index].Content = "Capture";
        }
        FinishButton.IsEnabled = false;
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
