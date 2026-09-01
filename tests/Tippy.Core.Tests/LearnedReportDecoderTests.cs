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

    [Fact]
    public void LearnsAndDecodesMoreThanThreeSwitches()
    {
        var builder = new LearnedDefinitionBuilder();
        var definition = builder.Build("Five switch", "Buttons", 1, 2,
            [[0, 1], [0, 2], [0, 4], [0, 8], [0, 16]],
            [[0, 0], [0, 0], [0, 0], [0, 0], [0, 0]]);
        var decoder = new LearnedReportDecoder(definition);
        var state = new bool[5];

        var changes = decoder.Decode([0, 25], state);

        Assert.Equal(3, changes.Count);
        Assert.Equal([true, false, false, true, true], state);
        Assert.Equal(5, definition.Switches.Count);
    }

    [Fact]
    public void RejectsDuplicateSwitchCaptures()
    {
        var builder = new LearnedDefinitionBuilder();

        var error = Assert.Throws<InvalidDataException>(() => builder.Build(
            "Duplicate", "Buttons", 1, 2,
            [[0, 1], [0, 1], [0, 4]],
            [[0, 0], [0, 0], [0, 0]]));

        Assert.Contains("uniquely identifiable", error.Message);
    }

    [Fact]
    public void RepeatedSamplesMaskVolatileBytes()
    {
        var builder = new LearnedDefinitionBuilder();
        IReadOnlyList<IReadOnlyList<byte[]>> pressed =
        [
            new byte[][] { [1, 1], [2, 1], [3, 1] },
            new byte[][] { [4, 2], [5, 2], [6, 2] }
        ];
        IReadOnlyList<IReadOnlyList<byte[]>> released =
        [
            new byte[][] { [7, 0], [8, 0], [9, 0] },
            new byte[][] { [10, 0], [11, 0], [12, 0] }
        ];

        var definition = builder.Build("Counters", "Volatile", 1, 2, pressed, released);
        var decoder = new LearnedReportDecoder(definition);
        var state = new bool[2];

        decoder.Decode([250, 3], state);

        Assert.Equal([true, true], state);
        Assert.All(definition.Switches.SelectMany(rule => rule.PressedConditions), condition => Assert.Equal(1, condition.Offset));
    }

    [Fact]
    public void SimultaneousValidationRequiresTwoDecodedSwitchesAndCleanRelease()
    {
        var builder = new LearnedDefinitionBuilder();
        var definition = builder.Build("Test", "Buttons", 1, 2,
            [[0, 1], [0, 2], [0, 4]],
            [[0, 0], [0, 0], [0, 0]]);

        builder.ValidateSimultaneousSample(definition, [0, 5], [0, 0]);
        Assert.Throws<InvalidDataException>(() => builder.ValidateSimultaneousSample(definition, [0, 1], [0, 0]));
        Assert.Throws<InvalidDataException>(() => builder.ValidateSimultaneousSample(definition, [0, 3], [0, 1]));
    }
}
