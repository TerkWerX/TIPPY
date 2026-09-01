namespace Tippy.Core.Models;

public sealed class SavedPedalBank
{
    public int SchemaVersion { get; set; } = 1;
    public string Name { get; set; } = "Saved bank";
    public int RequiredSwitchCount { get; set; } = 3;
    public PedalBank Bank { get; set; } = PedalBank.Create(0);

    public void Normalize()
    {
        SchemaVersion = 1;
        Name = string.IsNullOrWhiteSpace(Name) ? "Saved bank" : Name.Trim();
        RequiredSwitchCount = Math.Clamp(RequiredSwitchCount, 1, 32);
        Bank ??= PedalBank.Create(0, RequiredSwitchCount);
        Bank.Name = Name;
        Bank.EnsureSwitchCount(RequiredSwitchCount);
    }
}
