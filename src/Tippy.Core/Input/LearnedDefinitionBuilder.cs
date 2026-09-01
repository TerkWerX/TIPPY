using Tippy.Core.Models;

namespace Tippy.Core.Input;

public sealed class LearnedDefinitionBuilder
{
    public LearnedPedalDefinition Build(
        string name,
        string productName,
        int vendorId,
        int productId,
        IReadOnlyList<byte[]> pressedReports,
        IReadOnlyList<byte[]> releasedReports)
    {
        if (pressedReports.Count == 0 || pressedReports.Count != releasedReports.Count)
        {
            throw new InvalidDataException("A press and release sample is required for every switch.");
        }
        var reportLength = pressedReports[0].Length;
        if (reportLength == 0 || pressedReports.Concat(releasedReports).Any(report => report.Length != reportLength))
        {
            throw new InvalidDataException("All captured reports must have the same non-zero length.");
        }

        var definition = new LearnedPedalDefinition
        {
            Name = name,
            ProductName = productName,
            VendorId = vendorId,
            ProductId = productId,
            ReportLength = reportLength
        };

        for (var switchIndex = 0; switchIndex < pressedReports.Count; switchIndex++)
        {
            var pressed = pressedReports[switchIndex];
            var released = releasedReports[switchIndex];
            var rule = new LearnedSwitchRule { SwitchIndex = switchIndex };

            for (var offset = 0; offset < reportLength; offset++)
            {
                var stateMask = (byte)(pressed[offset] ^ released[offset]);
                if (stateMask != 0)
                {
                    rule.PressedConditions.Add(new LearnedByteCondition
                    {
                        Offset = offset,
                        Mask = stateMask,
                        Value = (byte)(pressed[offset] & stateMask)
                    });
                }
            }
            if (rule.PressedConditions.Count == 0)
            {
                throw new InvalidDataException($"Switch {switchIndex + 1} produced identical press and release reports.");
            }

            // Event-style pedals identify the switch in bytes that stay constant
            // across that switch's press/release pair but differ for other switches.
            for (var offset = 0; offset < reportLength; offset++)
            {
                if (pressed[offset] != released[offset])
                {
                    continue;
                }
                byte selectorMask = 0;
                for (var other = 0; other < pressedReports.Count; other++)
                {
                    if (other == switchIndex) continue;
                    selectorMask |= (byte)(pressed[offset] ^ pressedReports[other][offset]);
                    selectorMask |= (byte)(pressed[offset] ^ releasedReports[other][offset]);
                }
                if (selectorMask != 0)
                {
                    rule.Selectors.Add(new LearnedByteCondition
                    {
                        Offset = offset,
                        Mask = selectorMask,
                        Value = (byte)(pressed[offset] & selectorMask)
                    });
                }
            }
            definition.Switches.Add(rule);
        }
        definition.Normalize();
        return definition;
    }
}
