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
    public PedalGestureSettings Gestures { get; set; } = new();
    public int ShiftBankIndex { get; set; } = 1;

    public void Normalize()
    {
        Macro ??= new MacroDefinition();
        Macro.Normalize();
        ReleaseMacro ??= EmptyReleaseMacro();
        Gestures ??= new PedalGestureSettings();
        ReleaseMacro.Normalize();
        Gestures.Normalize();
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
        Gestures = Gestures.Clone(),
        ShiftBankIndex = ShiftBankIndex
    };

    [JsonIgnore]
    public bool HasPressAction => Type == PedalBindingType.Macro && Macro.Steps.Count > 0;

    [JsonIgnore]
    public bool HasReleaseAction => Type == PedalBindingType.Macro && ReleaseMacro.Steps.Count > 0;

    [JsonIgnore]
    public bool HasGestureAction => Type == PedalBindingType.Macro &&
        (Gestures.DoubleTapMacro.Steps.Count > 0 || Gestures.LongPressMacro.Steps.Count > 0 ||
         Gestures.RepeatWhileHeld || Gestures.Toggle);

    [JsonIgnore]
    public string DisplayName => Type switch
    {
        PedalBindingType.BankNext => "Next bank",
        PedalBindingType.Disabled => "Disabled",
        PedalBindingType.ShiftLayer => $"Shift to Bank {ShiftBankIndex + 1}",
        _ when HasPressAction => Macro.Name,
        _ when HasReleaseAction => ReleaseMacro.Name,
        _ when Gestures.DoubleTapMacro.Steps.Count > 0 => Gestures.DoubleTapMacro.Name,
        _ when Gestures.LongPressMacro.Steps.Count > 0 => Gestures.LongPressMacro.Name,
        _ => "No action"
    };

    [JsonIgnore]
    public string Summary => Type switch
    {
        PedalBindingType.BankNext => "Cycle to the next macro bank",
        PedalBindingType.Disabled => "This switch does nothing",
        PedalBindingType.ShiftLayer => $"Hold for Bank {ShiftBankIndex + 1}; release to restore",
        _ when HasPressAction || HasReleaseAction || HasGestureAction => BuildMacroSummary(),
        _ => "No action"
    };

    private string BuildMacroSummary()
    {
        var parts = new List<string>();
        if (HasPressAction) parts.Add($"Press: {Macro.Summary}");
        if (Gestures.DoubleTapMacro.Steps.Count > 0) parts.Add($"Double: {Gestures.DoubleTapMacro.Summary}");
        if (Gestures.LongPressMacro.Steps.Count > 0) parts.Add($"Hold: {Gestures.LongPressMacro.Summary}");
        if (Gestures.RepeatWhileHeld) parts.Add("Repeat while held");
        if (Gestures.Toggle) parts.Add("Toggle on/off");
        if (HasReleaseAction) parts.Add($"Release: {ReleaseMacro.Summary}");
        return string.Join(" · ", parts);
    }

    private static MacroDefinition EmptyReleaseMacro() => new()
    {
        Name = "On release",
        TriggerMode = MacroTriggerMode.ReleaseOnce
    };
}
