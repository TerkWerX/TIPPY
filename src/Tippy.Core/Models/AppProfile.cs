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
    public int SchemaVersion { get; set; } = 7;
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
    public List<PedalPatternDefinition> PedalPatterns { get; set; } = [];
    public List<TippyVariable> Variables { get; set; } = [];
    public MacroSafetySettings Safety { get; set; } = new();
    public OverlaySettings Overlay { get; set; } = new();
    public List<RawInputPedalDefinition> RawInputPedals { get; set; } = [];
    public WindowPlacementSettings WindowPlacement { get; set; } = new();

    public void Normalize()
    {
        var previousSchema = SchemaVersion;
        SchemaVersion = 7;
        Name = string.IsNullOrWhiteSpace(Name) ? "Default" : Name.Trim();
        ActiveBankIndex = Math.Clamp(ActiveBankIndex, 0, MaxBanks - 1);
        BankHotkey = string.IsNullOrWhiteSpace(BankHotkey) ? "Ctrl+Alt+B" : BankHotkey.Trim();
        TileColumns = Math.Clamp(TileColumns, 0, 6);
        SelectedTabbedDeviceKey = SelectedTabbedDeviceKey?.Trim() ?? string.Empty;
        Devices ??= [];
        LearnedPedals ??= [];
        ApplicationProfiles ??= [];
        PedalPatterns ??= [];
        Variables ??= [];
        Safety ??= new MacroSafetySettings();
        Overlay ??= new OverlaySettings();
        RawInputPedals ??= [];
        WindowPlacement ??= new WindowPlacementSettings();
        foreach (var learned in LearnedPedals)
        {
            learned.Normalize();
        }
        foreach (var applicationProfile in ApplicationProfiles)
        {
            applicationProfile.Normalize();
        }
        foreach (var pattern in PedalPatterns) pattern.Normalize();
        foreach (var variable in Variables) variable.Normalize();
        Safety.Normalize();
        Overlay.Normalize();
        foreach (var rawInputPedal in RawInputPedals) rawInputPedal.Normalize();
        WindowPlacement.Normalize();
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
