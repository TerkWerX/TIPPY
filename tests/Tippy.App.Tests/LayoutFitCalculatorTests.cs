using Tippy.App.Services;

namespace Tippy.App.Tests;

public sealed class LayoutFitCalculatorTests
{
    [Fact]
    public void OverflowReportsOnlyContentBeyondViewport()
    {
        Assert.Equal(169, LayoutFitCalculator.Overflow(892, 723));
        Assert.Equal(0, LayoutFitCalculator.Overflow(620, 723));
    }

    [Fact]
    public void VisualReductionSharesOverflowAcrossVisibleRows()
    {
        var fitted = LayoutFitCalculator.ReduceVisualHeight(440, 169, 2, 48, 6);

        Assert.Equal(349.5, fitted);
    }

    [Fact]
    public void VisualReductionHonorsReadableArtworkFloor()
    {
        var fitted = LayoutFitCalculator.ReduceVisualHeight(90, 500, 4, 48, 6);

        Assert.Equal(48, fitted);
    }

    [Fact]
    public void MinimumHeightUsesUnusedViewportWithoutDroppingBelowBase()
    {
        Assert.Equal(1035,
            LayoutFitCalculator.RequiredMinimumWindowHeight(1050, 778, 793, 650, 0));
        Assert.Equal(650,
            LayoutFitCalculator.RequiredMinimumWindowHeight(1050, 300, 793, 650, 0));
    }
}
