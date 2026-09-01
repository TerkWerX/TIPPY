namespace Tippy.App.Services;

public sealed record GamepadAnalogChange(string Axis, int Value);

/// <summary>Keeps overlapping held macros from resetting an axis still owned by another pedal.</summary>
public sealed class GamepadAnalogLedger
{
    private sealed record Entry(int Value, long Sequence);
    private readonly Dictionary<string, Dictionary<string, Entry>> _owners = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _effective = new(StringComparer.OrdinalIgnoreCase);
    private long _sequence;

    public IReadOnlyList<GamepadAnalogChange> Acquire(string owner, string axis, int value)
    {
        axis = VirtualGamepadService.NormalizeAxisName(axis);
        var before = _effective.GetValueOrDefault(axis);
        if (!_owners.TryGetValue(owner, out var axes))
            _owners[owner] = axes = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        axes[axis] = new Entry(value, ++_sequence);
        return Update(axis, before);
    }

    public IReadOnlyList<GamepadAnalogChange> Release(string owner, string axis)
    {
        axis = VirtualGamepadService.NormalizeAxisName(axis);
        var before = _effective.GetValueOrDefault(axis);
        if (_owners.TryGetValue(owner, out var axes))
        {
            axes.Remove(axis);
            if (axes.Count == 0) _owners.Remove(owner);
        }
        return Update(axis, before);
    }

    public IReadOnlyList<GamepadAnalogChange> ReleaseOwner(string owner)
    {
        if (!_owners.Remove(owner, out var axes)) return [];
        var changes = new List<GamepadAnalogChange>();
        foreach (var axis in axes.Keys) changes.AddRange(Update(axis, _effective.GetValueOrDefault(axis)));
        return changes;
    }

    public IReadOnlyList<GamepadAnalogChange> ReleaseAll()
    {
        var changes = _effective.Where(pair => pair.Value != 0)
            .Select(pair => new GamepadAnalogChange(pair.Key, 0)).ToArray();
        _owners.Clear();
        _effective.Clear();
        return changes;
    }

    private IReadOnlyList<GamepadAnalogChange> Update(string axis, int before)
    {
        var next = _owners.Values
            .Where(axes => axes.TryGetValue(axis, out _))
            .Select(axes => axes[axis])
            .OrderByDescending(entry => entry.Sequence)
            .Select(entry => entry.Value)
            .FirstOrDefault();
        if (next == 0) _effective.Remove(axis); else _effective[axis] = next;
        return before == next ? [] : [new GamepadAnalogChange(axis, next)];
    }
}
