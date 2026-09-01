using System.Text.Json.Serialization;

namespace Tippy.Core.Models;

public sealed class ApplicationProfileRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Application scene";
    public string ProcessName { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public string WindowTitleContains { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    // Retained so profiles created before complete application scenes continue to load.
    public List<ApplicationDeviceBank> DeviceBanks { get; set; } = [];
    public List<ApplicationDeviceScene> DeviceScenes { get; set; } = [];

    public void Normalize()
    {
        Id = string.IsNullOrWhiteSpace(Id) ? Guid.NewGuid().ToString("N") : Id.Trim();
        Name = string.IsNullOrWhiteSpace(Name) ? "Application scene" : Name.Trim();
        ExecutablePath = ExecutablePath?.Trim() ?? string.Empty;
        WindowTitleContains = WindowTitleContains?.Trim() ?? string.Empty;
        ProcessName = NormalizeProcessName(string.IsNullOrWhiteSpace(ProcessName)
            ? Path.GetFileNameWithoutExtension(ExecutablePath)
            : ProcessName);
        DeviceBanks ??= [];
        DeviceScenes ??= [];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        DeviceBanks.RemoveAll(mapping =>
        {
            mapping.Normalize();
            return string.IsNullOrWhiteSpace(mapping.DeviceKey) || !seen.Add(mapping.DeviceKey);
        });
        var seenScenes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        DeviceScenes.RemoveAll(scene =>
        {
            scene.Normalize();
            return string.IsNullOrWhiteSpace(scene.DeviceKey) || !seenScenes.Add(scene.DeviceKey);
        });
        foreach (var scene in DeviceScenes)
        {
            var legacy = DeviceBanks.FirstOrDefault(mapping =>
                mapping.DeviceKey.Equals(scene.DeviceKey, StringComparison.OrdinalIgnoreCase));
            if (legacy is not null) legacy.BankIndex = scene.ActiveBankIndex;
            else DeviceBanks.Add(new ApplicationDeviceBank
            {
                DeviceKey = scene.DeviceKey,
                BankIndex = scene.ActiveBankIndex
            });
        }
    }

    public bool Matches(string processName, string? executablePath, string? windowTitle = null)
    {
        if (!Enabled) return false;
        if (!string.IsNullOrWhiteSpace(WindowTitleContains) &&
            !(windowTitle ?? string.Empty).Contains(WindowTitleContains, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        var normalizedProcess = NormalizeProcessName(processName);
        if (!string.IsNullOrWhiteSpace(ExecutablePath) && !string.IsNullOrWhiteSpace(executablePath) &&
            string.Equals(ExecutablePath, executablePath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        return !string.IsNullOrWhiteSpace(ProcessName) &&
               ProcessName.Equals(normalizedProcess, StringComparison.OrdinalIgnoreCase);
    }

    public int GetBankIndex(string deviceKey, int fallback)
    {
        var scene = GetDeviceScene(deviceKey);
        if (scene is not null) return Math.Clamp(scene.ActiveBankIndex, 0, AppProfile.MaxBanks - 1);
        var mapping = DeviceBanks.FirstOrDefault(item =>
            item.DeviceKey.Equals(deviceKey, StringComparison.OrdinalIgnoreCase));
        return mapping is null ? fallback : Math.Clamp(mapping.BankIndex, 0, AppProfile.MaxBanks - 1);
    }

    public ApplicationDeviceScene? GetDeviceScene(string deviceKey) =>
        DeviceScenes.FirstOrDefault(item =>
            item.DeviceKey.Equals(deviceKey, StringComparison.OrdinalIgnoreCase));

    public ApplicationDeviceScene EnsureDeviceScene(PedalDeviceProfile device)
    {
        var scene = GetDeviceScene(device.DeviceKey);
        if (scene is not null)
        {
            scene.EnsureSwitchCount(device.SwitchCount);
            return scene;
        }
        scene = new ApplicationDeviceScene
        {
            DeviceKey = device.DeviceKey,
            DisplayName = device.DisplayName,
            ActiveBankIndex = DeviceBanks.FirstOrDefault(item =>
                item.DeviceKey.Equals(device.DeviceKey, StringComparison.OrdinalIgnoreCase))?.BankIndex
                ?? device.ActiveBankIndex,
            Banks = device.Banks.Select(bank => bank.Clone()).ToList()
        };
        scene.Normalize();
        DeviceScenes.Add(scene);
        return scene;
    }

    public ApplicationProfileRule Clone() => new()
    {
        Id = Id,
        Name = Name,
        ProcessName = ProcessName,
        ExecutablePath = ExecutablePath,
        WindowTitleContains = WindowTitleContains,
        Enabled = Enabled,
        DeviceBanks = DeviceBanks.Select(mapping => mapping.Clone()).ToList(),
        DeviceScenes = DeviceScenes.Select(scene => scene.Clone()).ToList()
    };

    [JsonIgnore]
    public string DisplayProcess => string.IsNullOrWhiteSpace(ProcessName) ? "No process selected" : $"{ProcessName}.exe";

    private static string NormalizeProcessName(string? processName) =>
        Path.GetFileNameWithoutExtension(processName?.Trim() ?? string.Empty);
}

public sealed class ApplicationDeviceScene
{
    public string DeviceKey { get; set; } = string.Empty;
    public string DisplayName { get; set; } = "Foot control";
    public int ActiveBankIndex { get; set; }
    public List<PedalBank> Banks { get; set; } = [];

    public void Normalize()
    {
        DeviceKey = DeviceKey?.Trim() ?? string.Empty;
        DisplayName = string.IsNullOrWhiteSpace(DisplayName) ? "Foot control" : DisplayName.Trim();
        ActiveBankIndex = Math.Clamp(ActiveBankIndex, 0, AppProfile.MaxBanks - 1);
        Banks ??= [];
        while (Banks.Count < AppProfile.MaxBanks) Banks.Add(PedalBank.Create(Banks.Count));
        if (Banks.Count > AppProfile.MaxBanks) Banks.RemoveRange(AppProfile.MaxBanks, Banks.Count - AppProfile.MaxBanks);
        var switchCount = Banks.Select(bank => bank.Bindings?.Count ?? 0).DefaultIfEmpty(3).Max();
        EnsureSwitchCount(Math.Clamp(switchCount, 1, 32));
    }

    public void EnsureSwitchCount(int switchCount)
    {
        switchCount = Math.Clamp(switchCount, 1, 32);
        while (Banks.Count < AppProfile.MaxBanks) Banks.Add(PedalBank.Create(Banks.Count, switchCount));
        foreach (var bank in Banks) bank.EnsureSwitchCount(switchCount);
    }

    public ApplicationDeviceScene Clone() => new()
    {
        DeviceKey = DeviceKey,
        DisplayName = DisplayName,
        ActiveBankIndex = ActiveBankIndex,
        Banks = Banks.Select(bank => bank.Clone()).ToList()
    };
}

public sealed class ApplicationDeviceBank
{
    public string DeviceKey { get; set; } = string.Empty;
    public int BankIndex { get; set; }

    public void Normalize()
    {
        DeviceKey = DeviceKey?.Trim() ?? string.Empty;
        BankIndex = Math.Clamp(BankIndex, 0, AppProfile.MaxBanks - 1);
    }

    public ApplicationDeviceBank Clone() => new() { DeviceKey = DeviceKey, BankIndex = BankIndex };
}
