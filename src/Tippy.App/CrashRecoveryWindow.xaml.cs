using System.Diagnostics;
using System.Windows;
using Tippy.App.Services;

namespace Tippy.App;

public enum CrashRecoveryChoice { StartNormally, RestoreLatestBackup }

public partial class CrashRecoveryWindow : Window
{
    private readonly PreviousCrashSession _previous;
    private readonly CrashRecoveryService _crashRecovery;
    private readonly SupportReportService _supportReports;

    public CrashRecoveryWindow(
        PreviousCrashSession previous,
        string? latestBackup,
        CrashRecoveryService crashRecovery,
        SupportReportService supportReports)
    {
        InitializeComponent();
        _previous = previous;
        _crashRecovery = crashRecovery;
        _supportReports = supportReports;
        LatestBackup = latestBackup;
        PreviousSessionText.Text = previous.StartedAt == DateTimeOffset.MinValue
            ? previous.Version
            : $"Previous session started {previous.StartedAt.LocalDateTime:g} · Tippy {previous.Version}";
        BackupText.Text = latestBackup is null
            ? "No automatic backup is available; starting normally will use the current profile."
            : $"Newest backup: {Path.GetFileName(latestBackup)}";
        RestoreButton.IsEnabled = latestBackup is not null;
    }

    public CrashRecoveryChoice Choice { get; private set; } = CrashRecoveryChoice.StartNormally;
    public string? LatestBackup { get; }

    private void Normal_Click(object sender, RoutedEventArgs e)
    {
        Choice = CrashRecoveryChoice.StartNormally;
        DialogResult = true;
    }

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        Choice = CrashRecoveryChoice.RestoreLatestBackup;
        DialogResult = true;
    }

    private void OpenLogs_Click(object sender, RoutedEventArgs e) => Process.Start(new ProcessStartInfo
    {
        FileName = _crashRecovery.LogsDirectory,
        UseShellExecute = true
    });

    private void PrepareReport_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var report = _supportReports.CreateCrashReport(_previous, _crashRecovery.CrashLogPath);
            new SupportReportWindow(report) { Owner = this }.ShowDialog();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"Tippy could not prepare the report.\n\n{exception.Message}",
                "Tippy support report", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
