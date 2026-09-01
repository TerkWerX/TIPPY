using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tippy.Core.Models;

namespace Tippy.App.Services;

public static class PedalBankStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task SaveAsync(string path, SavedPedalBank bank)
    {
        bank.Normalize();
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(bank, Options));
    }

    public static async Task<SavedPedalBank> LoadAsync(string path)
    {
        var bank = JsonSerializer.Deserialize<SavedPedalBank>(await File.ReadAllTextAsync(path), Options)
            ?? throw new InvalidDataException("The bank file is empty or invalid.");
        bank.Normalize();
        return bank;
    }
}
