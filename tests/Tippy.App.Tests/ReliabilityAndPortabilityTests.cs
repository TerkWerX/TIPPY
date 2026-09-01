using Tippy.App.Services;
using Tippy.Core.Input;

namespace Tippy.App.Tests;

public sealed class ReliabilityAndPortabilityTests
{
    [Fact]
    public async Task LearnedDeviceDefinitionRoundTripsAsStandaloneFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"tippy-device-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "pedal.tippy-device.json");
        try
        {
            var definition = new LearnedDefinitionBuilder().Build("Four buttons", "Test", 0x1234, 0x5678,
                [[0, 1], [0, 2], [0, 4], [0, 8]],
                [[0, 0], [0, 0], [0, 0], [0, 0]]);

            await LearnedPedalDefinitionStore.SaveAsync(path, definition);
            var loaded = await LearnedPedalDefinitionStore.LoadAsync(path);

            Assert.Equal("Four buttons", loaded.Name);
            Assert.Equal(4, loaded.Switches.Count);
            Assert.True(definition.MatchesHardwareIdentity(loaded));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void CrashRecoveryMarkerAndLogAreRecoverable()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"tippy-crash-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var first = new CrashRecoveryService(directory);
            Assert.Null(first.BeginSession());
            first.Log(new InvalidOperationException("test crash"), "test");

            var second = new CrashRecoveryService(directory);
            Assert.NotNull(second.BeginSession());
            Assert.Contains("test crash", File.ReadAllText(second.CrashLogPath));
            second.CompleteSession();

            var third = new CrashRecoveryService(directory);
            Assert.Null(third.BeginSession());
            third.CompleteSession();
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
