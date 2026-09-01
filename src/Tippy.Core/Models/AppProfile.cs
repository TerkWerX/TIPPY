namespace Tippy.Core.Models;

public enum AppTheme
{
    Dark,
    Light
}

public enum PedalLayoutMode
{
    Auto,
    Stacked,
    SideBySide
}

public sealed class AppProfile
{
    public const int MaxBanks = 3;
    public int SchemaVersion { get; set; } = 3;
    public string Name { get; set; } = "Default";
    public AppTheme Theme { get; set; } = AppTheme.Dark;
    public int ActiveBankIndex { get; set; }
    public string BankHotkey { get; set; } = "Ctrl+Alt+B";
    public bool StartMinimized { get; set; }
    public PedalLayoutMode PedalLayout { get; set; } = PedalLayoutMode.Auto;
    public List<PedalDeviceProfile> Devices { get; set; } = [];
    public List<LearnedPedalDefinition> LearnedPedals { get; set; } = [];

    public void Normalize()
    {
        var previousSchema = SchemaVersion;
        SchemaVersion = 3;
        Name = string.IsNullOrWhiteSpace(Name) ? "Default" : Name.Trim();
        ActiveBankIndex = Math.Clamp(ActiveBankIndex, 0, MaxBanks - 1);
        BankHotkey = string.IsNullOrWhiteSpace(BankHotkey) ? "Ctrl+Alt+B" : BankHotkey.Trim();
        Devices ??= [];
        LearnedPedals ??= [];
        foreach (var learned in LearnedPedals)
        {
            learned.Normalize();
        }
        foreach (var device in Devices)
        {
            if (previousSchema < 3)
            {
                device.ActiveBankIndex = ActiveBankIndex;
            }
            device.Normalize();
        }
    }

    public int NextBank()
    {
        ActiveBankIndex = (ActiveBankIndex + 1) % MaxBanks;
        return ActiveBankIndex;
    }
}
