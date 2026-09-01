namespace Tippy.Core.Input;

/// <summary>
/// Decoder for the VEC/Infinity 05F3:00FF family. The device sends one
/// eight-byte block per switch: index, 0, 0, 9, down, 0, 0, 0. Some HID
/// APIs prepend a report ID or combine all three blocks, so the decoder
/// deliberately accepts each representation.
/// </summary>
public sealed class InfinityReportDecoder : IPedalReportDecoder
{
    public const int VendorId = 0x05F3;
    public const int ProductId = 0x00FF;

    public string Name => "VEC / Infinity digital foot control";

    public bool Supports(int vendorId, int productId) =>
        vendorId == VendorId && productId == ProductId;

    public IReadOnlyList<PedalTransition> Decode(ReadOnlySpan<byte> report, Span<bool> state)
    {
        List<PedalTransition> changes = [];

        // Preferred report representation: scan for the fixed 00 00 09 marker.
        for (var offset = 0; offset + 4 < report.Length; offset++)
        {
            var pedalNumber = report[offset];
            if (pedalNumber is < 1 or > 3 ||
                report[offset + 1] != 0 ||
                report[offset + 2] != 0 ||
                report[offset + 3] != 9 ||
                report[offset + 4] > 1)
            {
                continue;
            }

            Apply(pedalNumber - 1, report[offset + 4] == 1, state, changes);
            offset += Math.Min(7, report.Length - offset - 1);
        }

        if (changes.Count > 0)
        {
            return changes;
        }

        // Alternate compact representations seen through some HID stacks:
        // [bit mask] or [report id, bit mask].
        var compactOffset = report.Length switch
        {
            1 => 0,
            >= 2 when report[0] == 0 && report[1] <= 0b111 => 1,
            _ => -1
        };
        if (compactOffset >= 0 && report[compactOffset] <= 0b111)
        {
            var mask = report[compactOffset];
            for (var index = 0; index < Math.Min(3, state.Length); index++)
            {
                Apply(index, (mask & (1 << index)) != 0, state, changes);
            }
        }

        return changes;
    }

    private static void Apply(
        int index,
        bool pressed,
        Span<bool> state,
        ICollection<PedalTransition> changes)
    {
        if (index < 0 || index >= state.Length || state[index] == pressed)
        {
            return;
        }

        state[index] = pressed;
        changes.Add(new PedalTransition(index, pressed));
    }
}
