using Tippy.Core.Models;

namespace Tippy.Core.Input;

public sealed record PedalPatternInvocation(string PatternId, string Name, PedalPatternType Type, MacroDefinition Macro);

public sealed class PedalPatternEngine
{
    private readonly object _gate = new();
    private readonly Dictionary<string, long> _pressedAt = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(string TriggerId, long Timestamp)> _recentPresses = [];
    private readonly HashSet<string> _firedCombinations = new(StringComparer.OrdinalIgnoreCase);
    private List<PedalPatternDefinition> _patterns = [];

    public void Configure(IEnumerable<PedalPatternDefinition> patterns)
    {
        lock (_gate)
        {
            _patterns = patterns.Select(pattern => pattern.Clone()).ToList();
            foreach (var pattern in _patterns) pattern.Normalize();
            _pressedAt.Clear();
            _recentPresses.Clear();
            _firedCombinations.Clear();
        }
    }

    public IReadOnlyList<PedalPatternInvocation> Press(string triggerId, long timestamp)
    {
        lock (_gate)
        {
            _pressedAt[triggerId] = timestamp;
            _recentPresses.Add((triggerId, timestamp));
            var maximumWindow = _patterns.Count == 0 ? 5_000 : _patterns.Max(pattern => pattern.WindowMs);
            var cutoff = timestamp - MillisecondsToTicks(maximumWindow);
            _recentPresses.RemoveAll(item => item.Timestamp < cutoff);
            var result = new List<PedalPatternInvocation>();
            foreach (var pattern in _patterns.Where(pattern => pattern.Enabled && pattern.Triggers.Count >= 2))
            {
                var triggerIds = pattern.Triggers.Select(trigger => trigger.ToTriggerId()).ToArray();
                if (pattern.Type == PedalPatternType.Combination)
                {
                    if (_firedCombinations.Contains(pattern.Id) || triggerIds.Any(id => !_pressedAt.ContainsKey(id))) continue;
                    var times = triggerIds.Select(id => _pressedAt[id]).ToArray();
                    if (TicksToMilliseconds(times.Max() - times.Min()) > pattern.WindowMs) continue;
                    _firedCombinations.Add(pattern.Id);
                    result.Add(new PedalPatternInvocation(pattern.Id, pattern.Name, pattern.Type, pattern.Macro.Clone()));
                }
                else
                {
                    if (_recentPresses.Count < triggerIds.Length) continue;
                    var tail = _recentPresses.TakeLast(triggerIds.Length).ToArray();
                    if (!tail.Select(item => item.TriggerId).SequenceEqual(triggerIds, StringComparer.OrdinalIgnoreCase)) continue;
                    if (TicksToMilliseconds(tail[^1].Timestamp - tail[0].Timestamp) > pattern.WindowMs) continue;
                    result.Add(new PedalPatternInvocation(pattern.Id, pattern.Name, pattern.Type, pattern.Macro.Clone()));
                    _recentPresses.Clear();
                }
            }
            return result;
        }
    }

    public void Release(string triggerId)
    {
        lock (_gate)
        {
            _pressedAt.Remove(triggerId);
            foreach (var pattern in _patterns.Where(pattern => pattern.Type == PedalPatternType.Combination &&
                         pattern.Triggers.Any(trigger => trigger.ToTriggerId().Equals(triggerId, StringComparison.OrdinalIgnoreCase))))
                _firedCombinations.Remove(pattern.Id);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _pressedAt.Clear();
            _recentPresses.Clear();
            _firedCombinations.Clear();
        }
    }

    private static long MillisecondsToTicks(int milliseconds) =>
        (long)(milliseconds / 1000d * System.Diagnostics.Stopwatch.Frequency);

    private static double TicksToMilliseconds(long ticks) =>
        ticks * 1000d / System.Diagnostics.Stopwatch.Frequency;
}
