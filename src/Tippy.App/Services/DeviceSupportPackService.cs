using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tippy.App.Services;

public sealed class DeviceSupportPackManifest
{
    [JsonPropertyName("pack_id")] public string PackId { get; set; } = string.Empty;
    [JsonPropertyName("version")] public string Version { get; set; } = string.Empty;
    [JsonPropertyName("files")] public List<DeviceSupportPackFile> Files { get; set; } = [];
}

public sealed class DeviceSupportPackFile
{
    [JsonPropertyName("path")] public string Path { get; set; } = string.Empty;
    [JsonPropertyName("sha256")] public string Sha256 { get; set; } = string.Empty;
}

public sealed record DeviceSupportPackResult(string PackId, string Version, int FileCount, string Destination);

public sealed class DeviceSupportPackService
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".json", ".png", ".csv" };
    private readonly string? _destinationOverride;

    public DeviceSupportPackService(string? destinationOverride = null) =>
        _destinationOverride = destinationOverride;

    public async Task<DeviceSupportPackResult> InstallAsync(string archivePath)
    {
        var destination = _destinationOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tippy", "PedalLibrary");
        var parent = Directory.GetParent(destination)!.FullName;
        Directory.CreateDirectory(parent);
        var temporary = Path.Combine(parent, $"PedalLibrary-install-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporary);
        try
        {
            using var archive = ZipFile.OpenRead(archivePath);
            var manifestEntry = archive.GetEntry("pack-manifest.json") ??
                throw new InvalidDataException("This archive has no pack-manifest.json file.");
            DeviceSupportPackManifest manifest;
            await using (var stream = manifestEntry.Open())
            {
                manifest = await JsonSerializer.DeserializeAsync<DeviceSupportPackManifest>(stream,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ??
                    throw new InvalidDataException("The support-pack manifest is invalid.");
            }
            if (string.IsNullOrWhiteSpace(manifest.PackId) || string.IsNullOrWhiteSpace(manifest.Version) || manifest.Files.Count == 0)
                throw new InvalidDataException("The support-pack identity, version, or file list is missing.");
            long totalBytes = 0;
            foreach (var file in manifest.Files)
            {
                var relative = file.Path.Replace('\\', '/').TrimStart('/');
                if (relative.Contains("../", StringComparison.Ordinal) || relative.Equals("..", StringComparison.Ordinal) ||
                    !AllowedExtensions.Contains(Path.GetExtension(relative)))
                    throw new InvalidDataException($"Unsafe or unsupported pack path: {file.Path}");
                var entry = archive.GetEntry(relative) ?? throw new InvalidDataException($"Pack file is missing: {relative}");
                totalBytes += entry.Length;
                if (totalBytes > 80 * 1024 * 1024) throw new InvalidDataException("The support pack exceeds the 80 MB safety limit.");
                await using var input = entry.Open();
                using var memory = new MemoryStream();
                await input.CopyToAsync(memory).ConfigureAwait(false);
                var actualHash = Convert.ToHexString(SHA256.HashData(memory.ToArray()));
                if (!actualHash.Equals(file.Sha256.Replace("sha256:", string.Empty), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Checksum verification failed for {relative}.");
                var outputPath = SafePath(temporary, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                await File.WriteAllBytesAsync(outputPath, memory.ToArray()).ConfigureAwait(false);
            }
            Directory.CreateDirectory(destination);
            foreach (var path in Directory.EnumerateFiles(temporary, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(temporary, path);
                var output = SafePath(destination, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                File.Copy(path, output, true);
            }
            await File.WriteAllTextAsync(Path.Combine(destination, "installed-pack.json"),
                JsonSerializer.Serialize(new { manifest.PackId, manifest.Version, Installed = DateTimeOffset.Now },
                    new JsonSerializerOptions { WriteIndented = true })).ConfigureAwait(false);
            return new DeviceSupportPackResult(manifest.PackId, manifest.Version, manifest.Files.Count, destination);
        }
        finally
        {
            if (Directory.Exists(temporary)) Directory.Delete(temporary, true);
        }
    }

    private static string SafePath(string root, string relative)
    {
        var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Pack path escapes its destination.");
        return path;
    }
}
