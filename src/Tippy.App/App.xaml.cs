using System.Diagnostics;
using System.Windows;
using Tippy.App.Services;

namespace Tippy.App;

public partial class App : Application
{
    private const int SplashDurationMilliseconds = 10_000;
    private Mutex? _singleInstance;
    private CrashRecoveryService? _crashRecovery;

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
        var profileStore = new Services.ProfileStore();
        _crashRecovery = new CrashRecoveryService(profileStore.AppDataDirectory);
        DispatcherUnhandledException += (_, args) => _crashRecovery.Log(args.Exception, "WPF dispatcher", true);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception) _crashRecovery.Log(exception, "AppDomain", true);
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            _crashRecovery.Log(args.Exception, "Unobserved task");
            args.SetObserved();
        };
        var previousCrash = _crashRecovery.BeginSession();
        if (previousCrash is not null)
        {
            var latestBackup = profileStore.GetBackups().FirstOrDefault();
            var supportReports = new SupportReportService(profileStore.AppDataDirectory);
            var recovery = new CrashRecoveryWindow(previousCrash, latestBackup, _crashRecovery, supportReports);
            if (recovery.ShowDialog() == true && recovery.Choice == CrashRecoveryChoice.RestoreLatestBackup &&
                recovery.LatestBackup is not null)
            {
                try { await profileStore.RestoreBackupAsync(recovery.LatestBackup); }
                catch (Exception exception)
                {
                    _crashRecovery.Log(exception, "Startup backup restore");
                    MessageBox.Show($"The newest backup could not be restored. Tippy will start with the current profile.\n\n{exception.Message}",
                        "Tippy recovery", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }
        var splash = new SplashWindow();
        splash.Show();
        var visibleFor = Stopwatch.StartNew();

        var forceMinimized = e.Args.Any(argument => argument.Equals("--minimized", StringComparison.OrdinalIgnoreCase));
        var mainWindow = new MainWindow(forceMinimized);
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
        _crashRecovery?.CompleteSession();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
