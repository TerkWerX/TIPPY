namespace Tippy.Core.Models;

public sealed class PedalBank
{
    public string Name { get; set; } = "Bank";
    public List<PedalBinding> Bindings { get; set; } = [];

    public static PedalBank Create(int bankIndex, int switchCount = 3) => new()
    {
        Name = $"Bank {bankIndex + 1}",
        Bindings = Enumerable.Range(0, switchCount).Select(PedalBinding.Empty).ToList()
    };

    public void EnsureSwitchCount(int switchCount)
    {
        Bindings ??= [];
        while (Bindings.Count < switchCount)
        {
            Bindings.Add(PedalBinding.Empty(Bindings.Count));
        }

        if (Bindings.Count > switchCount)
        {
            Bindings.RemoveRange(switchCount, Bindings.Count - switchCount);
        }
        foreach (var binding in Bindings)
        {
            binding.Normalize();
        }
    }

    public PedalBank Clone() => new()
    {
        Name = Name,
        Bindings = Bindings.Select(binding => binding.Clone()).ToList()
    };
}
