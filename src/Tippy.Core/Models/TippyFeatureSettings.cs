namespace Tippy.Core.Models;

public sealed class TippyVariable
{
    public string Name { get; set; } = "variable";
    public string Value { get; set; } = string.Empty;

    public void Normalize()
    {
        Name = new string((Name ?? string.Empty).Where(character => char.IsLetterOrDigit(character) || character == '_').ToArray());
        Name = string.IsNullOrWhiteSpace(Name) ? "variable" : Name;
        Value ??= string.Empty;
    }
}

public sealed class MacroSafetySettings
{
    public int MaximumMacroSeconds { get; set; } = 30;
    public int MaximumRepeatSeconds { get; set; } = 20;
    public int MaximumSteps { get; set; } = 500;
    public string EmergencyStopHotkey { get; set; } = "Ctrl+Alt+Escape";

    public void Normalize()
    {
        MaximumMacroSeconds = Math.Clamp(MaximumMacroSeconds, 1, 600);
        MaximumRepeatSeconds = Math.Clamp(MaximumRepeatSeconds, 1, 600);
        MaximumSteps = Math.Clamp(MaximumSteps, 10, 10_000);
        EmergencyStopHotkey = string.IsNullOrWhiteSpace(EmergencyStopHotkey)
            ? "Ctrl+Alt+Escape"
            : EmergencyStopHotkey.Trim();
    }
}

public sealed class OverlaySettings
{
    public bool Enabled { get; set; }
    public int VisibleSeconds { get; set; } = 3;
    public double Left { get; set; } = 24;
    public double Top { get; set; } = 24;

    public void Normalize()
    {
        VisibleSeconds = Math.Clamp(VisibleSeconds, 1, 30);
        if (!double.IsFinite(Left)) Left = 24;
        if (!double.IsFinite(Top)) Top = 24;
    }
}

public sealed class MidiOutputSettings
{
    public string PreferredOutputName { get; set; } = string.Empty;

    public void Normalize()
    {
        PreferredOutputName = PreferredOutputName?.Trim() ?? string.Empty;
    }

    public MidiOutputSettings Clone() => new() { PreferredOutputName = PreferredOutputName };
}

public sealed class RawInputPedalDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string DevicePath { get; set; } = string.Empty;
    public string DisplayName { get; set; } = "Keyboard-style pedal";
    public List<RawInputSwitchMapping> Switches { get; set; } = [];

    public void Normalize()
    {
        Id = string.IsNullOrWhiteSpace(Id) ? Guid.NewGuid().ToString("N") : Id.Trim();
        DevicePath = DevicePath?.Trim() ?? string.Empty;
        DisplayName = string.IsNullOrWhiteSpace(DisplayName) ? "Keyboard-style pedal" : DisplayName.Trim();
        Switches ??= [];
        foreach (var mapping in Switches) mapping.Normalize();
    }
}

public sealed class RawInputSwitchMapping
{
    public int VirtualKey { get; set; }
    public int SwitchIndex { get; set; }

    public void Normalize()
    {
        VirtualKey = Math.Clamp(VirtualKey, 0, 255);
        SwitchIndex = Math.Clamp(SwitchIndex, 0, 31);
    }
}

public sealed class WindowPlacementSettings
{
    public bool HasPlacement { get; set; }
    public int Left { get; set; }
    public int Top { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool IsMaximized { get; set; }

    public void Normalize()
    {
        Left = Math.Clamp(Left, -100_000, 100_000);
        Top = Math.Clamp(Top, -100_000, 100_000);
        Width = Math.Clamp(Width, 320, 20_000);
        Height = Math.Clamp(Height, 240, 20_000);
        if (!HasPlacement)
        {
            Left = 0;
            Top = 0;
            Width = 0;
            Height = 0;
            IsMaximized = false;
        }
    }
}
