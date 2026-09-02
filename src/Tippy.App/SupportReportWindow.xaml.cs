using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;
using Tippy.App.Services;

namespace Tippy.App;

public partial class SupportReportWindow : Window
{
    private readonly SupportReportResult _report;

    public SupportReportWindow(SupportReportResult report)
    {
        InitializeComponent();
        _report = report;
        KindText.Text = report.Kind switch
        {
            SupportReportKind.Crash => $"Crash recovery report · ID {report.ReportId}",
            SupportReportKind.UnknownPedal => $"Unknown USB pedal report · ID {report.ReportId}",
            _ => $"Diagnostic report · ID {report.ReportId}"
        };
        ReportPathText.Text = report.FilePath;
        PreviewText.Text = report.Json;
    }

    private void SaveCopy_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "JSON support report (*.json)|*.json",
            FileName = Path.GetFileName(_report.FilePath),
            DefaultExt = ".json",
            AddExtension = true
        };
        if (dialog.ShowDialog(this) != true) return;
        File.Copy(_report.FilePath, dialog.FileName, true);
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e) => Process.Start(new ProcessStartInfo
    {
        FileName = Path.GetDirectoryName(_report.FilePath)!,
        UseShellExecute = true
    });

    private void CopyIssue_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText($"{_report.IssueTitle}{Environment.NewLine}{Environment.NewLine}{_report.IssueBody}");
        KindText.Text = $"Issue title and description copied · report ID {_report.ReportId}";
    }

    private void OpenGitHub_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(_report.IssueBody);
        Process.Start(new ProcessStartInfo
        {
            FileName = _report.GitHubIssueUri.AbsoluteUri,
            UseShellExecute = true
        });
        KindText.Text = $"GitHub opened · attach {Path.GetFileName(_report.FilePath)} before submitting";
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
