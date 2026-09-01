using Tippy.Core.Models;

namespace Tippy.Core.Input;

public sealed class LearnedReportDecoder : IPedalReportDecoder
{
    private readonly LearnedPedalDefinition _definition;

    public LearnedReportDecoder(LearnedPedalDefinition definition)
    {
        definition.Normalize();
        _definition = definition;
    }

    public string Name => _definition.Name;

    public bool Supports(int vendorId, int productId) =>
        vendorId == _definition.VendorId && productId == _definition.ProductId;

    public IReadOnlyList<PedalTransition> Decode(ReadOnlySpan<byte> report, Span<bool> state)
    {
        List<PedalTransition> changes = [];
        if (report.Length != _definition.ReportLength)
        {
            return changes;
        }

        foreach (var rule in _definition.Switches)
        {
            if (rule.SwitchIndex < 0 || rule.SwitchIndex >= state.Length || !Matches(report, rule.Selectors))
            {
                continue;
            }
            var pressed = rule.PressedConditions.Count > 0 && Matches(report, rule.PressedConditions);
            if (state[rule.SwitchIndex] == pressed)
            {
                continue;
            }
            state[rule.SwitchIndex] = pressed;
            changes.Add(new PedalTransition(rule.SwitchIndex, pressed));
        }
        return changes;
    }

    private static bool Matches(ReadOnlySpan<byte> report, IEnumerable<LearnedByteCondition> conditions)
    {
        foreach (var condition in conditions)
        {
            if (condition.Offset < 0 || condition.Offset >= report.Length ||
                (report[condition.Offset] & condition.Mask) != condition.Value)
            {
                return false;
            }
        }
        return true;
    }
}
