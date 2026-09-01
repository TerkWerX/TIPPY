using System.IO;
using Tippy.Core.Models;
using Tippy.Core.Profiles;

namespace Tippy.App.Services;

public sealed class ProfileStore
{
    private readonly ProfileSerializer _serializer = new();
    private readonly SemaphoreSlim _fileGate = new(1, 1);

    public string AppDataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tippy");

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
            var temporary = path + ".tmp";
            await File.WriteAllTextAsync(temporary, json).ConfigureAwait(false);
            File.Move(temporary, path, true);
        }
        finally
        {
            _fileGate.Release();
        }
    }
}
