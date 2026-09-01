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
    public int SchemaVersion { get; set; } = 11;
    public string Name { get; set; } = "Default";
    public AppTheme Theme { get; set; } = AppTheme.Dark;
    public int ActiveBankIndex { get; set; }
    public string BankHotkey { get; set; } = "Ctrl+Alt+B";
    public bool StartMinimized { get; set; }
    public bool IsCompactMode { get; set; }
    public bool IsSubCompactMode { get; set; }
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
    public MidiOutputSettings Midi { get; set; } = new();
    public List<RawInputPedalDefinition> RawInputPedals { get; set; } = [];
    public WindowPlacementSettings WindowPlacement { get; set; } = new();
    public Dictionary<string, LayoutWindowSizeSettings> LayoutWindowSizes { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public void Normalize()
    {
        var previousSchema = SchemaVersion;
        SchemaVersion = 11;
        Name = string.IsNullOrWhiteSpace(Name) ? "Default" : Name.Trim();
        ActiveBankIndex = Math.Clamp(ActiveBankIndex, 0, MaxBanks - 1);
        BankHotkey = string.IsNullOrWhiteSpace(BankHotkey) ? "Ctrl+Alt+B" : BankHotkey.Trim();
        if (IsSubCompactMode) IsCompactMode = false;
        TileColumns = Math.Clamp(TileColumns, 0, 6);
        SelectedTabbedDeviceKey = SelectedTabbedDeviceKey?.Trim() ?? string.Empty;
        Devices ??= [];
        LearnedPedals ??= [];
        ApplicationProfiles ??= [];
        PedalPatterns ??= [];
        Variables ??= [];
        Safety ??= new MacroSafetySettings();
        Overlay ??= new OverlaySettings();
        Midi ??= new MidiOutputSettings();
        RawInputPedals ??= [];
        WindowPlacement ??= new WindowPlacementSettings();
        LayoutWindowSizes ??= new Dictionary<string, LayoutWindowSizeSettings>(StringComparer.OrdinalIgnoreCase);
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
        Midi.Normalize();
        foreach (var rawInputPedal in RawInputPedals) rawInputPedal.Normalize();
        WindowPlacement.Normalize();
        var normalizedLayoutSizes = new Dictionary<string, LayoutWindowSizeSettings>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in LayoutWindowSizes)
        {
            if (string.IsNullOrWhiteSpace(entry.Key) || entry.Value is null) continue;
            entry.Value.Normalize();
            if (entry.Value.HasSize) normalizedLayoutSizes[entry.Key.Trim()] = entry.Value;
        }
        LayoutWindowSizes = normalizedLayoutSizes;
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
