using System.IO;
using Tippy.Core.Models;
using Tippy.Core.Profiles;

namespace Tippy.App.Services;

public sealed class ProfileStore
{
    private readonly ProfileSerializer _serializer = new();
    private readonly SemaphoreSlim _fileGate = new(1, 1);

    public ProfileStore(string? appDataDirectoryOverride = null, string? appBaseDirectoryOverride = null)
    {
        var appBase = appBaseDirectoryOverride ?? AppContext.BaseDirectory;
        PortableMarkerPath = Path.Combine(appBase, "portable.mode");
        IsPortable = appDataDirectoryOverride is null && File.Exists(PortableMarkerPath);
        AppDataDirectory = appDataDirectoryOverride ?? (IsPortable
            ? Path.Combine(appBase, "TippyData")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tippy"));
    }

    public string AppDataDirectory { get; }
    public string PortableMarkerPath { get; }
    public bool IsPortable { get; }
    public string BackupsDirectory => Path.Combine(AppDataDirectory, "Backups");

    public string DefaultProfilePath => Path.Combine(AppDataDirectory, "default.tippy.json");

    public async Task<AppProfile> LoadDefaultAsync()
    {
        if (!File.Exists(DefaultProfilePath))
        {
            return new AppProfile();
        }
        return await LoadAsync(DefaultProfilePath).ConfigureAwait(false);
    }

    public async Task<AppProfile> LoadAsync(string path)
    {
        await _fileGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
            return _serializer.Deserialize(json);
        }
        finally
        {
            _fileGate.Release();
        }
    }

    public Task SaveDefaultAsync(AppProfile profile) => SaveAsync(DefaultProfilePath, profile);

    public async Task SaveAsync(string path, AppProfile profile)
    {
        await _fileGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }
            var json = _serializer.Serialize(profile);
            if (Path.GetFullPath(path).Equals(Path.GetFullPath(DefaultProfilePath), StringComparison.OrdinalIgnoreCase) &&
                File.Exists(path))
                CreateAutomaticBackup(path);
            var temporary = path + ".tmp";
            await File.WriteAllTextAsync(temporary, json).ConfigureAwait(false);
            File.Move(temporary, path, true);
        }
        finally
        {
            _fileGate.Release();
        }
    }

    public IReadOnlyList<string> GetBackups()
    {
        if (!Directory.Exists(BackupsDirectory)) return [];
        return Directory.EnumerateFiles(BackupsDirectory, "*.tippy.json")
            .OrderByDescending(File.GetLastWriteTimeUtc).ToArray();
    }

    public async Task<string> CreateBackupAsync(AppProfile profile, string reason = "manual")
    {
        Directory.CreateDirectory(BackupsDirectory);
        var path = Path.Combine(BackupsDirectory,
            $"default-{DateTime.Now:yyyyMMdd-HHmmss}-{SafePart(reason)}.tippy.json");
        await SaveAsync(path, profile).ConfigureAwait(false);
        PruneBackups();
        return path;
    }

    public async Task<AppProfile> RestoreBackupAsync(string backupPath)
    {
        var profile = await LoadAsync(backupPath).ConfigureAwait(false);
        if (File.Exists(DefaultProfilePath)) await CreateBackupAsync(await LoadDefaultAsync().ConfigureAwait(false), "before-restore").ConfigureAwait(false);
        await SaveDefaultAsync(profile).ConfigureAwait(false);
        return profile;
    }

    public void EnablePortableMode(AppProfile profile)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PortableMarkerPath)!);
        File.WriteAllText(PortableMarkerPath, "Tippy portable mode. Remove this file to return to LocalAppData storage.");
        var portableDirectory = Path.Combine(Path.GetDirectoryName(PortableMarkerPath)!, "TippyData");
        Directory.CreateDirectory(portableDirectory);
        File.WriteAllText(Path.Combine(portableDirectory, "default.tippy.json"), _serializer.Serialize(profile));
    }

    public void DisablePortableMode() => File.Delete(PortableMarkerPath);

    private void CreateAutomaticBackup(string profilePath)
    {
        Directory.CreateDirectory(BackupsDirectory);
        var newest = GetBackups().FirstOrDefault();
        if (newest is not null && DateTime.UtcNow - File.GetLastWriteTimeUtc(newest) < TimeSpan.FromMinutes(5)) return;
        var backupPath = Path.Combine(BackupsDirectory, $"default-{DateTime.Now:yyyyMMdd-HHmmss}-automatic.tippy.json");
        File.Copy(profilePath, backupPath, false);
        PruneBackups();
    }

    private void PruneBackups()
    {
        foreach (var path in GetBackups().Skip(20))
        {
            try { File.Delete(path); } catch { }
        }
    }

    private static string SafePart(string value) => new(value.Where(character => char.IsLetterOrDigit(character) || character == '-').ToArray());
}
