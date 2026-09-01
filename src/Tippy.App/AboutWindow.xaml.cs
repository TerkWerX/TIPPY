using System.Reflection;
using System.Windows;

namespace Tippy.App;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        Version version = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0);
        VersionText.Text = $"Version {version.Major}.{version.Minor}.{Math.Max(0, version.Build)}";
    }
}
