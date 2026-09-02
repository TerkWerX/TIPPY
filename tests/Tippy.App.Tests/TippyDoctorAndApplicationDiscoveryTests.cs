using System.Text.Json;
using System.Text.Json.Serialization;
using Tippy.App.Models;
using Tippy.App.Services;
using Tippy.Core.Models;

namespace Tippy.App.Tests;

public sealed class TippyDoctorAndApplicationDiscoveryTests
{
    [Fact]
    public void CompatibleApplicationScanCoversEveryShortcutCatalogApplication()
    {
        var catalog = ApplicationShortcutCatalog.Create();
        var candidates = catalog.Select(profile =>
            new ApplicationInstallCandidate(profile.Name, string.Empty, string.Empty, "Installed programs"));

        var matches = InstalledApplicationScanner.MatchCandidates(catalog, candidates);

        Assert.Equal(catalog.Count, matches.Count);
        Assert.Equal(catalog.Select(profile => profile.Name).Order(),
            matches.Select(match => match.CatalogProfile.Name).Order());
    }

    [Fact]
    public void CompatibleApplicationScanMatchesKnownProcessesAndCombinesEvidence()
    {
        ApplicationInstallCandidate[] candidates =
        [
            new("Microsoft 365 Word", @"C:\Program Files\Microsoft Office\root\Office16\WINWORD.EXE", "WINWORD", "Installed programs"),
            new("Quarterly plan - Word", @"C:\Program Files\Microsoft Office\root\Office16\WINWORD.EXE", "WINWORD", "Running now"),
            new("OBS Studio", @"C:\Program Files\obs-studio\bin\64bit\obs64.exe", "obs64", "Start menu"),
            new("Blender 4.5", @"C:\Program Files\Blender Foundation\Blender\blender.exe", "blender", "Installed programs"),
            new("Windows Notepad", @"C:\Windows\System32\notepad.exe", "notepad", "Running now"),
            new("ProjectLibre", @"C:\Program Files\ProjectLibre\projectlibre.exe", "projectlibre", "Installed programs")
        ];

        var matches = InstalledApplicationScanner.MatchCandidates(ApplicationShortcutCatalog.Create(), candidates);

        Assert.Contains(matches, match => match.CatalogProfile.Name == "Microsoft Word" &&
                                          match.Evidence.Contains("Installed programs") &&
                                          match.Evidence.Contains("Running now"));
        Assert.Contains(matches, match => match.CatalogProfile.Name == "OBS Studio" && match.ProcessName == "obs64");
        Assert.Contains(matches, match => match.CatalogProfile.Name == "Blender");
        Assert.DoesNotContain(matches, match => match.CatalogProfile.Name == "Notepad++");
        Assert.DoesNotContain(matches, match => match.CatalogProfile.Name == "Microsoft Project");
    }

    [Fact]
    public async Task DoctorProducesPrivacySafeReadinessReport()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"tippy-doctor-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var profile = new AppProfile();
            profile.Normalize();
            var store = new ProfileStore(directory, directory);
            await store.SaveDefaultAsync(profile);
            await store.SaveDefaultAsync(profile);
            await File.WriteAllTextAsync(Path.Combine(directory, "pedal_registry.json"),
                """{"registry_version":"test","devices":[]}""");

            var report = new TippyDoctorService().Run(new TippyDoctorContext
            {
                ProfileStore = store,
                Profile = profile,
                PedalRegistry = new PedalRegistryService(directory),
                ConnectedDevices = [],
                HidListening = true,
                BankHotkeyRegistered = true,
                EmergencyHotkeyRegistered = true,
                StartupRegistrationProbe = () => throw new IOException($"Probe failed under {directory}"),
                GamepadProbe = () => (false, "ViGEmBus is not installed; gamepad output is optional.")
            });

            Assert.Equal("Action required", report.Overall);
            Assert.Equal(1, report.Failed);
            Assert.Contains(report.Checks, check => check.Name == "Live profile" && check.Status == TippyDoctorStatus.Pass);
            Assert.Contains(report.Checks, check => check.Name == "Connected pedals" && check.Status == TippyDoctorStatus.Warning);

            var json = JsonSerializer.Serialize(report, new JsonSerializerOptions
            {
                Converters = { new JsonStringEnumConverter() }
            });
            Assert.DoesNotContain(directory, json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("default.tippy.json", json, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
