using System.Diagnostics;
using System.Reflection;
using System.Windows;

namespace Tippy.App;

public partial class AboutWindow : Window
{
    private const string SupportUri = "https://paypal.me/Terkinstein?locale.x=en_US&country.x=US";

    public AboutWindow()
    {
        InitializeComponent();
        Version version = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0);
        VersionText.Text = $"Version {version.Major}.{version.Minor}.{Math.Max(0, version.Build)}";
    }

    private void SupportTippy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = SupportUri, UseShellExecute = true });
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"Windows could not open the PayPal page.\n\n{exception.Message}",
                "Support Tippy", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
