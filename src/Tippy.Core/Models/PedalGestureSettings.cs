namespace Tippy.Core.Models;

public sealed class PedalGestureSettings
{
    public MacroDefinition DoubleTapMacro { get; set; } = Named("Double tap");
    public MacroDefinition LongPressMacro { get; set; } = Named("Long press");
    public int DoubleTapWindowMs { get; set; } = 320;
    public int LongPressThresholdMs { get; set; } = 550;
    public bool RepeatWhileHeld { get; set; }
    public int RepeatDelayMs { get; set; } = 450;
    public int RepeatIntervalMs { get; set; } = 110;
    public bool Toggle { get; set; }

    public void Normalize()
    {
        DoubleTapMacro ??= Named("Double tap");
        LongPressMacro ??= Named("Long press");
        DoubleTapMacro.Normalize();
        LongPressMacro.Normalize();
        DoubleTapMacro.TriggerMode = MacroTriggerMode.PressOnce;
        LongPressMacro.TriggerMode = MacroTriggerMode.PressOnce;
        DoubleTapWindowMs = Math.Clamp(DoubleTapWindowMs, 150, 900);
        LongPressThresholdMs = Math.Clamp(LongPressThresholdMs, 250, 3_000);
        RepeatDelayMs = Math.Clamp(RepeatDelayMs, 100, 5_000);
        RepeatIntervalMs = Math.Clamp(RepeatIntervalMs, 30, 5_000);
    }

    public PedalGestureSettings Clone() => new()
    {
        DoubleTapMacro = DoubleTapMacro.Clone(),
        LongPressMacro = LongPressMacro.Clone(),
        DoubleTapWindowMs = DoubleTapWindowMs,
        LongPressThresholdMs = LongPressThresholdMs,
        RepeatWhileHeld = RepeatWhileHeld,
        RepeatDelayMs = RepeatDelayMs,
        RepeatIntervalMs = RepeatIntervalMs,
        Toggle = Toggle
    };

    private static MacroDefinition Named(string name) => new() { Name = name };
}
