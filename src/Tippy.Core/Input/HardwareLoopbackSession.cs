namespace Tippy.Core.Input;

public sealed record HardwareLoopbackSnapshot(
    HardwareCertificationSnapshot Device,
    int OutputSamples,
    double OutputAverageMs,
    double OutputMedianMs,
    double OutputP95Ms,
    double OutputP99Ms,
    double OutputMaximumMs,
    bool SoakPassed,
    bool OutputLatencyPassed,
    bool Complete,
    string Result);

public sealed class HardwareLoopbackSession
{
    private readonly HardwareCertificationSession _device;
    private readonly List<double> _outputs = [];
    private readonly int _cyclesPerSwitch;

    public HardwareLoopbackSession(int switchCount, int cyclesPerSwitch = 10)
    {
        _cyclesPerSwitch = Math.Clamp(cyclesPerSwitch, 2, 1_000);
        _device = new HardwareCertificationSession(switchCount,
            new HardwareCertificationRequirements(_cyclesPerSwitch, 1, 5, RequireReconnect: true, RequireUnplugWhileHeld: true));
    }

    public void RecordInput(int switchIndex, bool isPressed, bool isSynthetic, double routingLatencyMs) =>
        _device.RecordInput(switchIndex, isPressed, isSynthetic, routingLatencyMs);

    public void RecordConnection(bool connected) => _device.RecordConnection(connected);

    public void RecordOutput(double pressToDispatchMs)
    {
        if (!double.IsFinite(pressToDispatchMs) || pressToDispatchMs < 0) return;
        _outputs.Add(pressToDispatchMs);
        if (_outputs.Count > 10_000) _outputs.RemoveRange(0, 1_000);
    }

    public HardwareLoopbackSnapshot Snapshot()
    {
        var device = _device.Snapshot();
        var ordered = _outputs.Order().ToArray();
        var average = ordered.Length == 0 ? 0 : ordered.Average();
        var median = Percentile(ordered, .50);
        var p95 = Percentile(ordered, .95);
        var p99 = Percentile(ordered, .99);
        var maximum = ordered.Length == 0 ? 0 : ordered[^1];
        var soak = device.Cycles.All(cycle => cycle >= _cyclesPerSwitch);
        var output = ordered.Length >= Math.Max(5, device.SwitchCount * 2) && median <= 5 && p99 <= 10;
        var complete = soak && device.SimultaneousPassed && device.Reconnected &&
                       device.UnpluggedWhileHeld && device.SyntheticReleaseObserved && device.AllReleased && output;
        return new HardwareLoopbackSnapshot(device, ordered.Length, average, median, p95, p99, maximum,
            soak, output, complete, complete ? "hardware-loop-verified" : "in-progress");
    }

    private static double Percentile(IReadOnlyList<double> ordered, double percentile)
    {
        if (ordered.Count == 0) return 0;
        var position = (ordered.Count - 1) * percentile;
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        return lower == upper ? ordered[lower] : ordered[lower] + (ordered[upper] - ordered[lower]) * (position - lower);
    }
}
