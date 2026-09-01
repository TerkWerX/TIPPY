using System.Diagnostics;
using System.Windows;

namespace Tippy.App;

public partial class App : Application
{
    private const int SplashDurationMilliseconds = 5_000;
    private Mutex? _singleInstance;

    protected override async void OnStartup(StartupEventArgs e)
    {
        _singleInstance = new Mutex(true, @"Local\Tippy.FootControlMacros", out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show("Tippy is already running.", "Tippy", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var splash = new SplashWindow();
        splash.Show();
        var visibleFor = Stopwatch.StartNew();

        var mainWindow = new MainWindow();
        int remaining = Math.Max(0, SplashDurationMilliseconds - (int)visibleFor.ElapsedMilliseconds);
        if (remaining > 0)
        {
            await Task.Delay(remaining);
        }

        MainWindow = mainWindow;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        mainWindow.Show();
        splash.Close();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
