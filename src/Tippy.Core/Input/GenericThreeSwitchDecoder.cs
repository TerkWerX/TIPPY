namespace Tippy.Core.Input;

/// <summary>
/// Conservative fallback for pedals that expose a one-byte, three-bit button
/// mask. New device-specific decoders can be registered ahead of this one.
/// </summary>
public sealed class GenericThreeSwitchDecoder : IPedalReportDecoder
{
    public string Name => "Generic three-switch HID";
    public bool Supports(int vendorId, int productId) => true;

    public IReadOnlyList<PedalTransition> Decode(ReadOnlySpan<byte> report, Span<bool> state)
    {
        List<PedalTransition> changes = [];
        if (report.IsEmpty)
        {
            return changes;
        }

        var value = report.Length > 1 && report[0] == 0 ? report[1] : report[0];
        if ((value & ~0b111) != 0)
        {
            return changes;
        }

        for (var index = 0; index < Math.Min(3, state.Length); index++)
        {
            var pressed = (value & (1 << index)) != 0;
            if (state[index] == pressed)
            {
                continue;
            }
            state[index] = pressed;
            changes.Add(new PedalTransition(index, pressed));
        }

        return changes;
    }
}
