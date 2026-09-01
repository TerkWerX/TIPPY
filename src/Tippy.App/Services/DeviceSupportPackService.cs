using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tippy.App.Services;

public sealed class DeviceSupportPackManifest
{
    [JsonPropertyName("pack_id")] public string PackId { get; set; } = string.Empty;
    [JsonPropertyName("version")] public string Version { get; set; } = string.Empty;
    [JsonPropertyName("publisher_id")] public string PublisherId { get; set; } = string.Empty;
    [JsonPropertyName("signature_algorithm")] public string SignatureAlgorithm { get; set; } = "RSA-SHA256";
    [JsonPropertyName("signature")] public string Signature { get; set; } = string.Empty;
    [JsonPropertyName("files")] public List<DeviceSupportPackFile> Files { get; set; } = [];
}

public sealed class DeviceSupportPackFile
{
    [JsonPropertyName("path")] public string Path { get; set; } = string.Empty;
    [JsonPropertyName("sha256")] public string Sha256 { get; set; } = string.Empty;
}

public sealed class SupportPackPublisher
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("public_key_pem")] public string PublicKeyPem { get; set; } = string.Empty;
}

public sealed class SupportPackTrustDocument
{
    [JsonPropertyName("publishers")] public List<SupportPackPublisher> Publishers { get; set; } = [];
}

public sealed class DeviceSupportPackCatalog
{
    [JsonPropertyName("schema_version")] public int SchemaVersion { get; set; } = 1;
    [JsonPropertyName("packs")] public List<DeviceSupportPackCatalogEntry> Packs { get; set; } = [];
}

public sealed class DeviceSupportPackCatalogEntry
{
    [JsonPropertyName("pack_id")] public string PackId { get; set; } = string.Empty;
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("version")] public string Version { get; set; } = string.Empty;
    [JsonPropertyName("description")] public string Description { get; set; } = string.Empty;
    [JsonPropertyName("publisher_id")] public string PublisherId { get; set; } = string.Empty;
    [JsonPropertyName("download_url")] public string DownloadUrl { get; set; } = string.Empty;
    [JsonPropertyName("sha256")] public string Sha256 { get; set; } = string.Empty;
}

public sealed class InstalledSupportPack
{
    public string PackId { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string PublisherId { get; set; } = string.Empty;
    public string PublisherName { get; set; } = string.Empty;
    public bool PublisherAuthenticated { get; set; }
    public DateTimeOffset Installed { get; set; }
}

public sealed record DeviceSupportPackResult(string PackId, string Version, int FileCount, string Destination,
    string Publisher, bool PublisherAuthenticated);

public sealed class DeviceSupportPackService
{
    public static readonly Uri DefaultCatalogUri = new(
        "https://raw.githubusercontent.com/TerkWerX/TIPPY/main/pedal-packs/catalog.json");
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".json", ".png", ".csv" };
    private readonly string _destination;
    private readonly IReadOnlyDictionary<string, SupportPackPublisher> _publishers;
    private readonly HttpClient _httpClient;

    public DeviceSupportPackService(string? destinationOverride = null,
        IEnumerable<SupportPackPublisher>? trustedPublishers = null, HttpClient? httpClient = null)
    {
        _destination = destinationOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tippy", "PedalLibrary");
        _publishers = (trustedPublishers ?? LoadTrustedPublishers())
            .Where(publisher => !string.IsNullOrWhiteSpace(publisher.Id))
            .GroupBy(publisher => publisher.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Tippy-SupportPack/1.0");
    }

    public IReadOnlyCollection<SupportPackPublisher> TrustedPublishers => _publishers.Values.ToArray();

    public async Task<DeviceSupportPackCatalog> GetCatalogAsync(Uri? catalogUri = null, CancellationToken token = default)
    {
        catalogUri ??= DefaultCatalogUri;
        if (catalogUri.Scheme is not ("https" or "file"))
            throw new InvalidOperationException("Support-pack catalogs must use HTTPS or a local file.");
        await using var stream = catalogUri.IsFile
            ? File.OpenRead(catalogUri.LocalPath)
            : await _httpClient.GetStreamAsync(catalogUri, token).ConfigureAwait(false);
        var catalog = await JsonSerializer.DeserializeAsync<DeviceSupportPackCatalog>(stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, token).ConfigureAwait(false)
            ?? throw new InvalidDataException("The support-pack catalog is invalid.");
        catalog.Packs ??= [];
        foreach (var entry in catalog.Packs)
        {
            if (string.IsNullOrWhiteSpace(entry.PackId) || string.IsNullOrWhiteSpace(entry.Version) ||
                string.IsNullOrWhiteSpace(entry.DownloadUrl) || string.IsNullOrWhiteSpace(entry.Sha256))
                throw new InvalidDataException("A catalog entry is missing its identity, version, download URL, or checksum.");
            if (!_publishers.ContainsKey(entry.PublisherId))
                throw new InvalidDataException($"Catalog pack {entry.PackId} names an untrusted publisher: {entry.PublisherId}");
            if (!Uri.TryCreate(entry.DownloadUrl, UriKind.Absolute, out var download) || download.Scheme != "https")
                throw new InvalidDataException($"Catalog pack {entry.PackId} does not use an HTTPS download URL.");
        }
        return catalog;
    }

    public IReadOnlyList<InstalledSupportPack> GetInstalledPacks()
    {
        var folder = Path.Combine(_destination, "installed-packs");
        if (!Directory.Exists(folder)) return [];
        var result = new List<InstalledSupportPack>();
        foreach (var file in Directory.EnumerateFiles(folder, "*.json", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var item = JsonSerializer.Deserialize<InstalledSupportPack>(File.ReadAllText(file),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (item is not null) result.Add(item);
            }
            catch { }
        }
        return result.OrderBy(item => item.PackId).ToArray();
    }

    public static bool IsUpdateAvailable(string installedVersion, string availableVersion)
    {
        static Version Parse(string value)
        {
            var numeric = value.Trim().TrimStart('v', 'V').Split('-', '+')[0];
            return Version.TryParse(numeric, out var version) ? version : new Version(0, 0);
        }
        return Parse(availableVersion) > Parse(installedVersion);
    }

    public async Task<DeviceSupportPackResult> DownloadAndInstallAsync(DeviceSupportPackCatalogEntry entry,
        CancellationToken token = default)
    {
        if (!_publishers.ContainsKey(entry.PublisherId))
            throw new InvalidDataException($"Publisher is not trusted: {entry.PublisherId}");
        var uri = new Uri(entry.DownloadUrl);
        if (uri.Scheme != "https") throw new InvalidDataException("Support-pack downloads must use HTTPS.");
        var temporary = Path.Combine(Path.GetTempPath(), $"tippy-pack-{Guid.NewGuid():N}.zip");
        try
        {
            using var response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > 85 * 1024 * 1024)
                throw new InvalidDataException("The support-pack download exceeds the 85 MB safety limit.");
            await using (var output = File.Create(temporary))
                await response.Content.CopyToAsync(output, token).ConfigureAwait(false);
            await using var hashStream = File.OpenRead(temporary);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(hashStream, token).ConfigureAwait(false));
            if (!hash.Equals(CleanHash(entry.Sha256), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The downloaded support pack does not match the catalog checksum.");
            var result = await InstallAsync(temporary, requireAuthenticatedPublisher: true, token).ConfigureAwait(false);
            if (!result.PackId.Equals(entry.PackId, StringComparison.OrdinalIgnoreCase) ||
                !result.Version.Equals(entry.Version, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The downloaded pack identity does not match its catalog entry.");
            return result;
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
        }
    }

    public Task<DeviceSupportPackResult> InstallAsync(string archivePath) =>
        InstallAsync(archivePath, requireAuthenticatedPublisher: false, CancellationToken.None);

    public async Task<DeviceSupportPackResult> InstallAsync(string archivePath, bool requireAuthenticatedPublisher,
        CancellationToken token = default)
    {
        var parent = Directory.GetParent(_destination)?.FullName ?? throw new InvalidOperationException("Pedal library destination is invalid.");
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
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, token).ConfigureAwait(false) ??
                    throw new InvalidDataException("The support-pack manifest is invalid.");
            }
            ValidateManifest(manifest);
            var publisher = VerifyPublisher(manifest);
            if (requireAuthenticatedPublisher && publisher is null)
                throw new CryptographicException("This pack is not signed by a trusted Tippy publisher.");
            long totalBytes = 0;
            foreach (var file in manifest.Files)
            {
                token.ThrowIfCancellationRequested();
                var relative = file.Path.Replace('\\', '/').TrimStart('/');
                if (relative.Contains("../", StringComparison.Ordinal) || relative.Equals("..", StringComparison.Ordinal) ||
                    !AllowedExtensions.Contains(Path.GetExtension(relative)))
                    throw new InvalidDataException($"Unsafe or unsupported pack path: {file.Path}");
                var entry = archive.GetEntry(relative) ?? throw new InvalidDataException($"Pack file is missing: {relative}");
                totalBytes += entry.Length;
                if (totalBytes > 80 * 1024 * 1024) throw new InvalidDataException("The support pack exceeds the 80 MB safety limit.");
                await using var input = entry.Open();
                using var memory = new MemoryStream();
                await input.CopyToAsync(memory, token).ConfigureAwait(false);
                var bytes = memory.ToArray();
                var actualHash = Convert.ToHexString(SHA256.HashData(bytes));
                if (!actualHash.Equals(CleanHash(file.Sha256), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Checksum verification failed for {relative}.");
                var outputPath = SafePath(temporary, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                await File.WriteAllBytesAsync(outputPath, bytes, token).ConfigureAwait(false);
            }
            Directory.CreateDirectory(_destination);
            foreach (var path in Directory.EnumerateFiles(temporary, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(temporary, path);
                var output = SafePath(_destination, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                File.Copy(path, output, true);
            }
            var installed = new InstalledSupportPack
            {
                PackId = manifest.PackId, Version = manifest.Version, PublisherId = manifest.PublisherId,
                PublisherName = publisher?.Name ?? "Unsigned local pack", PublisherAuthenticated = publisher is not null,
                Installed = DateTimeOffset.Now
            };
            var records = Path.Combine(_destination, "installed-packs");
            Directory.CreateDirectory(records);
            await File.WriteAllTextAsync(SafePath(records, SafeId(manifest.PackId) + ".json"),
                JsonSerializer.Serialize(installed, new JsonSerializerOptions { WriteIndented = true }), token).ConfigureAwait(false);
            return new DeviceSupportPackResult(manifest.PackId, manifest.Version, manifest.Files.Count, _destination,
                installed.PublisherName, installed.PublisherAuthenticated);
        }
        finally
        {
            try { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); } catch { }
        }
    }

    public static byte[] GetSignaturePayload(DeviceSupportPackManifest manifest)
    {
        var builder = new StringBuilder();
        builder.Append(manifest.PackId.Trim()).Append('\n').Append(manifest.Version.Trim()).Append('\n')
            .Append(manifest.PublisherId.Trim()).Append('\n');
        foreach (var file in manifest.Files.OrderBy(file => file.Path, StringComparer.Ordinal))
            builder.Append(file.Path.Replace('\\', '/')).Append(':').Append(CleanHash(file.Sha256)).Append('\n');
        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    private SupportPackPublisher? VerifyPublisher(DeviceSupportPackManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.PublisherId) || string.IsNullOrWhiteSpace(manifest.Signature)) return null;
        if (!_publishers.TryGetValue(manifest.PublisherId, out var publisher))
            throw new CryptographicException($"The support-pack publisher is not trusted: {manifest.PublisherId}");
        if (!manifest.SignatureAlgorithm.Equals("RSA-SHA256", StringComparison.OrdinalIgnoreCase))
            throw new CryptographicException($"Unsupported pack signature algorithm: {manifest.SignatureAlgorithm}");
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(publisher.PublicKeyPem);
            if (!rsa.VerifyData(GetSignaturePayload(manifest), Convert.FromBase64String(manifest.Signature),
                    HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                throw new CryptographicException("The support-pack publisher signature is invalid.");
            return publisher;
        }
        catch (FormatException exception)
        {
            throw new CryptographicException("The support-pack signature is not valid Base64.", exception);
        }
    }

    private static IEnumerable<SupportPackPublisher> LoadTrustedPublishers()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "PedalPacks", "trusted-publishers.json"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "pedal-packs", "trusted-publishers.json"))
        };
        foreach (var path in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(path)) continue;
            try
            {
                return JsonSerializer.Deserialize<SupportPackTrustDocument>(File.ReadAllText(path),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })?.Publishers ?? [];
            }
            catch { }
        }
        return [];
    }

    private static void ValidateManifest(DeviceSupportPackManifest manifest)
    {
        manifest.Files ??= [];
        if (string.IsNullOrWhiteSpace(manifest.PackId) || string.IsNullOrWhiteSpace(manifest.Version) || manifest.Files.Count == 0)
            throw new InvalidDataException("The support-pack identity, version, or file list is missing.");
        var duplicate = manifest.Files.GroupBy(file => file.Path.Replace('\\', '/'), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null) throw new InvalidDataException($"The support pack lists a file more than once: {duplicate.Key}");
    }

    private static string CleanHash(string value) => (value ?? string.Empty).Replace("sha256:", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
    private static string SafeId(string value) => new(value.Where(character => char.IsLetterOrDigit(character) || character is '-' or '_').ToArray());

    private static string SafePath(string root, string relative)
    {
        var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Pack path escapes its destination.");
        return path;
    }
}
