using Tippy.Core.Input;
using Tippy.Core.Models;

namespace Tippy.Core.Tests;

public sealed class PedalBankResolverTests
{
    [Fact]
    public void ApplicationBankOverridesSavedBank()
    {
        var resolver = new PedalBankResolver();
        var device = PedalDeviceProfile.Create("pedal", "Pedal", 1, 2);
        device.ActiveBankIndex = 0;
        var application = new ApplicationProfileRule
        {
            DeviceBanks = [new ApplicationDeviceBank { DeviceKey = "pedal", BankIndex = 1 }]
        };

        Assert.Equal(1, resolver.Resolve(device, application));
    }

    [Fact]
    public void MostRecentMomentaryShiftWinsAndReleaseRestoresPreviousLayer()
    {
        var resolver = new PedalBankResolver();
        var device = PedalDeviceProfile.Create("pedal", "Pedal", 1, 2);
        var application = new ApplicationProfileRule
        {
            DeviceBanks = [new ApplicationDeviceBank { DeviceKey = "pedal", BankIndex = 1 }]
        };

        resolver.ActivateShift("pedal", 0, 2);
        resolver.ActivateShift("pedal", 1, 0);
        Assert.Equal(0, resolver.Resolve(device, application));

        Assert.True(resolver.ReleaseShift("pedal", 1));
        Assert.Equal(2, resolver.Resolve(device, application));

        Assert.True(resolver.ReleaseShift("pedal", 0));
        Assert.Equal(1, resolver.Resolve(device, application));
    }

    [Fact]
    public void DeviceReleaseClearsOnlyThatPedalsShiftState()
    {
        var resolver = new PedalBankResolver();
        var first = PedalDeviceProfile.Create("first", "First", 1, 2);
        var second = PedalDeviceProfile.Create("second", "Second", 1, 2);
        resolver.ActivateShift("first", 0, 2);
        resolver.ActivateShift("second", 0, 1);

        resolver.ReleaseDevice("first");

        Assert.Equal(0, resolver.Resolve(first));
        Assert.Equal(1, resolver.Resolve(second));
    }
}
