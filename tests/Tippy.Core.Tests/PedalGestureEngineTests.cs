using Tippy.Core.Input;
using Tippy.Core.Models;

namespace Tippy.Core.Tests;

public sealed class PedalGestureEngineTests
{
    [Fact]
    public async Task DoubleTapSuppressesSingleTap()
    {
        using var engine = new PedalGestureEngine();
        var invocations = new List<PedalGestureInvocation>();
        engine.Invoked += (_, invocation) => invocations.Add(invocation);
        var binding = Binding("Single");
        binding.Gestures.DoubleTapMacro = Macro("Double");
        binding.Gestures.DoubleTapWindowMs = 200;

        engine.Press("device:0", binding);
        engine.Release("device:0");
        await Task.Delay(30);
        engine.Press("device:0", binding);
        engine.Release("device:0");
        await Task.Delay(260);

        Assert.Single(invocations, item => item.Gesture == "Double tap");
        Assert.DoesNotContain(invocations, item => item.Macro.Name == "Single");
    }

    [Fact]
    public async Task LongPressReplacesTap()
    {
        using var engine = new PedalGestureEngine();
        var completion = new TaskCompletionSource<PedalGestureInvocation>();
        engine.Invoked += (_, invocation) => completion.TrySetResult(invocation);
        var binding = Binding("Tap");
        binding.Gestures.LongPressMacro = Macro("Hold");
        binding.Gestures.LongPressThresholdMs = 250;

        engine.Press("device:1", binding);
        var result = await completion.Task.WaitAsync(TimeSpan.FromSeconds(2));
        engine.Release("device:1");

        Assert.Equal("Long press", result.Gesture);
        Assert.Equal("Hold", result.Macro.Name);
    }

    [Fact]
    public void ToggleAlternatesHeldState()
    {
        using var engine = new PedalGestureEngine();
        var invocations = new List<PedalGestureInvocation>();
        engine.Invoked += (_, invocation) => invocations.Add(invocation);
        var binding = Binding("PTT");
        binding.Gestures.Toggle = true;

        engine.Press("device:2", binding);
        engine.Release("device:2");
        engine.Press("device:2", binding);

        Assert.Equal([true, false], invocations.Select(item => item.IsPressed));
    }

    private static PedalBinding Binding(string name) => new()
    {
        Type = PedalBindingType.Macro,
        Macro = Macro(name)
    };

    private static MacroDefinition Macro(string name) => new()
    {
        Name = name,
        Steps = [new MacroStep { Type = MacroStepType.KeyChord, Keys = ["A"] }]
    };
}
