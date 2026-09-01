namespace Tippy.Core.Models;

public enum MacroStepType
{
    KeyChord,
    KeyDown,
    KeyUp,
    Text,
    MouseButton,
    MouseWheel,
    Delay,
    GamepadButton,
    LaunchProgram
}

public sealed class MacroStep
{
    public MacroStepType Type { get; set; } = MacroStepType.KeyChord;
    public List<string> Keys { get; set; } = [];
    public string? Value { get; set; }
    public int DurationMs { get; set; } = 25;
    public int Amount { get; set; }
    public string? Arguments { get; set; }
    public string? WorkingDirectory { get; set; }

    public MacroStep Clone() => new()
    {
        Type = Type,
        Keys = [.. Keys],
        Value = Value,
        DurationMs = DurationMs,
        Amount = Amount,
        Arguments = Arguments,
        WorkingDirectory = WorkingDirectory
    };

    public string ToSummary() => Type switch
    {
        MacroStepType.KeyChord => Keys.Count == 0 ? "Empty chord" : string.Join(" + ", Keys),
        MacroStepType.KeyDown => $"Hold {string.Join(" + ", Keys)}",
        MacroStepType.KeyUp => $"Release {string.Join(" + ", Keys)}",
        MacroStepType.Text => $"Type “{Trim(Value, 28)}”",
        MacroStepType.MouseButton => $"Mouse {Value ?? "Left"}",
        MacroStepType.MouseWheel => $"Wheel {(Amount >= 0 ? "+" : string.Empty)}{Amount}",
        MacroStepType.Delay => $"Wait {Math.Clamp(DurationMs, 0, 60_000)} ms",
        MacroStepType.GamepadButton => $"Gamepad {Value ?? "A"}",
        MacroStepType.LaunchProgram => $"Run {Path.GetFileName(Value) ?? "program"}",
        _ => Type.ToString()
    };

    private static string Trim(string? value, int max)
    {
        var text = value ?? string.Empty;
        return text.Length <= max ? text : text[..(max - 1)] + "…";
    }
}
