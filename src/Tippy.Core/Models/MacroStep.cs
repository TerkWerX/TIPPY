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
    GamepadAxis,
    LaunchProgram,
    PowerShellCommand,
    MouseMove,
    Midi,
    Osc
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
    public string? EndpointPresetId { get; set; }

    public MacroStep Clone() => new()
    {
        Type = Type,
        Keys = [.. Keys],
        Value = Value,
        DurationMs = DurationMs,
        Amount = Amount,
        Arguments = Arguments,
        WorkingDirectory = WorkingDirectory,
        EndpointPresetId = EndpointPresetId
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
        MacroStepType.GamepadAxis => $"Gamepad {Value ?? "Left X"} {(Amount >= 0 ? "+" : string.Empty)}{Amount}%",
        MacroStepType.LaunchProgram => $"Run {Path.GetFileName(Value) ?? "program"}",
        MacroStepType.PowerShellCommand => $"PowerShell: {Trim(Value, 28)}",
        MacroStepType.MouseMove => $"Mouse {Value ?? "move"} at {Math.Abs(Amount)} px/tick",
        MacroStepType.Midi => $"MIDI {Value ?? "message"}",
        MacroStepType.Osc => $"OSC {Value ?? "/tippy"}",
        _ => Type.ToString()
    };

    private static string Trim(string? value, int max)
    {
        var text = value ?? string.Empty;
        return text.Length <= max ? text : text[..(max - 1)] + "…";
    }
}
