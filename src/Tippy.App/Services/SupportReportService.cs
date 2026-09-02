using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Tippy.App.Models;

namespace Tippy.App.Services;

public enum SupportReportKind
{
    Crash,
    UnknownPedal,
    Diagnostics
}

public sealed record SupportReportResult(
    SupportReportKind Kind,
    string ReportId,
    string FilePath,
    string Json,
    string IssueTitle,
    string IssueBody,
    Uri GitHubIssueUri);

/// <summary>
/// Creates local, reviewable support bundles. This service never uploads data and never stores credentials.
/// </summary>
public sealed partial class SupportReportService
{
    public const string RepositoryIssuesUrl = "https://github.com/TerkWerX/TIPPY/issues/new";
    private const int MaximumCrashCharacters = 32_768;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public SupportReportService(string appDataDirectory)
    {
        ReportsDirectory = Path.Combine(appDataDirectory, "SupportReports");
        Directory.CreateDirectory(ReportsDirectory);
    }

    public string ReportsDirectory { get; }

    public SupportReportResult CreateUnknownPedalReport(
        PedalDeviceInfo device,
        HidCandidateInfo? candidate = null,
        IEnumerable<string>? rawReports = null)
    {
        var details = new
        {
            Device = DescribeDevice(device),
            HidDescriptor = candidate is null ? null : new
            {
                candidate.ReportLength,
                candidate.ReportDescriptorHash,
                candidate.LooksLikePedal,
                DevicePathHash = Hash(candidate.DevicePath)
            },
            RawInputSamples = CleanRawReports(rawReports),
            RegistryResult = "No confident match was found in the installed pedal registry."
        };
        var title = $"[Unknown pedal] VID_{device.VendorId:X4} PID_{device.ProductId:X4}";
        return WriteReport(SupportReportKind.UnknownPedal, title, details,
            $"Device: {Clean(device.DisplayName, 160)}\n" +
            $"USB identity: VID_{device.VendorId:X4} PID_{device.ProductId:X4}\n" +
            $"Reported switches: {device.SwitchCount}; decoder: {Clean(device.DecoderName, 100)}");
    }

    public SupportReportResult CreateCrashReport(PreviousCrashSession previous, string crashLogPath)
    {
        var details = new
        {
            PreviousSession = new
            {
                previous.StartedAt,
                previous.Version
            },
            RecentCrashLog = ReadSanitizedTail(crashLogPath, MaximumCrashCharacters)
        };
        var title = $"[Crash] Tippy {Clean(previous.Version, 80)} did not close cleanly";
        return WriteReport(SupportReportKind.Crash, title, details,
            $"Previous session: Tippy {Clean(previous.Version, 80)}\n" +
            $"Started: {(previous.StartedAt == DateTimeOffset.MinValue ? "unknown" : previous.StartedAt.ToString("O"))}");
    }

    public SupportReportResult CreateDiagnosticsReport(
        IEnumerable<PedalDeviceInfo> devices,
        IReadOnlyDictionary<string, IReadOnlyCollection<string>>? rawReports = null)
    {
        var snapshots = devices.Select(device => new
        {
            Device = DescribeDevice(device),
            RawInputSamples = rawReports is not null && rawReports.TryGetValue(device.DeviceKey, out var samples)
                ? CleanRawReports(samples)
                : []
        }).ToArray();
        var details = new { ConnectedDevices = snapshots };
        return WriteReport(SupportReportKind.Diagnostics, "[Support report] Tippy diagnostics", details,
            $"Connected foot controls included: {snapshots.Length}");
    }

    private SupportReportResult WriteReport(SupportReportKind kind, string title, object details, string summary)
    {
        var reportId = Guid.NewGuid().ToString("N")[..16].ToUpperInvariant();
        var document = new
        {
            SchemaVersion = 1,
            ReportId = reportId,
            Kind = kind.ToString(),
            GeneratedUtc = DateTimeOffset.UtcNow,
            Application = new
            {
                Name = "Tippy",
                Version = typeof(SupportReportService).Assembly.GetName().Version?.ToString() ?? "unknown"
            },
            Environment = new
            {
                OperatingSystem = RuntimeInformation.OSDescription,
                RuntimeInformation.OSArchitecture,
                RuntimeInformation.ProcessArchitecture,
                Runtime = RuntimeInformation.FrameworkDescription,
                UiCulture = CultureInfo.CurrentUICulture.Name
            },
            Privacy = new
            {
                Upload = "No automatic upload. This file remains local until the user deliberately attaches it to an issue.",
                Included = "Tippy/Windows versions, non-secret USB identity, decoder and bounded diagnostic data.",
                Excluded = "Typed text, macro contents, profile contents, user name, computer name, and raw device paths.",
                PathHandling = "Device paths are represented only by one-way SHA-256 hashes; paths found in crash text are redacted."
            },
            Details = details
        };
        var json = JsonSerializer.Serialize(document, JsonOptions);
        var prefix = kind switch
        {
            SupportReportKind.Crash => "crash",
            SupportReportKind.UnknownPedal => "unknown-pedal",
            _ => "diagnostics"
        };
        var fileName = $"tippy-{prefix}-{DateTime.Now:yyyyMMdd-HHmmss}-{reportId}.json";
        var filePath = Path.Combine(ReportsDirectory, fileName);
        File.WriteAllText(filePath, json, new UTF8Encoding(false));

        var issueBody = $"""
            Tippy generated privacy-safe support report `{fileName}` (report ID `{reportId}`).

            {summary}

            Please drag the JSON report into this issue after reviewing its contents. Nothing was uploaded automatically.

            What I was doing when this happened:

            Steps to reproduce:

            - [ ] I reviewed the report and removed anything else I do not want to share.
            """;
        var issueUri = new Uri($"{RepositoryIssuesUrl}?title={Uri.EscapeDataString(title)}&body={Uri.EscapeDataString(issueBody)}");
        return new SupportReportResult(kind, reportId, filePath, json, title, issueBody, issueUri);
    }

    private static object DescribeDevice(PedalDeviceInfo device) => new
    {
        DisplayName = Clean(device.DisplayName, 160),
        Manufacturer = Clean(device.Manufacturer, 120),
        Vid = $"{device.VendorId:X4}",
        Pid = $"{device.ProductId:X4}",
        device.SwitchCount,
        Decoder = Clean(device.DecoderName, 120),
        DevicePathHash = Hash(device.DevicePath),
        DeviceKeyHash = Hash(device.DeviceKey)
    };

    private static string[] CleanRawReports(IEnumerable<string>? reports)
    {
        if (reports is null) return [];
        return reports
            .Select(report => NonHexRegex().Replace(report ?? string.Empty, string.Empty).ToUpperInvariant())
            .Where(report => report.Length > 0)
            .Select(report => report[..Math.Min(report.Length, 512)])
            .Distinct(StringComparer.Ordinal)
            .Take(20)
            .ToArray();
    }

    private static string ReadSanitizedTail(string path, int maximumCharacters)
    {
        if (!File.Exists(path)) return "No exception text was available.";
        try
        {
            var text = File.ReadAllText(path);
            if (text.Length > maximumCharacters) text = text[^maximumCharacters..];
            return Redact(text);
        }
        catch (Exception exception)
        {
            return $"The local crash log could not be read: {Clean(exception.GetType().Name, 80)}";
        }
    }

    internal static string Redact(string value)
    {
        var result = value ?? string.Empty;
        var replacements = new (string Value, string Token)[]
        {
            (Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "%LOCALAPPDATA%"),
            (Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "%APPDATA%"),
            (Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "%USERPROFILE%"),
            (Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar), "%TEMP%")
        };
        foreach (var (path, token) in replacements.OrderByDescending(item => item.Value.Length))
        {
            if (!string.IsNullOrWhiteSpace(path)) result = result.Replace(path, token, StringComparison.OrdinalIgnoreCase);
        }
        if (!string.IsNullOrWhiteSpace(Environment.UserName))
            result = result.Replace(Environment.UserName, "%USERNAME%", StringComparison.OrdinalIgnoreCase);
        result = UserProfilePathRegex().Replace(result, "%USERPROFILE%");
        result = DevicePathRegex().Replace(result, "<redacted-device-path>");
        result = EmailRegex().Replace(result, "<redacted-email>");
        return result;
    }

    private static string Clean(string? value, int maximumLength)
    {
        var cleaned = Redact(value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        return cleaned[..Math.Min(cleaned.Length, maximumLength)];
    }

    private static string Hash(string? value) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(value ?? string.Empty)));

    [GeneratedRegex("[^0-9A-Fa-f]")]
    private static partial Regex NonHexRegex();

    [GeneratedRegex(@"(?i)\b[A-Z]:\\Users\\[^\\\r\n]+")]
    private static partial Regex UserProfilePathRegex();

    [GeneratedRegex(@"(?i)\\\\\?\\(?:hid|usb)#[^\s\r\n]+")]
    private static partial Regex DevicePathRegex();

    [GeneratedRegex(@"(?i)\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b")]
    private static partial Regex EmailRegex();
}
