namespace Tippy.Core.Input;

/// <summary>
/// Tracks keyboard keys and virtual gamepad buttons held by independent macro
/// owners. An output is released only after its final owner lets go.
/// </summary>
public sealed class HeldOutputLedger
{
    private readonly object _gate = new();
    private readonly Dictionary<string, OwnerState> _owners = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _keyCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _buttonCounts = new(StringComparer.OrdinalIgnoreCase);

    public HeldOutputDelta Acquire(
        string ownerId,
        IEnumerable<string>? keys = null,
        IEnumerable<string>? gamepadButtons = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        var normalizedKeys = NormalizeKeys(keys);
        var normalizedButtons = NormalizeButtons(gamepadButtons);
        lock (_gate)
        {
            if (!_owners.TryGetValue(ownerId, out var owner))
            {
                owner = new OwnerState();
                _owners.Add(ownerId, owner);
            }

            List<string> keyDown = [];
            foreach (var key in normalizedKeys)
            {
                Increment(owner.Keys, key);
                if (Increment(_keyCounts, key) == 1)
                {
                    keyDown.Add(key);
                }
            }

            List<string> buttonDown = [];
            foreach (var button in normalizedButtons)
            {
                Increment(owner.GamepadButtons, button);
                if (Increment(_buttonCounts, button) == 1)
                {
                    buttonDown.Add(button);
                }
            }
            return new HeldOutputDelta(keyDown, [], buttonDown, []);
        }
    }

    public HeldOutputDelta Release(
        string ownerId,
        IEnumerable<string>? keys = null,
        IEnumerable<string>? gamepadButtons = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        var normalizedKeys = NormalizeKeys(keys);
        var normalizedButtons = NormalizeButtons(gamepadButtons);
        lock (_gate)
        {
            if (!_owners.TryGetValue(ownerId, out var owner))
            {
                return HeldOutputDelta.Empty;
            }

            List<string> keyUp = [];
            foreach (var key in normalizedKeys)
            {
                if (TryDecrement(owner.Keys, key, out _) &&
                    TryDecrement(_keyCounts, key, out var releasedGlobally) && releasedGlobally)
                {
                    keyUp.Add(key);
                }
            }

            List<string> buttonUp = [];
            foreach (var button in normalizedButtons)
            {
                if (TryDecrement(owner.GamepadButtons, button, out _) &&
                    TryDecrement(_buttonCounts, button, out var releasedGlobally) && releasedGlobally)
                {
                    buttonUp.Add(button);
                }
            }

            RemoveOwnerIfEmpty(ownerId, owner);
            return new HeldOutputDelta([], keyUp, [], buttonUp);
        }
    }

    public HeldOutputDelta ReleaseOwner(string ownerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        lock (_gate)
        {
            if (!_owners.Remove(ownerId, out var owner))
            {
                return HeldOutputDelta.Empty;
            }
            return ReleaseOwnerState(owner);
        }
    }

    public HeldOutputDelta ReleaseAll()
    {
        lock (_gate)
        {
            if (_owners.Count == 0)
            {
                return HeldOutputDelta.Empty;
            }
            var delta = new HeldOutputDelta(
                [], _keyCounts.Keys.ToArray(), [], _buttonCounts.Keys.ToArray());
            _owners.Clear();
            _keyCounts.Clear();
            _buttonCounts.Clear();
            return delta;
        }
    }

    private HeldOutputDelta ReleaseOwnerState(OwnerState owner)
    {
        List<string> keyUp = [];
        foreach (var pair in owner.Keys)
        {
            for (var index = 0; index < pair.Value; index++)
            {
                if (TryDecrement(_keyCounts, pair.Key, out var releasedGlobally) && releasedGlobally)
                {
                    keyUp.Add(pair.Key);
                }
            }
        }

        List<string> buttonUp = [];
        foreach (var pair in owner.GamepadButtons)
        {
            for (var index = 0; index < pair.Value; index++)
            {
                if (TryDecrement(_buttonCounts, pair.Key, out var releasedGlobally) && releasedGlobally)
                {
                    buttonUp.Add(pair.Key);
                }
            }
        }
        return new HeldOutputDelta([], keyUp, [], buttonUp);
    }

    private void RemoveOwnerIfEmpty(string ownerId, OwnerState owner)
    {
        if (owner.Keys.Count == 0 && owner.GamepadButtons.Count == 0)
        {
            _owners.Remove(ownerId);
        }
    }

    private static string[] NormalizeKeys(IEnumerable<string>? values) =>
        values?.Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim().ToUpperInvariant() switch
            {
                "CONTROL" => "Ctrl",
                "WINDOWS" => "Win",
                "RETURN" => "Enter",
                "ESC" => "Escape",
                "MENU" => "Apps",
                _ => value.Trim()
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];

    private static string[] NormalizeButtons(IEnumerable<string>? values) =>
        values?.Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim().ToUpperInvariant() switch
            {
                "B" => "B",
                "X" => "X",
                "Y" => "Y",
                "BACK" => "Back",
                "START" => "Start",
                "GUIDE" => "Guide",
                "LB" or "LEFT SHOULDER" => "LB",
                "RB" or "RIGHT SHOULDER" => "RB",
                "L3" or "LEFT THUMB" => "L3",
                "R3" or "RIGHT THUMB" => "R3",
                "DPAD UP" => "DPad Up",
                "DPAD DOWN" => "DPad Down",
                "DPAD LEFT" => "DPad Left",
                "DPAD RIGHT" => "DPad Right",
                _ => "A"
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];

    private static int Increment(IDictionary<string, int> counts, string value)
    {
        counts.TryGetValue(value, out var count);
        counts[value] = ++count;
        return count;
    }

    private static bool TryDecrement(
        IDictionary<string, int> counts,
        string value,
        out bool releasedLast)
    {
        releasedLast = false;
        if (!counts.TryGetValue(value, out var count) || count <= 0)
        {
            return false;
        }
        if (count == 1)
        {
            counts.Remove(value);
            releasedLast = true;
            return true;
        }
        counts[value] = count - 1;
        return true;
    }

    private sealed class OwnerState
    {
        public Dictionary<string, int> Keys { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> GamepadButtons { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}

public sealed record HeldOutputDelta(
    IReadOnlyList<string> KeysDown,
    IReadOnlyList<string> KeysUp,
    IReadOnlyList<string> GamepadButtonsDown,
    IReadOnlyList<string> GamepadButtonsUp)
{
    public static HeldOutputDelta Empty { get; } = new([], [], [], []);
    public bool IsEmpty => KeysDown.Count == 0 && KeysUp.Count == 0 &&
                           GamepadButtonsDown.Count == 0 && GamepadButtonsUp.Count == 0;
}
