using System.Text.Json;
using Tippy.Core.Models;

namespace Tippy.App.Services;

public static class LearnedPedalDefinitionStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static async Task SaveAsync(string path, LearnedPedalDefinition definition)
    {
        definition.Normalize();
        Validate(definition);
        var document = new LearnedPedalDefinitionDocument
        {
            ExportedAt = DateTimeOffset.Now,
            Definition = definition
        };
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(document, Options)).ConfigureAwait(false);
    }

    public static async Task<LearnedPedalDefinition> LoadAsync(string path)
    {
        var json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
        var document = JsonSerializer.Deserialize<LearnedPedalDefinitionDocument>(json, Options)
                       ?? throw new InvalidDataException("The learned-device file is empty or invalid.");
        if (document.SchemaVersion != 1)
            throw new InvalidDataException($"Learned-device schema {document.SchemaVersion} is not supported.");
        document.Definition.Normalize();
        Validate(document.Definition);
        return document.Definition;
    }

    private static void Validate(LearnedPedalDefinition definition)
    {
        if (definition.Switches.Count is < 1 or > 32)
            throw new InvalidDataException("A learned device must define between 1 and 32 switches.");
        if (definition.VendorId is < 0 or > 0xFFFF || definition.ProductId is < 0 or > 0xFFFF)
            throw new InvalidDataException("The learned device contains an invalid VID or PID.");
        if (definition.Switches.Select(rule => rule.SwitchIndex).Distinct().Count() != definition.Switches.Count ||
            definition.Switches.Any(rule => rule.SwitchIndex < 0 || rule.SwitchIndex >= definition.Switches.Count ||
                                            rule.PressedConditions.Count == 0))
            throw new InvalidDataException("The learned device contains invalid or duplicate switch rules.");
        foreach (var condition in definition.Switches.SelectMany(rule => rule.Selectors.Concat(rule.PressedConditions)))
        {
            if (condition.Offset < 0 || condition.Offset >= definition.ReportLength || condition.Mask == 0)
                throw new InvalidDataException("The learned device contains an invalid report condition.");
        }
    }

    private sealed class LearnedPedalDefinitionDocument
    {
        public int SchemaVersion { get; set; } = 1;
        public DateTimeOffset ExportedAt { get; set; }
        public LearnedPedalDefinition Definition { get; set; } = new();
    }
}
