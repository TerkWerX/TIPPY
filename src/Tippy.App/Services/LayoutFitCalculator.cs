namespace Tippy.App.Services;

public static class LayoutFitCalculator
{
    public static double Overflow(double extentHeight, double viewportHeight) =>
        Math.Max(0, extentHeight - viewportHeight);

    public static double ReduceVisualHeight(
        double currentHeight,
        double overflow,
        int visibleRows,
        double minimumHeight,
        double fitPadding = 4) =>
        Math.Max(minimumHeight,
            currentHeight - overflow / Math.Max(1, visibleRows) - Math.Max(0, fitPadding));

    public static double RequiredMinimumWindowHeight(
        double actualWindowHeight,
        double extentHeight,
        double viewportHeight,
        double baseMinimumHeight,
        double fitPadding = 2)
    {
        var unusedViewport = Math.Max(0, viewportHeight - extentHeight);
        return Math.Max(baseMinimumHeight,
            Math.Ceiling(actualWindowHeight - unusedViewport + Math.Max(0, fitPadding)));
    }
}
