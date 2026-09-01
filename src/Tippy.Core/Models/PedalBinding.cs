namespace Tippy.Core.Models;

using System.Text.Json.Serialization;

public enum PedalBindingType
{
    Macro,
    BankNext,
    Disabled
}

public sealed class PedalBinding
{
    public PedalBindingType Type { get; set; } = PedalBindingType.Macro;
    public MacroDefinition Macro { get; set; } = new();

    public void Normalize()
    {
        Macro ??= new MacroDefinition();
        Macro.Normalize();
    }

    public static PedalBinding Empty(int switchIndex) => new()
    {
        Macro = new MacroDefinition { Name = $"Pedal {switchIndex + 1}" }
    };

    public PedalBinding Clone() => new() { Type = Type, Macro = Macro.Clone() };

    [JsonIgnore]
    public string DisplayName => Type switch
    {
        PedalBindingType.BankNext => "Next bank",
        PedalBindingType.Disabled => "Disabled",
        _ => Macro.Name
    };

    [JsonIgnore]
    public string Summary => Type switch
    {
        PedalBindingType.BankNext => "Cycle to the next macro bank",
        PedalBindingType.Disabled => "This switch does nothing",
        _ => Macro.Summary
    };
}
