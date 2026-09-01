using System.Windows;
using Tippy.App.Services;
using Tippy.Core.Models;

namespace Tippy.App;

public partial class StorageToolsWindow : Window
{
    private readonly ProfileStore _store;
    private readonly AppProfile _profile;
    private string[] _backupPaths = [];

    public StorageToolsWindow(ProfileStore store, AppProfile profile)
    {
        InitializeComponent();
        _store = store;
        _profile = profile;
        LocationText.Text = $"Current storage: {_store.AppDataDirectory}";
        ModeText.Text = _store.IsPortable ? "Portable mode is active" : "Standard per-user storage is active";
        PortableButton.Content = _store.IsPortable ? "Disable portable mode" : "Enable portable mode";
        RefreshBackups();
    }

    public AppProfile? RestoredProfile { get; private set; }

    private void RefreshBackups()
    {
        _backupPaths = _store.GetBackups().ToArray();
        BackupsList.ItemsSource = _backupPaths.Select(path =>
            $"{File.GetLastWriteTime(path):g}  ·  {Path.GetFileName(path)}").ToArray();
        if (_backupPaths.Length > 0) BackupsList.SelectedIndex = 0;
    }

    private async void Backup_Click(object sender, RoutedEventArgs e)
    {
        await _store.CreateBackupAsync(_profile);
        RefreshBackups();
    }

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (BackupsList.SelectedIndex < 0) return;
        if (MessageBox.Show(this, "Restore this backup as Tippy's live profile? The current profile will be backed up first.",
                "Restore profile", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        RestoredProfile = await _store.RestoreBackupAsync(_backupPaths[BackupsList.SelectedIndex]);
        DialogResult = true;
    }

    private void Portable_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_store.IsPortable) _store.DisablePortableMode(); else _store.EnablePortableMode(_profile);
            MessageBox.Show(this, "The storage mode will change the next time Tippy starts.", "Portable mode");
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Portable mode", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
