using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using Microsoft.Win32;
using Tippy.App.Services;

namespace Tippy.App;

public partial class TippyDoctorWindow : Window
{
    private readonly Func<TippyDoctorReport> _runChecks;
    private readonly Action _releaseAllInputs;
    private readonly Action _findCompatibleApps;
    private TippyDoctorReport? _report;

    public TippyDoctorWindow(
        Func<TippyDoctorReport> runChecks,
        Action releaseAllInputs,
        Action findCompatibleApps)
    {
        InitializeComponent();
        _runChecks = runChecks;
        _releaseAllInputs = releaseAllInputs;
        _findCompatibleApps = findCompatibleApps;
        Loaded += (_, _) => RunChecks();
    }

    private void RunChecks()
    {
        try
        {
            ActionStatusText.Text = "Running local checks…";
            _report = _runChecks();
            ChecksList.ItemsSource = _report.Checks;
            PassedText.Text = _report.Passed.ToString();
            WarningText.Text = _report.Warnings.ToString();
            FailedText.Text = _report.Failed.ToString();
            PedalsText.Text = _report.ConnectedPedals.ToString();
            OverallText.Text = _report.Overall;
            OverallText.Foreground = (System.Windows.Media.Brush)FindResource(
                _report.Failed > 0 ? "DangerBrush" : _report.Warnings > 0 ? "PressedBrush" : "SuccessBrush");
            ActionStatusText.Text = $"Completed {_report.Generated.LocalDateTime:T} · no documents, macros, or typed text were inspected";
        }
        catch (Exception exception)
        {
            ActionStatusText.Text = $"Tippy Doctor could not finish: {exception.Message}";
            OverallText.Text = "Check failed";
            OverallText.Foreground = (System.Windows.Media.Brush)FindResource("DangerBrush");
        }
    }

    private void RunAgain_Click(object sender, RoutedEventArgs e) => RunChecks();

    private void ReleaseInputs_Click(object sender, RoutedEventArgs e)
    {
        _releaseAllInputs();
        ActionStatusText.Text = "Released all held keyboard, mouse, and gamepad inputs.";
    }

    private void FindApps_Click(object sender, RoutedEventArgs e)
    {
        Close();
        Dispatcher.BeginInvoke(_findCompatibleApps);
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_report is null) return;
        var dialog = new SaveFileDialog
        {
            Filter = "Tippy Doctor report (*.tippy-doctor.json)|*.tippy-doctor.json|JSON files (*.json)|*.json",
            FileName = $"tippy-doctor-{DateTime.Now:yyyyMMdd-HHmmss}.tippy-doctor.json"
        };
        if (dialog.ShowDialog(this) != true) return;
        var options = new JsonSerializerOptions { WriteIndented = true };
        options.Converters.Add(new JsonStringEnumConverter());
        File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(_report, options));
        ActionStatusText.Text = "Exported a privacy-safe Tippy Doctor report.";
    }
}
