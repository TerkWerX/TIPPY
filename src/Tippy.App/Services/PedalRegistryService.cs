using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Tippy.App.Models;

namespace Tippy.App.Services;

public sealed partial class PedalRegistryService
{
    private readonly string? _preferredDirectory;
    private readonly object _gate = new();
    private readonly HashSet<string> _loggedThisRun = new(StringComparer.OrdinalIgnoreCase);
    private PedalRegistryDocument _registry = new();
    private string? _libraryDirectory;

    public string? LibraryDirectory
    {
        get { lock (_gate) return _libraryDirectory; }
    }

    public string? LoadError { get; private set; }

    public PedalRegistryService(string? preferredDirectory = null) =>
        _preferredDirectory = preferredDirectory;

    public bool Reload()
    {
        foreach (var directory in CandidateDirectories())
        {
            var path = Path.Combine(directory, "pedal_registry.json");
            if (!File.Exists(path)) continue;
            try
            {
                var registry = JsonSerializer.Deserialize<PedalRegistryDocument>(File.ReadAllText(path),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new PedalRegistryDocument();
                registry.Devices ??= [];
                lock (_gate)
                {
                    _registry = registry;
                    _libraryDirectory = directory;
                    LoadError = null;
                }
                return true;
            }
            catch (Exception exception)
            {
                LoadError = $"Could not load pedal registry: {exception.Message}";
            }
        }
        LoadError = "Pedal image registry was not found.";
        return false;
    }

    public PedalRegistryMatch? Match(int vendorId, int productId, string? manufacturer, string? product)
    {
        PedalRegistryEntry[] entries;
        string? directory;
        lock (_gate)
        {
            entries = _registry.Devices.ToArray();
            directory = _libraryDirectory;
        }
        var exact = entries.Where(entry => MatchesVid(entry, vendorId) && ParsedPids(entry).Contains(productId)).ToArray();
        var selected = BestTextMatch(exact, manufacturer, product) ?? exact.FirstOrDefault();
        selected ??= BestTextMatch(entries.Where(entry =>
            (!TryParseHex(entry.Vid, out var vid) || vid == vendorId) && ParsedPids(entry).Count == 0), manufacturer, product);
        if (selected is null) return null;
        var imagePath = string.IsNullOrWhiteSpace(selected.Image) || directory is null
            ? null
            : ExistingPath(directory, selected.Image);
        return new PedalRegistryMatch(selected, imagePath);
    }

    public IReadOnlyList<PedalArtworkOption> GetArtworkOptions()
    {
        PedalRegistryEntry[] entries;
        string? directory;
        lock (_gate)
        {
            entries = _registry.Devices.ToArray();
            directory = _libraryDirectory;
        }
        List<PedalArtworkOption> options =
        [
            new("built-in:infinity-in-usb-2", "Infinity IN-USB-2", "/Tippy;component/Assets/Pedals/infinity-in-usb-2.png", "Infinity IN-USB-2", true),
            new("built-in:altoedge-in-ae-s", "AltoEdge IN-AE-S", "/Tippy;component/Assets/Pedals/altoedge-in-ae-s-scale-matched.png", "AltoEdge IN-AE-S", true)
        ];
        if (directory is null) return options;
        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries.Where(entry => !string.IsNullOrWhiteSpace(entry.Image)))
        {
            var image = entry.Image!;
            referenced.Add(image);
            var path = ExistingPath(directory, image);
            if (path is not null)
            {
                options.Add(new PedalArtworkOption($"file:{image}", entry.DisplayName, path,
                    entry.Models.FirstOrDefault() ?? entry.Brand));
            }
        }
        foreach (var path in Directory.EnumerateFiles(directory, "*.png"))
        {
            var fileName = Path.GetFileName(path);
            if (referenced.Contains(fileName)) continue;
            var label = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(
                Path.GetFileNameWithoutExtension(fileName).Replace('_', ' ').Replace('-', ' '));
            options.Add(new PedalArtworkOption($"file:{fileName}", label, path, label));
        }
        return options.DistinctBy(option => option.Key, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public PedalArtworkOption? ResolveArtwork(string artworkKey, PedalDeviceInfo device)
    {
        var options = GetArtworkOptions();
        if (!string.IsNullOrWhiteSpace(artworkKey))
        {
            var selected = options.FirstOrDefault(option =>
                option.Key.Equals(artworkKey, StringComparison.OrdinalIgnoreCase));
            if (selected is not null) return selected;
        }
        var match = Match(device.VendorId, device.ProductId, device.Manufacturer, device.DisplayName);
        if (match is null) return null;
        if (match.ImagePath is not null)
        {
            var fileName = Path.GetFileName(match.ImagePath);
            return new PedalArtworkOption($"file:{fileName}", match.Entry.DisplayName,
                match.ImagePath, match.Entry.Models.FirstOrDefault() ?? match.Entry.Brand);
        }
        return new PedalArtworkOption($"registry:{match.Entry.Id}", match.Entry.DisplayName,
            null, match.Entry.Models.FirstOrDefault() ?? match.Entry.Brand);
    }

    public bool IsAmbiguous(PedalDeviceInfo device) =>
        Match(device.VendorId, device.ProductId, device.Manufacturer, device.DisplayName)?.Entry.Ambiguous == true;

    public void AuditCandidates(IEnumerable<HidCandidateInfo> candidates)
    {
        Reload();
        foreach (var candidate in candidates)
        {
            var match = Match(candidate.VendorId, candidate.ProductId, candidate.Manufacturer, candidate.ProductName);
            var uncertain = match is not null &&
                            (match.Entry.IdConfidence.Contains("unverified", StringComparison.OrdinalIgnoreCase) ||
                             string.IsNullOrWhiteSpace(match.Entry.Pid));
            if (match is null && !candidate.LooksLikePedal || match is not null && !uncertain) continue;
            AppendUnknown(candidate, match?.Entry.Id);
        }
    }

    private void AppendUnknown(HidCandidateInfo candidate, string? registryId)
    {
        var identity = $"{candidate.VendorId:X4}:{candidate.ProductId:X4}|{candidate.Manufacturer}|{candidate.ProductName}";
        lock (_gate)
        {
            if (!_loggedThisRun.Add(identity)) return;
            var parent = _libraryDirectory is null ? null : Directory.GetParent(_libraryDirectory)?.FullName;
            var logPath = Path.Combine(parent ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tippy"), "unknown_pedals.log");
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            File.AppendAllText(logPath,
                $"{DateTimeOffset.Now:O}\tVID_{candidate.VendorId:X4}\tPID_{candidate.ProductId:X4}\t" +
                $"manufacturer={candidate.Manufacturer}\tproduct={candidate.ProductName}\tregistry={registryId ?? "unmatched"}{Environment.NewLine}");
        }
    }

    private static PedalRegistryEntry? BestTextMatch(
        IEnumerable<PedalRegistryEntry> entries,
        string? manufacturer,
        string? product)
    {
        var haystack = $"{manufacturer} {product}";
        return entries
            .Select(entry => (Entry: entry, Score: MatchScore(entry, haystack)))
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .Select(item => item.Entry)
            .FirstOrDefault();
    }

    private static int MatchScore(PedalRegistryEntry entry, string haystack)
    {
        var score = 0;
        foreach (var model in entry.Models.Where(model => model.Length >= 4))
        {
            if (haystack.Contains(model, StringComparison.OrdinalIgnoreCase)) score += 10;
        }
        foreach (var token in SignificantTokens(entry.Brand).Concat(SignificantTokens(entry.Id)))
        {
            if (haystack.Contains(token, StringComparison.OrdinalIgnoreCase)) score++;
        }
        return score;
    }

    private static IEnumerable<string> SignificantTokens(string value) =>
        WordPattern().Matches(value).Select(match => match.Value)
            .Where(token => token.Length >= 5 && !token.Equals("pedal", StringComparison.OrdinalIgnoreCase));

    private static bool MatchesVid(PedalRegistryEntry entry, int vendorId) =>
        TryParseHex(entry.Vid, out var parsed) && parsed == vendorId;

    private static HashSet<int> ParsedPids(PedalRegistryEntry entry) =>
        HexPattern().Matches(entry.Pid ?? string.Empty)
            .Select(match => int.Parse(match.Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture))
            .ToHashSet();

    private static bool TryParseHex(string? value, out int parsed) =>
        int.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out parsed);

    private static string? ExistingPath(string directory, string fileName)
    {
        var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                   Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(directory, fileName));
        return path.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(path)
            ? path
            : null;
    }

    private IEnumerable<string> CandidateDirectories()
    {
        if (!string.IsNullOrWhiteSpace(_preferredDirectory)) yield return _preferredDirectory;
        var configured = Environment.GetEnvironmentVariable("TIPPY_PEDAL_LIBRARY");
        if (!string.IsNullOrWhiteSpace(configured)) yield return configured;
        yield return Path.Combine(AppContext.BaseDirectory, "PedalLibrary");
        yield return Path.Combine(Directory.GetCurrentDirectory(), "Claude", "pedals");
        yield return @"F:\TIPPY\Claude\pedals";
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Tippy", "PedalLibrary");
    }

    [GeneratedRegex("[0-9A-Fa-f]{4}")]
    private static partial Regex HexPattern();

    [GeneratedRegex("[A-Za-z0-9]+")]
    private static partial Regex WordPattern();
}
