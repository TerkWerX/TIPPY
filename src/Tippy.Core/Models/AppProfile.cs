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
    SideBySide,
    Tiled,
    Tabbed
}

public sealed class AppProfile
{
    public const int MaxBanks = 3;
    public int SchemaVersion { get; set; } = 5;
    public string Name { get; set; } = "Default";
    public AppTheme Theme { get; set; } = AppTheme.Dark;
    public int ActiveBankIndex { get; set; }
    public string BankHotkey { get; set; } = "Ctrl+Alt+B";
    public bool StartMinimized { get; set; }
    public PedalLayoutMode PedalLayout { get; set; } = PedalLayoutMode.Auto;
    public int TileColumns { get; set; }
    public string SelectedTabbedDeviceKey { get; set; } = string.Empty;
    public List<PedalDeviceProfile> Devices { get; set; } = [];
    public List<LearnedPedalDefinition> LearnedPedals { get; set; } = [];
    public List<ApplicationProfileRule> ApplicationProfiles { get; set; } = [];

    public void Normalize()
    {
        var previousSchema = SchemaVersion;
        SchemaVersion = 5;
        Name = string.IsNullOrWhiteSpace(Name) ? "Default" : Name.Trim();
        ActiveBankIndex = Math.Clamp(ActiveBankIndex, 0, MaxBanks - 1);
        BankHotkey = string.IsNullOrWhiteSpace(BankHotkey) ? "Ctrl+Alt+B" : BankHotkey.Trim();
        TileColumns = Math.Clamp(TileColumns, 0, 6);
        SelectedTabbedDeviceKey = SelectedTabbedDeviceKey?.Trim() ?? string.Empty;
        Devices ??= [];
        LearnedPedals ??= [];
        ApplicationProfiles ??= [];
        foreach (var learned in LearnedPedals)
        {
            learned.Normalize();
        }
        foreach (var applicationProfile in ApplicationProfiles)
        {
            applicationProfile.Normalize();
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
