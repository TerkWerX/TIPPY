namespace Tippy.Core.Input;

public sealed record HardwareCertificationRequirements(
    int CyclesPerSwitch = 2,
    double MedianLatencyTargetMs = 1.0,
    double P99LatencyTargetMs = 5.0,
    bool RequireReconnect = true,
    bool RequireUnplugWhileHeld = true);

public sealed record HardwareCertificationSnapshot(
    int SwitchCount,
    IReadOnlyList<int> Cycles,
    int MaximumSimultaneous,
    bool EverySwitchRepeated,
    bool SimultaneousPassed,
    bool Disconnected,
    bool Reconnected,
    bool UnpluggedWhileHeld,
    bool SyntheticReleaseObserved,
    bool AllReleased,
    int LatencySamples,
    double AverageLatencyMs,
    double MedianLatencyMs,
    double P95LatencyMs,
    double P99LatencyMs,
    double MaximumLatencyMs,
    bool FunctionalPassed,
    bool PerformancePassed,
    bool Certified,
    string Result);

/// <summary>Pure state machine used by both live diagnostics and the physical Hardware Passport flow.</summary>
public sealed class HardwareCertificationSession
{
    private readonly HardwareCertificationRequirements _requirements;
    private readonly int[] _cycles;
    private readonly HashSet<int> _down = [];
    private readonly List<double> _latencies = [];
    private bool _disconnected;
    private bool _reconnected;
    private bool _unpluggedWhileHeld;
    private bool _syntheticRelease;
    private int _maximumTogether;

    public HardwareCertificationSession(int switchCount, HardwareCertificationRequirements? requirements = null)
    {
        SwitchCount = Math.Clamp(switchCount, 1, 32);
        _cycles = new int[SwitchCount];
        _requirements = requirements ?? new HardwareCertificationRequirements();
    }

    public int SwitchCount { get; }

    public void RecordInput(int switchIndex, bool isPressed, bool isSynthetic, double latencyMs)
    {
        if (switchIndex < 0 || switchIndex >= SwitchCount) return;
        if (double.IsFinite(latencyMs) && latencyMs >= 0)
        {
            _latencies.Add(latencyMs);
            if (_latencies.Count > 10_000) _latencies.RemoveRange(0, 1_000);
        }
        if (isPressed)
        {
            _down.Add(switchIndex);
            _maximumTogether = Math.Max(_maximumTogether, _down.Count);
        }
        else
        {
            if (isSynthetic && _down.Contains(switchIndex))
            {
                _unpluggedWhileHeld = true;
                _syntheticRelease = true;
            }
            _down.Remove(switchIndex);
            _cycles[switchIndex]++;
            if (isSynthetic && _unpluggedWhileHeld) _syntheticRelease = true;
        }
    }

    public void RecordConnection(bool connected)
    {
        if (!connected)
        {
            _disconnected = true;
            if (_down.Count > 0) _unpluggedWhileHeld = true;
        }
        else if (_disconnected)
        {
            _reconnected = true;
        }
    }

    public HardwareCertificationSnapshot Snapshot()
    {
        var ordered = _latencies.Order().ToArray();
        var average = ordered.Length == 0 ? 0 : ordered.Average();
        var median = Percentile(ordered, .50);
        var p95 = Percentile(ordered, .95);
        var p99 = Percentile(ordered, .99);
        var maximum = ordered.Length == 0 ? 0 : ordered[^1];
        var repeated = _cycles.All(cycle => cycle >= _requirements.CyclesPerSwitch);
        var simultaneous = SwitchCount == 1 || _maximumTogether >= 2;
        var reconnect = !_requirements.RequireReconnect || _reconnected;
        var unplugRelease = !_requirements.RequireUnplugWhileHeld || (_unpluggedWhileHeld && _syntheticRelease);
        var released = _down.Count == 0;
        var functional = repeated && simultaneous && reconnect && unplugRelease && released;
        var performance = ordered.Length > 0 && median <= _requirements.MedianLatencyTargetMs && p99 <= _requirements.P99LatencyTargetMs;
        var certified = functional && performance;
        var result = certified ? "verified" : functional ? "functional-pass-performance-review" : "in-progress";
        return new HardwareCertificationSnapshot(SwitchCount, [.. _cycles], _maximumTogether, repeated,
            simultaneous, _disconnected, _reconnected, _unpluggedWhileHeld, _syntheticRelease, released,
            ordered.Length, average, median, p95, p99, maximum, functional, performance, certified, result);
    }

    private static double Percentile(IReadOnlyList<double> ordered, double percentile)
    {
        if (ordered.Count == 0) return 0;
        var position = (ordered.Count - 1) * percentile;
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        if (lower == upper) return ordered[lower];
        return ordered[lower] + (ordered[upper] - ordered[lower]) * (position - lower);
    }
}
