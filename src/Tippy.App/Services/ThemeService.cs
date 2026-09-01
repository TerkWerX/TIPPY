using System.Windows;
using System.Windows.Media;
using Tippy.Core.Models;

namespace Tippy.App.Services;

public static class ThemeService
{
    public static void Apply(AppTheme theme)
    {
        var colors = theme == AppTheme.Dark
            ? new Dictionary<string, string>
            {
                ["BackgroundBrush"] = "#101318", ["SurfaceBrush"] = "#181D24", ["SurfaceAltBrush"] = "#222934",
                ["TextBrush"] = "#F4F7FB", ["MutedTextBrush"] = "#9BA8B8", ["BorderBrush"] = "#303A48",
                ["AccentBrush"] = "#66E3C4", ["AccentSoftBrush"] = "#263F3E", ["PressedBrush"] = "#F2C14E",
                ["SuccessBrush"] = "#66E3C4", ["DangerBrush"] = "#FF7A90"
            }
            : new Dictionary<string, string>
            {
                ["BackgroundBrush"] = "#EEF2F5", ["SurfaceBrush"] = "#FFFFFF", ["SurfaceAltBrush"] = "#E3E9EE",
                ["TextBrush"] = "#16202A", ["MutedTextBrush"] = "#5F6D7A", ["BorderBrush"] = "#C8D1D9",
                ["AccentBrush"] = "#14A88A", ["AccentSoftBrush"] = "#D7F1EA", ["PressedBrush"] = "#F2C14E",
                ["SuccessBrush"] = "#07836B", ["DangerBrush"] = "#C73452"
            };

        foreach (var pair in colors)
        {
            Application.Current.Resources[pair.Key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(pair.Value));
        }
    }
}
