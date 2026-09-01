using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Tippy.App.Models;

namespace Tippy.App;

public partial class PedalArtworkPickerWindow : Window
{
    public PedalArtworkPickerWindow(
        string deviceName,
        string vidPid,
        IReadOnlyList<PedalArtworkOption> options,
        string? selectedKey,
        bool ambiguous)
    {
        InitializeComponent();
        DeviceText.Text = ambiguous
            ? $"{deviceName} · {vidPid}\nThis USB identity is shared by several models. Pick the shell/logo that matches yours; Tippy remembers it for this USB device."
            : $"{deviceName} · {vidPid}";
        ArtworkList.ItemsSource = options;
        ArtworkList.SelectedItem = options.FirstOrDefault(option =>
            option.Key.Equals(selectedKey, StringComparison.OrdinalIgnoreCase)) ?? options.FirstOrDefault();
    }

    public string? SelectedArtworkKey { get; private set; }

    private void ArtworkList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ArtworkList.SelectedItem is not PedalArtworkOption option) return;
        PreviewImage.Source = null;
        PlaceholderText.Text = string.Empty;
        if (string.IsNullOrWhiteSpace(option.ImagePath))
        {
            PlaceholderText.Text = $"Generic pedal\n{option.ModelLabel}";
            return;
        }
        try
        {
            BitmapImage bitmap;
            if (option.ImagePath.StartsWith("/", StringComparison.Ordinal))
            {
                bitmap = new BitmapImage(new Uri(option.ImagePath, UriKind.Relative));
            }
            else
            {
                bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(option.ImagePath, UriKind.Absolute);
                bitmap.EndInit();
            }
            bitmap.Freeze();
            PreviewImage.Source = bitmap;
        }
        catch
        {
            PlaceholderText.Text = $"Picture unavailable\n{option.ModelLabel}";
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (ArtworkList.SelectedItem is not PedalArtworkOption option) return;
        SelectedArtworkKey = option.Key;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
