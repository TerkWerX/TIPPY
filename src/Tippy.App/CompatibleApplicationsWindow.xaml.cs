using System.Windows;
using Tippy.App.Services;
using Tippy.Core.Models;

namespace Tippy.App;

public sealed class CompatibleApplicationChoice
{
    public required CompatibleApplicationMatch Match { get; init; }
    public bool IsSelected { get; set; }
    public bool CanAdd { get; init; }
    public string ApplicationName => Match.CatalogProfile.Name;
    public string FoundAs => Match.DisplayName;
    public string Evidence => Match.Evidence;
    public int ShortcutCount => Match.ShortcutCount;
    public string Status => CanAdd ? "Ready to add" : "Already configured";
    public string SelectionLabel => $"Add {ApplicationName}";
}

public partial class CompatibleApplicationsWindow : Window
{
    private readonly InstalledApplicationScanner _scanner = new();
    private readonly IReadOnlyList<ApplicationProfileRule> _existingProfiles;
    private List<CompatibleApplicationChoice> _choices = [];

    public CompatibleApplicationsWindow(IReadOnlyList<ApplicationProfileRule> existingProfiles)
    {
        InitializeComponent();
        _existingProfiles = existingProfiles;
        Loaded += (_, _) => Scan();
    }

    public IReadOnlyList<CompatibleApplicationMatch> Result { get; private set; } = [];

    private void Scan()
    {
        try
        {
            StatusText.Text = "Scanning locally…";
            var matches = _scanner.Scan();
            _choices = matches.Select(match => new CompatibleApplicationChoice
            {
                Match = match,
                CanAdd = !IsConfigured(match),
                IsSelected = !IsConfigured(match)
            }).ToList();
            ApplicationsGrid.ItemsSource = _choices;
            var newCount = _choices.Count(choice => choice.CanAdd);
            StatusText.Text = _choices.Count == 0
                ? "No applications from Tippy's current shortcut catalog were found."
                : $"Found {_choices.Count} compatible application{Plural(_choices.Count)} · {newCount} available to add";
        }
        catch (Exception exception)
        {
            _choices = [];
            ApplicationsGrid.ItemsSource = _choices;
            StatusText.Text = $"The local scan could not finish: {exception.Message}";
        }
    }

    private bool IsConfigured(CompatibleApplicationMatch match) => _existingProfiles.Any(profile =>
        !string.IsNullOrWhiteSpace(profile.ProcessName) &&
        profile.ProcessName.Equals(match.ProcessName, StringComparison.OrdinalIgnoreCase) ||
        !string.IsNullOrWhiteSpace(profile.ExecutablePath) && !string.IsNullOrWhiteSpace(match.ExecutablePath) &&
        profile.ExecutablePath.Equals(match.ExecutablePath, StringComparison.OrdinalIgnoreCase));

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var choice in _choices) choice.IsSelected = choice.CanAdd;
        ApplicationsGrid.Items.Refresh();
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        foreach (var choice in _choices) choice.IsSelected = false;
        ApplicationsGrid.Items.Refresh();
    }

    private void ScanAgain_Click(object sender, RoutedEventArgs e) => Scan();

    private void AddSelected_Click(object sender, RoutedEventArgs e)
    {
        Result = _choices.Where(choice => choice.CanAdd && choice.IsSelected).Select(choice => choice.Match).ToArray();
        if (Result.Count == 0)
        {
            StatusText.Text = "Select at least one new application to add.";
            return;
        }
        DialogResult = true;
    }

    private static string Plural(int count) => count == 1 ? string.Empty : "s";
}
