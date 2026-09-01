namespace Tippy.Core.Models;

using System.Text.Json.Serialization;

public enum MacroTriggerMode
{
    PressOnce,
    WhileHeld
}

public sealed class MacroDefinition
{
    public string Name { get; set; } = "Unassigned";
    public MacroTriggerMode TriggerMode { get; set; }
    public List<MacroStep> Steps { get; set; } = [];

    public void Normalize()
    {
        Name = string.IsNullOrWhiteSpace(Name) ? "Unnamed macro" : Name.Trim();
        Steps ??= [];
        foreach (var step in Steps)
        {
            step.Keys ??= [];
            step.DurationMs = Math.Clamp(step.DurationMs, 0, 60_000);
        }
    }

    public MacroDefinition Clone() => new()
    {
        Name = Name,
        TriggerMode = TriggerMode,
        Steps = Steps.Select(step => step.Clone()).ToList()
    };

    [JsonIgnore]
    public string Summary => Steps.Count switch
    {
        0 => "No action",
        1 => Steps[0].ToSummary(),
        _ => $"{Steps[0].ToSummary()}  +{Steps.Count - 1} more"
    };
}
