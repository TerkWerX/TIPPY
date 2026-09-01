namespace Tippy.Core.Models;

public sealed class LearnedPedalDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Learned foot control";
    public string ProductName { get; set; } = string.Empty;
    public int VendorId { get; set; }
    public int ProductId { get; set; }
    public int ReportLength { get; set; }
    public string ReportDescriptorHash { get; set; } = string.Empty;
    public List<LearnedSwitchRule> Switches { get; set; } = [];

    public void Normalize()
    {
        Id = string.IsNullOrWhiteSpace(Id) ? Guid.NewGuid().ToString("N") : Id.Trim();
        Name = string.IsNullOrWhiteSpace(Name) ? "Learned foot control" : Name.Trim();
        ProductName = ProductName?.Trim() ?? string.Empty;
        ReportLength = Math.Clamp(ReportLength, 1, 1024);
        ReportDescriptorHash = ReportDescriptorHash?.Trim().ToUpperInvariant() ?? string.Empty;
        Switches ??= [];
        foreach (var rule in Switches)
        {
            rule.Selectors ??= [];
            rule.PressedConditions ??= [];
        }
    }
}

public sealed class LearnedSwitchRule
{
    public int SwitchIndex { get; set; }
    public List<LearnedByteCondition> Selectors { get; set; } = [];
    public List<LearnedByteCondition> PressedConditions { get; set; } = [];
}

public sealed class LearnedByteCondition
{
    public int Offset { get; set; }
    public byte Mask { get; set; }
    public byte Value { get; set; }
}
