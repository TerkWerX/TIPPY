using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace Tippy.App.Services;

public sealed class UpdateService
{
    private static readonly Uri LatestReleaseApi = new("https://api.github.com/repos/TerkWerX/TIPPY/releases/latest");

    public async Task<TippyUpdateResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Tippy-Update-Check/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        using var response = await client.GetAsync(LatestReleaseApi, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? string.Empty;
        var url = root.GetProperty("html_url").GetString() ?? "https://github.com/TerkWerX/TIPPY/releases/latest";
        var title = root.TryGetProperty("name", out var name) ? name.GetString() ?? tag : tag;
        var current = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0);
        var latest = ParseVersion(tag);
        return new TippyUpdateResult(latest > current, current, latest, tag, title, url);
    }

    private static Version ParseVersion(string value)
    {
        var normalized = value.Trim().TrimStart('v', 'V');
        var prerelease = normalized.IndexOfAny(['-', '+']);
        if (prerelease >= 0) normalized = normalized[..prerelease];
        return Version.TryParse(normalized, out var version)
            ? version
            : throw new InvalidDataException($"The latest GitHub release tag '{value}' is not a version number.");
    }
}

public sealed record TippyUpdateResult(
    bool IsUpdateAvailable,
    Version CurrentVersion,
    Version LatestVersion,
    string LatestTag,
    string ReleaseTitle,
    string ReleaseUrl);
