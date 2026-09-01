using Tippy.Core.Input;

namespace Tippy.Core.Tests;

public sealed class InfinityReportDecoderTests
{
    private readonly InfinityReportDecoder _decoder = new();

    [Fact]
    public void DecodesSingleEightByteReport()
    {
        var state = new bool[3];
        var changes = _decoder.Decode([2, 0, 0, 9, 1, 0, 0, 0], state);

        var change = Assert.Single(changes);
        Assert.Equal(1, change.SwitchIndex);
        Assert.True(change.IsPressed);
        Assert.True(state[1]);
    }

    [Fact]
    public void HandlesReportIdPrefixAndRelease()
    {
        var state = new[] { false, false, true };
        var changes = _decoder.Decode([0, 3, 0, 0, 9, 0, 0, 0, 0], state);

        var change = Assert.Single(changes);
        Assert.Equal(2, change.SwitchIndex);
        Assert.False(change.IsPressed);
    }

    [Fact]
    public void DecodesCombinedBlocks()
    {
        var state = new bool[3];
        byte[] report =
        [
            1, 0, 0, 9, 1, 0, 0, 0,
            2, 0, 0, 9, 0, 0, 0, 0,
            3, 0, 0, 9, 1, 0, 0, 0
        ];

        var changes = _decoder.Decode(report, state);

        Assert.Equal(2, changes.Count);
        Assert.True(state[0]);
        Assert.False(state[1]);
        Assert.True(state[2]);
    }

    [Fact]
    public void IgnoresDuplicateState()
    {
        var state = new[] { true, false, false };
        var changes = _decoder.Decode([1, 0, 0, 9, 1, 0, 0, 0], state);
        Assert.Empty(changes);
    }

    [Fact]
    public void DecodesWindowsThreeByteButtonMask()
    {
        var state = new bool[3];
        var changes = _decoder.Decode([0, 0b101, 0], state);

        Assert.Equal(2, changes.Count);
        Assert.True(state[0]);
        Assert.False(state[1]);
        Assert.True(state[2]);
    }
}
