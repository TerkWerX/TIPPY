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
        var pressedSamples = pressedReports.Select(report => (IReadOnlyList<byte[]>)new[] { report }).ToArray();
        var releasedSamples = releasedReports.Select(report => (IReadOnlyList<byte[]>)new[] { report }).ToArray();
        return Build(name, productName, vendorId, productId, pressedSamples, releasedSamples);
    }

    public LearnedPedalDefinition Build(
        string name,
        string productName,
        int vendorId,
        int productId,
        IReadOnlyList<IReadOnlyList<byte[]>> pressedSamples,
        IReadOnlyList<IReadOnlyList<byte[]>> releasedSamples)
    {
        if (pressedSamples.Count == 0 || pressedSamples.Count != releasedSamples.Count ||
            pressedSamples.Any(samples => samples.Count == 0) ||
            releasedSamples.Where((samples, index) => samples.Count != pressedSamples[index].Count).Any())
        {
            throw new InvalidDataException("Matching press and release samples are required for every switch.");
        }
        var allReports = pressedSamples.SelectMany(samples => samples)
            .Concat(releasedSamples.SelectMany(samples => samples)).ToArray();
        var reportLength = allReports[0].Length;
        if (reportLength == 0 || allReports.Any(report => report.Length != reportLength))
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

        for (var switchIndex = 0; switchIndex < pressedSamples.Count; switchIndex++)
        {
            var pressed = pressedSamples[switchIndex];
            var released = releasedSamples[switchIndex];
            var pressedReference = pressed[0];
            var releasedReference = released[0];
            var rule = new LearnedSwitchRule { SwitchIndex = switchIndex };

            for (var offset = 0; offset < reportLength; offset++)
            {
                var stablePressed = StableMask(pressed, offset);
                var stableReleased = StableMask(released, offset);
                var stateMask = stablePressed == byte.MaxValue && stableReleased == byte.MaxValue
                    ? (byte)(pressedReference[offset] ^ releasedReference[offset])
                    : (byte)0;
                if (stateMask != 0)
                {
                    rule.PressedConditions.Add(new LearnedByteCondition
                    {
                        Offset = offset,
                        Mask = stateMask,
                        Value = (byte)(pressedReference[offset] & stateMask)
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
                var ownReports = pressed.Concat(released).ToArray();
                var ownStableMask = StableMask(ownReports, offset);
                // Selector bytes identify an event-style switch. Be conservative:
                // a byte that changes at all may be a counter whose currently stable
                // high bits will roll over later, so it cannot safely identify hardware.
                if (ownStableMask != byte.MaxValue) continue;
                byte selectorMask = 0;
                for (var other = 0; other < pressedSamples.Count; other++)
                {
                    if (other == switchIndex) continue;
                    var otherReports = pressedSamples[other].Concat(releasedSamples[other]).ToArray();
                    var otherStableMask = StableMask(otherReports, offset);
                    if (otherStableMask != byte.MaxValue) continue;
                    selectorMask |= (byte)(ownStableMask & otherStableMask &
                                           (pressedReference[offset] ^ otherReports[0][offset]));
                }
                if (selectorMask != 0)
                {
                    rule.Selectors.Add(new LearnedByteCondition
                    {
                        Offset = offset,
                        Mask = selectorMask,
                        Value = (byte)(pressedReference[offset] & selectorMask)
                    });
                }
            }
            definition.Switches.Add(rule);
        }
        definition.Normalize();
        ValidateSamples(definition, pressedSamples, releasedSamples);
        return definition;
    }

    public void ValidateSimultaneousSample(
        LearnedPedalDefinition definition,
        byte[] pressedReport,
        byte[] releasedReport)
    {
        var decoder = new LearnedReportDecoder(definition);
        var state = new bool[definition.Switches.Count];
        var pressed = decoder.Decode(pressedReport, state);
        if (pressed.Count(change => change.IsPressed) < Math.Min(2, definition.Switches.Count) || state.Count(value => value) < 2)
            throw new InvalidDataException("The report did not contain at least two independently recognized switches.");
        decoder.Decode(releasedReport, state);
        if (state.Any(value => value))
            throw new InvalidDataException("One or more switches remained pressed after the simultaneous release.");
    }

    private static void ValidateSamples(
        LearnedPedalDefinition definition,
        IReadOnlyList<IReadOnlyList<byte[]>> pressedSamples,
        IReadOnlyList<IReadOnlyList<byte[]>> releasedSamples)
    {
        var decoder = new LearnedReportDecoder(definition);
        for (var switchIndex = 0; switchIndex < definition.Switches.Count; switchIndex++)
        {
            for (var sampleIndex = 0; sampleIndex < pressedSamples[switchIndex].Count; sampleIndex++)
            {
                var state = new bool[definition.Switches.Count];
                var pressedChanges = decoder.Decode(pressedSamples[switchIndex][sampleIndex], state);
                if (pressedChanges.Count != 1 || pressedChanges[0] != new PedalTransition(switchIndex, true))
                {
                    throw new InvalidDataException(
                        $"Switch {switchIndex + 1} is not uniquely identifiable. Recapture it with every other switch released.");
                }

                var releasedChanges = decoder.Decode(releasedSamples[switchIndex][sampleIndex], state);
                if (releasedChanges.Count != 1 || releasedChanges[0] != new PedalTransition(switchIndex, false) ||
                    state.Any(pressed => pressed))
                {
                    throw new InvalidDataException(
                        $"Switch {switchIndex + 1} did not produce a unique release. Recapture it carefully.");
                }
            }
        }
    }

    private static byte StableMask(IReadOnlyList<byte[]> samples, int offset)
    {
        var reference = samples[0][offset];
        byte changes = 0;
        for (var index = 1; index < samples.Count; index++)
            changes |= (byte)(reference ^ samples[index][offset]);
        return (byte)~changes;
    }
}
