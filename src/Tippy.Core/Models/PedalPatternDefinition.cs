namespace Tippy.Core.Models;

public enum PedalPatternType
{
    Combination,
    Sequence
}

public sealed class PedalPatternDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Foot pattern";
    public PedalPatternType Type { get; set; }
    public List<PedalTriggerReference> Triggers { get; set; } = [];
    public MacroDefinition Macro { get; set; } = new() { Name = "Foot pattern" };
    public int WindowMs { get; set; } = 500;
    public bool Enabled { get; set; } = true;

    public void Normalize()
    {
        Id = string.IsNullOrWhiteSpace(Id) ? Guid.NewGuid().ToString("N") : Id.Trim();
        Name = string.IsNullOrWhiteSpace(Name) ? "Foot pattern" : Name.Trim();
        Triggers ??= [];
        Triggers.RemoveAll(trigger => string.IsNullOrWhiteSpace(trigger.DeviceKey) || trigger.SwitchIndex < 0);
        foreach (var trigger in Triggers) trigger.Normalize();
        Macro ??= new MacroDefinition { Name = Name };
        Macro.Normalize();
        Macro.TriggerMode = MacroTriggerMode.PressOnce;
        WindowMs = Math.Clamp(WindowMs, 100, 5_000);
    }

    public PedalPatternDefinition Clone() => new()
    {
        Id = Id,
        Name = Name,
        Type = Type,
        Triggers = Triggers.Select(trigger => trigger.Clone()).ToList(),
        Macro = Macro.Clone(),
        WindowMs = WindowMs,
        Enabled = Enabled
    };
}

public sealed class PedalTriggerReference
{
    public string DeviceKey { get; set; } = string.Empty;
    public int SwitchIndex { get; set; }

    public void Normalize()
    {
        DeviceKey = DeviceKey?.Trim() ?? string.Empty;
        SwitchIndex = Math.Clamp(SwitchIndex, 0, 31);
    }

    public PedalTriggerReference Clone() => new() { DeviceKey = DeviceKey, SwitchIndex = SwitchIndex };

    public string ToTriggerId() => $"{DeviceKey}:{SwitchIndex}";
}
