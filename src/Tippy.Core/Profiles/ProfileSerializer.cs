using System.Text.Json;
using System.Text.Json.Serialization;
using Tippy.Core.Models;

namespace Tippy.Core.Profiles;

public sealed class ProfileSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public string Serialize(AppProfile profile)
    {
        profile.Normalize();
        return JsonSerializer.Serialize(profile, Options);
    }

    public AppProfile Deserialize(string json)
    {
        var profile = JsonSerializer.Deserialize<AppProfile>(json, Options)
            ?? throw new InvalidDataException("The profile file is empty or invalid.");
        profile.Normalize();
        return profile;
    }
}
