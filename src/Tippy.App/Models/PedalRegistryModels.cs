using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tippy.App.Models;

public sealed class PedalRegistryDocument
{
    [JsonPropertyName("registry_version")]
    public string RegistryVersion { get; set; } = string.Empty;

    [JsonPropertyName("devices")]
    public List<PedalRegistryEntry> Devices { get; set; } = [];
}

public sealed class PedalRegistryEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("image")]
    public string? Image { get; set; }

    [JsonPropertyName("brand")]
    public string Brand { get; set; } = string.Empty;

    [JsonPropertyName("models")]
    public List<string> Models { get; set; } = [];

    [JsonPropertyName("pedal_count")]
    public JsonElement PedalCount { get; set; }

    [JsonPropertyName("vid")]
    public string? Vid { get; set; }

    [JsonPropertyName("pid")]
    public string? Pid { get; set; }

    [JsonPropertyName("id_confidence")]
    public string IdConfidence { get; set; } = string.Empty;

    [JsonPropertyName("ambiguous")]
    public bool Ambiguous { get; set; }

    [JsonPropertyName("notes")]
    public string Notes { get; set; } = string.Empty;

    [JsonIgnore]
    public string DisplayName => Models.Count == 0 ? Brand : $"{Brand} · {Models[0]}";

    [JsonIgnore]
    public int SwitchCount
    {
        get
        {
            if (PedalCount.ValueKind == JsonValueKind.Number && PedalCount.TryGetInt32(out var count))
                return Math.Clamp(count, 1, 32);
            if (PedalCount.ValueKind == JsonValueKind.String)
            {
                var text = PedalCount.GetString() ?? string.Empty;
                var firstNumber = System.Text.RegularExpressions.Regex.Match(text, @"\d+").Value;
                if (int.TryParse(firstNumber, out count)) return Math.Clamp(count, 1, 32);
            }
            return 3;
        }
    }
}

public sealed record PedalRegistryMatch(PedalRegistryEntry Entry, string? ImagePath);

public sealed record PedalArtworkOption(
    string Key,
    string DisplayName,
    string? ImagePath,
    string ModelLabel,
    bool IsBuiltIn = false);
