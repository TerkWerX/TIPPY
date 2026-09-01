using Tippy.Core.Input;

namespace Tippy.Core.Tests;

public sealed class LearnedReportDecoderTests
{
    [Fact]
    public void LearnsSnapshotBitMaskAndSimultaneousPresses()
    {
        var builder = new LearnedDefinitionBuilder();
        var definition = builder.Build("Test", "Buttons", 1, 2,
            [[0, 1, 0], [0, 2, 0], [0, 4, 0]],
            [[0, 0, 0], [0, 0, 0], [0, 0, 0]]);
        var decoder = new LearnedReportDecoder(definition);
        var state = new bool[3];

        var changes = decoder.Decode([0, 5, 0], state);

        Assert.Equal(2, changes.Count);
        Assert.Equal([true, false, true], state);
        Assert.All(definition.Switches, rule => Assert.Empty(rule.Selectors));
    }

    [Fact]
    public void LearnsIndexedEventReports()
    {
        var builder = new LearnedDefinitionBuilder();
        var definition = builder.Build("Test", "Events", 1, 2,
            [[1, 0, 0, 9, 1], [2, 0, 0, 9, 1], [3, 0, 0, 9, 1]],
            [[1, 0, 0, 9, 0], [2, 0, 0, 9, 0], [3, 0, 0, 9, 0]]);
        var decoder = new LearnedReportDecoder(definition);
        var state = new bool[3];

        decoder.Decode([2, 0, 0, 9, 1], state);
        Assert.Equal([false, true, false], state);
        decoder.Decode([2, 0, 0, 9, 0], state);
        Assert.Equal([false, false, false], state);
        Assert.All(definition.Switches, rule => Assert.NotEmpty(rule.Selectors));
    }

    [Fact]
    public void RejectsIdenticalPressAndReleaseSamples()
    {
        var builder = new LearnedDefinitionBuilder();
        Assert.Throws<InvalidDataException>(() => builder.Build("Bad", "Bad", 1, 2,
            [[0, 0], [0, 2], [0, 4]],
            [[0, 0], [0, 0], [0, 0]]));
    }
}
