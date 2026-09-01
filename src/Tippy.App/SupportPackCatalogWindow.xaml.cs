using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using Tippy.App.Services;

namespace Tippy.App;

public sealed record SupportPackDisplayItem(DeviceSupportPackCatalogEntry Entry, string Title, string Description,
    string PublisherLine, string ActionLabel, bool CanInstall);

public partial class SupportPackCatalogWindow : Window
{
    private readonly DeviceSupportPackService _service = new();

    public SupportPackCatalogWindow()
    {
        InitializeComponent();
        TrustText.Text = $"{_service.TrustedPublishers.Count} trusted publisher key{(_service.TrustedPublishers.Count == 1 ? string.Empty : "s")} installed";
        Loaded += async (_, _) => await RefreshCatalogAsync();
    }

    public bool LibraryChanged { get; private set; }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshCatalogAsync();

    private async Task RefreshCatalogAsync()
    {
        StatusText.Foreground = (Brush)FindResource("MutedTextBrush");
        StatusText.Text = "Loading authenticated catalog…";
        try
        {
            var catalog = await _service.GetCatalogAsync();
            var installed = _service.GetInstalledPacks().ToDictionary(pack => pack.PackId, StringComparer.OrdinalIgnoreCase);
            var publishers = _service.TrustedPublishers.ToDictionary(publisher => publisher.Id, StringComparer.OrdinalIgnoreCase);
            var rows = catalog.Packs.Select(entry =>
            {
                installed.TryGetValue(entry.PackId, out var current);
                var update = current is not null && DeviceSupportPackService.IsUpdateAvailable(current.Version, entry.Version);
                var installedCurrent = current is not null && !update;
                var publisher = publishers.GetValueOrDefault(entry.PublisherId)?.Name ?? entry.PublisherId;
                return new SupportPackDisplayItem(entry,
                    $"{(string.IsNullOrWhiteSpace(entry.Name) ? entry.PackId : entry.Name)} · v{entry.Version}", entry.Description,
                    $"✓ Authenticated publisher: {publisher}" + (current is null ? string.Empty : $" · installed v{current.Version}"),
                    update ? "Install update" : installedCurrent ? "Up to date" : "Install", !installedCurrent);
            }).ToArray();
            PacksList.ItemsSource = rows;
            StatusText.Text = rows.Length == 0
                ? "The trusted catalog is online and valid. No community packs have been published yet."
                : $"{rows.Length} authenticated pack{(rows.Length == 1 ? string.Empty : "s")} available.";
        }
        catch (Exception exception)
        {
            StatusText.Foreground = (Brush)FindResource("DangerBrush");
            StatusText.Text = $"Catalog unavailable: {exception.Message}";
        }
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: SupportPackDisplayItem item } button) return;
        button.IsEnabled = false;
        StatusText.Text = $"Downloading and authenticating {item.Title}…";
        try
        {
            var result = await _service.DownloadAndInstallAsync(item.Entry);
            LibraryChanged = true;
            StatusText.Foreground = (Brush)FindResource("SuccessBrush");
            StatusText.Text = $"Installed {result.PackId} {result.Version} · signed by {result.Publisher} · {result.FileCount} checked files.";
            await RefreshCatalogAsync();
        }
        catch (Exception exception)
        {
            StatusText.Foreground = (Brush)FindResource("DangerBrush");
            StatusText.Text = exception.Message;
            button.IsEnabled = true;
        }
    }

    private async void InstallLocal_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Install local Tippy pedal support pack",
            Filter = "Tippy pedal packs (*.tippy-pedal-pack.zip)|*.tippy-pedal-pack.zip|ZIP archives (*.zip)|*.zip"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var result = await _service.InstallAsync(dialog.FileName);
            if (!result.PublisherAuthenticated)
            {
                MessageBox.Show(this,
                    "This local pack passed every file checksum but has no trusted publisher signature. Tippy installed it as explicitly selected local data; catalog packs never bypass publisher authentication.",
                    "Unsigned local pack", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            LibraryChanged = true;
            StatusText.Foreground = result.PublisherAuthenticated ? (Brush)FindResource("SuccessBrush") : (Brush)FindResource("PressedBrush");
            StatusText.Text = $"Installed {result.PackId} {result.Version} · {result.Publisher} · {result.FileCount} checked files.";
            await RefreshCatalogAsync();
        }
        catch (Exception exception)
        {
            StatusText.Foreground = (Brush)FindResource("DangerBrush");
            StatusText.Text = exception.Message;
        }
    }
}
