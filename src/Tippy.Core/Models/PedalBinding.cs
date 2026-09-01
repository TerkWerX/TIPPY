namespace Tippy.Core.Models;

using System.Text.Json.Serialization;

public enum PedalBindingType
{
    Macro,
    BankNext,
    Disabled,
    ShiftLayer
}

public sealed class PedalBinding
{
    public PedalBindingType Type { get; set; } = PedalBindingType.Macro;
    public MacroDefinition Macro { get; set; } = new();
    public MacroDefinition ReleaseMacro { get; set; } = EmptyReleaseMacro();
    public int ShiftBankIndex { get; set; } = 1;

    public void Normalize()
    {
        Macro ??= new MacroDefinition();
        Macro.Normalize();
        ReleaseMacro ??= EmptyReleaseMacro();
        ReleaseMacro.Normalize();
        ShiftBankIndex = Math.Clamp(ShiftBankIndex, 0, AppProfile.MaxBanks - 1);

        // Schema 4 stored release-only behavior in the main macro. Move it
        // into the dedicated release edge without changing what old profiles do.
        if (Macro.TriggerMode == MacroTriggerMode.ReleaseOnce)
        {
            if (ReleaseMacro.Steps.Count == 0)
            {
                ReleaseMacro = Macro.Clone();
            }
            ReleaseMacro.TriggerMode = MacroTriggerMode.ReleaseOnce;
            Macro = new MacroDefinition { Name = "On press" };
        }
        else
        {
            ReleaseMacro.TriggerMode = MacroTriggerMode.ReleaseOnce;
        }
    }

    public static PedalBinding Empty(int switchIndex) => new()
    {
        Macro = new MacroDefinition { Name = $"Pedal {switchIndex + 1}" },
        ReleaseMacro = EmptyReleaseMacro()
    };

    public PedalBinding Clone() => new()
    {
        Type = Type,
        Macro = Macro.Clone(),
        ReleaseMacro = ReleaseMacro.Clone(),
        ShiftBankIndex = ShiftBankIndex
    };

    [JsonIgnore]
    public bool HasPressAction => Type == PedalBindingType.Macro && Macro.Steps.Count > 0;

    [JsonIgnore]
    public bool HasReleaseAction => Type == PedalBindingType.Macro && ReleaseMacro.Steps.Count > 0;

    [JsonIgnore]
    public string DisplayName => Type switch
    {
        PedalBindingType.BankNext => "Next bank",
        PedalBindingType.Disabled => "Disabled",
        PedalBindingType.ShiftLayer => $"Shift to Bank {ShiftBankIndex + 1}",
        _ when HasPressAction => Macro.Name,
        _ when HasReleaseAction => ReleaseMacro.Name,
        _ => "No action"
    };

    [JsonIgnore]
    public string Summary => Type switch
    {
        PedalBindingType.BankNext => "Cycle to the next macro bank",
        PedalBindingType.Disabled => "This switch does nothing",
        PedalBindingType.ShiftLayer => $"Hold for Bank {ShiftBankIndex + 1}; release to restore",
        _ when HasPressAction && HasReleaseAction => $"Press: {Macro.Summary} · Release: {ReleaseMacro.Summary}",
        _ when HasPressAction => $"Press: {Macro.Summary}",
        _ when HasReleaseAction => $"Release: {ReleaseMacro.Summary}",
        _ => "No action"
    };

    private static MacroDefinition EmptyReleaseMacro() => new()
    {
        Name = "On release",
        TriggerMode = MacroTriggerMode.ReleaseOnce
    };
}
