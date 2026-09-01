using System.Text.Json.Serialization;

namespace Tippy.Core.Models;

public sealed class ApplicationProfileRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Application profile";
    public string ProcessName { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public List<ApplicationDeviceBank> DeviceBanks { get; set; } = [];

    public void Normalize()
    {
        Id = string.IsNullOrWhiteSpace(Id) ? Guid.NewGuid().ToString("N") : Id.Trim();
        Name = string.IsNullOrWhiteSpace(Name) ? "Application profile" : Name.Trim();
        ExecutablePath = ExecutablePath?.Trim() ?? string.Empty;
        ProcessName = NormalizeProcessName(string.IsNullOrWhiteSpace(ProcessName)
            ? Path.GetFileNameWithoutExtension(ExecutablePath)
            : ProcessName);
        DeviceBanks ??= [];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        DeviceBanks.RemoveAll(mapping =>
        {
            mapping.Normalize();
            return string.IsNullOrWhiteSpace(mapping.DeviceKey) || !seen.Add(mapping.DeviceKey);
        });
    }

    public bool Matches(string processName, string? executablePath)
    {
        if (!Enabled) return false;
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
        var mapping = DeviceBanks.FirstOrDefault(item =>
            item.DeviceKey.Equals(deviceKey, StringComparison.OrdinalIgnoreCase));
        return mapping is null ? fallback : Math.Clamp(mapping.BankIndex, 0, AppProfile.MaxBanks - 1);
    }

    public ApplicationProfileRule Clone() => new()
    {
        Id = Id,
        Name = Name,
        ProcessName = ProcessName,
        ExecutablePath = ExecutablePath,
        Enabled = Enabled,
        DeviceBanks = DeviceBanks.Select(mapping => mapping.Clone()).ToList()
    };

    [JsonIgnore]
    public string DisplayProcess => string.IsNullOrWhiteSpace(ProcessName) ? "No process selected" : $"{ProcessName}.exe";

    private static string NormalizeProcessName(string? processName) =>
        Path.GetFileNameWithoutExtension(processName?.Trim() ?? string.Empty);
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
