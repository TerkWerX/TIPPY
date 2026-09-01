using Tippy.Core.Models;

namespace Tippy.Core.Input;

/// <summary>
/// Resolves the bank used for an input event. Momentary shift layers have
/// priority over an application profile, which has priority over the saved
/// active bank. Shift state is runtime-only and is never persisted.
/// </summary>
public sealed class PedalBankResolver
{
    private readonly object _gate = new();
    private readonly Dictionary<(string DeviceKey, int SwitchIndex), ShiftActivation> _shifts = new();
    private long _sequence;

    public int Resolve(PedalDeviceProfile device, ApplicationProfileRule? applicationProfile = null)
    {
        lock (_gate)
        {
            var shifted = _shifts
                .Where(pair => pair.Key.DeviceKey.Equals(device.DeviceKey, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(pair => pair.Value.Sequence)
                .Select(pair => (int?)pair.Value.BankIndex)
                .FirstOrDefault();
            if (shifted.HasValue) return shifted.Value;
        }
        return applicationProfile?.GetBankIndex(device.DeviceKey, device.ActiveBankIndex)
               ?? device.ActiveBankIndex;
    }

    public void ActivateShift(string deviceKey, int switchIndex, int bankIndex)
    {
        lock (_gate)
        {
            _shifts[(NormalizeDeviceKey(deviceKey), switchIndex)] = new ShiftActivation(
                Math.Clamp(bankIndex, 0, AppProfile.MaxBanks - 1), ++_sequence);
        }
    }

    public bool ReleaseShift(string deviceKey, int switchIndex)
    {
        lock (_gate)
        {
            return _shifts.Remove((NormalizeDeviceKey(deviceKey), switchIndex));
        }
    }

    public void ReleaseDevice(string deviceKey)
    {
        lock (_gate)
        {
            foreach (var key in _shifts.Keys
                         .Where(key => key.DeviceKey.Equals(deviceKey, StringComparison.OrdinalIgnoreCase))
                         .ToArray())
            {
                _shifts.Remove(key);
            }
        }
    }

    public void Clear()
    {
        lock (_gate) _shifts.Clear();
    }

    public bool IsShifted(string deviceKey)
    {
        lock (_gate)
        {
            return _shifts.Keys.Any(key =>
                key.DeviceKey.Equals(deviceKey, StringComparison.OrdinalIgnoreCase));
        }
    }

    private sealed record ShiftActivation(int BankIndex, long Sequence);

    private static string NormalizeDeviceKey(string deviceKey) => deviceKey.Trim().ToUpperInvariant();
}
