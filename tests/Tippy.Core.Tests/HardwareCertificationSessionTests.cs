using Tippy.Core.Input;

namespace Tippy.Core.Tests;

public sealed class HardwareCertificationSessionTests
{
    [Fact]
    public void CertifiesRepeatedSimultaneousReconnectCleanupAndLatency()
    {
        var session = new HardwareCertificationSession(3);
        for (var cycle = 0; cycle < 2; cycle++)
        {
            for (var index = 0; index < 3; index++)
            {
                session.RecordInput(index, true, false, .2);
                session.RecordInput(index, false, false, .3);
            }
        }
        session.RecordInput(0, true, false, .2);
        session.RecordInput(1, true, false, .2);
        session.RecordInput(1, false, false, .2);
        session.RecordInput(0, false, true, .4);
        session.RecordConnection(false);
        session.RecordConnection(true);

        var result = session.Snapshot();

        Assert.True(result.Certified);
        Assert.True(result.SyntheticReleaseObserved);
        Assert.Equal(2, result.MaximumSimultaneous);
        Assert.True(result.P99LatencyMs < 1);
    }

    [Fact]
    public void DoesNotPretendUntestedHardwarePassed()
    {
        var result = new HardwareCertificationSession(3).Snapshot();
        Assert.False(result.FunctionalPassed);
        Assert.False(result.PerformancePassed);
        Assert.Equal("in-progress", result.Result);
    }

    [Fact]
    public void LoopbackRequiresPhysicalSoakAndEnoughOutputSamples()
    {
        var station = new HardwareLoopbackSession(1, 2);
        for (var cycle = 0; cycle < 2; cycle++)
        {
            station.RecordInput(0, true, false, .2);
            station.RecordInput(0, false, false, .2);
        }
        station.RecordInput(0, true, false, .2);
        station.RecordInput(0, false, true, .2);
        station.RecordConnection(false);
        station.RecordConnection(true);
        for (var sample = 0; sample < 5; sample++) station.RecordOutput(1.5);

        Assert.True(station.Snapshot().Complete);
    }
}
