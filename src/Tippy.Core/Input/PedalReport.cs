namespace Tippy.Core.Input;

public sealed record PedalTransition(int SwitchIndex, bool IsPressed);

public interface IPedalReportDecoder
{
    string Name { get; }
    bool Supports(int vendorId, int productId);
    IReadOnlyList<PedalTransition> Decode(ReadOnlySpan<byte> report, Span<bool> state);
}
