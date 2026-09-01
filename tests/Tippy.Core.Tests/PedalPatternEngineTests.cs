using System.Diagnostics;
using Tippy.Core.Input;
using Tippy.Core.Models;

namespace Tippy.Core.Tests;

public sealed class PedalPatternEngineTests
{
    [Fact]
    public void CombinationFiresOnceUntilAContributorIsReleased()
    {
        var engine = new PedalPatternEngine();
        engine.Configure([Pattern(PedalPatternType.Combination, "a:0", "b:1")]);
        var now = Stopwatch.GetTimestamp();

        Assert.Empty(engine.Press("a:0", now));
        Assert.Single(engine.Press("b:1", now + Stopwatch.Frequency / 20));
        Assert.Empty(engine.Press("b:1", now + Stopwatch.Frequency / 10));
        engine.Release("a:0");
        Assert.Single(engine.Press("a:0", now + Stopwatch.Frequency / 5));
    }

    [Fact]
    public void SequenceRequiresConfiguredOrder()
    {
        var engine = new PedalPatternEngine();
        engine.Configure([Pattern(PedalPatternType.Sequence, "a:0", "b:1", "a:0")]);
        var now = Stopwatch.GetTimestamp();

        Assert.Empty(engine.Press("a:0", now));
        Assert.Empty(engine.Press("b:1", now + 1));
        var result = engine.Press("a:0", now + 2);

        Assert.Single(result);
        Assert.Equal("Pattern", result[0].Name);
    }

    private static PedalPatternDefinition Pattern(PedalPatternType type, params string[] triggers) => new()
    {
        Name = "Pattern",
        Type = type,
        WindowMs = 500,
        Triggers = triggers.Select(value =>
        {
            var separator = value.LastIndexOf(':');
            return new PedalTriggerReference
            {
                DeviceKey = value[..separator],
                SwitchIndex = int.Parse(value[(separator + 1)..])
            };
        }).ToList(),
        Macro = new MacroDefinition
        {
            Name = "Pattern action",
            Steps = [new MacroStep { Type = MacroStepType.KeyChord, Keys = ["A"] }]
        }
    };
}
