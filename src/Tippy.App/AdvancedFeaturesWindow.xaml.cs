using System.Windows;
using System.Windows.Controls;

namespace Tippy.App;

public sealed record AdvancedFeatureItem(string Group, string Title, string Description, Action Open)
{
    public string SearchText => $"{Group} {Title} {Description}";
}

public partial class AdvancedFeaturesWindow : Window
{
    private readonly IReadOnlyList<AdvancedFeatureItem> _features = [];

    public AdvancedFeaturesWindow(IEnumerable<AdvancedFeatureItem> features)
    {
        InitializeComponent();
        _features = features.OrderBy(feature => feature.Group).ThenBy(feature => feature.Title).ToArray();
        Refresh();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => Refresh();

    private void Refresh()
    {
        var query = SearchBox?.Text.Trim() ?? string.Empty;
        var visible = string.IsNullOrWhiteSpace(query)
            ? _features
            : _features.Where(feature => feature.SearchText.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        FeaturesList.ItemsSource = visible;
        CountText.Text = $"{visible.Count} feature{(visible.Count == 1 ? string.Empty : "s")}";
    }

    private void Feature_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: AdvancedFeatureItem feature }) return;
        Close();
        Dispatcher.BeginInvoke(feature.Open);
    }
}
