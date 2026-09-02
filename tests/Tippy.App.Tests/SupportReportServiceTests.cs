using System.Text.Json;
using Tippy.App.Models;
using Tippy.App.Services;

namespace Tippy.App.Tests;

public sealed class SupportReportServiceTests
{
    [Fact]
    public void UnknownPedalReportIsLocalBoundedAndDoesNotExposeDevicePaths()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var service = new SupportReportService(directory);
            var device = new PedalDeviceInfo(
                @"\\?\hid#vid_1234&pid_5678#SUPER-SECRET-SERIAL",
                "Mystery pedal secret.person@example.com",
                0x1234,
                0x5678,
                @"\\?\hid#vid_1234&pid_5678#SUPER-SECRET-SERIAL",
                "Generic HID",
                4,
                "Example");
            var candidate = new HidCandidateInfo(device.DevicePath, device.DisplayName, device.Manufacturer,
                device.VendorId, device.ProductId, 8, new string('A', 64), true);

            var report = service.CreateUnknownPedalReport(device, candidate,
                Enumerable.Range(0, 30).Select(index => $"00 01 {index:X2}"));

            Assert.True(File.Exists(report.FilePath));
            Assert.StartsWith(Path.GetFullPath(service.ReportsDirectory), Path.GetFullPath(report.FilePath));
            Assert.DoesNotContain("SUPER-SECRET-SERIAL", report.Json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("secret.person@example.com", report.Json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("redacted-email", report.Json);
            Assert.Contains("1234", report.Json);
            Assert.Contains("5678", report.Json);
            using var json = JsonDocument.Parse(report.Json);
            var samples = json.RootElement.GetProperty("details").GetProperty("rawInputSamples");
            Assert.True(samples.GetArrayLength() <= 20);
            Assert.Equal("github.com", report.GitHubIssueUri.Host);
            Assert.Contains("Nothing was uploaded automatically", report.IssueBody);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void CrashReportRedactsUserPathsAndEmailAddresses()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var crashLog = Path.Combine(directory, "crash.log");
            File.WriteAllText(crashLog,
                "System.InvalidOperationException at C:\\Users\\SecretPerson\\source\\Tippy.cs:42\n" +
                @"device \\?\hid#vid_1234&pid_5678#SUPER-SECRET-SERIAL" + "\n" +
                "contact secret.person@example.com\n");
            var service = new SupportReportService(directory);

            var report = service.CreateCrashReport(
                new PreviousCrashSession(44, DateTimeOffset.UtcNow, "0.6.0"), crashLog);

            Assert.DoesNotContain("SecretPerson", report.Json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("SUPER-SECRET-SERIAL", report.Json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("secret.person@example.com", report.Json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("%USERPROFILE%", report.Json);
            Assert.Contains("redacted-device-path", report.Json);
            Assert.Contains("redacted-email", report.Json);
            Assert.Contains("InvalidOperationException", report.Json);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void FatalCrashKeepsRecoveryMarkerDuringShutdown()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var crashed = new CrashRecoveryService(directory);
            Assert.Null(crashed.BeginSession());
            crashed.Log(new InvalidOperationException("fatal"), "test", true);
            crashed.CompleteSession();

            var recovery = new CrashRecoveryService(directory);
            Assert.NotNull(recovery.BeginSession());
            recovery.CompleteSession();
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"tippy-support-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
