using Tippy.Core.Models;
using Tippy.Core.Profiles;

namespace Tippy.Core.Tests;

public sealed class ProfileTests
{
    [Fact]
    public void DeviceAlwaysNormalizesToThreeBanks()
    {
        var device = PedalDeviceProfile.Create("key", "Infinity", 0x05F3, 0x00FF);

        Assert.Equal(3, device.Banks.Count);
        Assert.All(device.Banks, bank => Assert.Equal(3, bank.Bindings.Count));
    }

    [Fact]
    public void DeviceBanksSupportMoreThanThreeSwitches()
    {
        var device = PedalDeviceProfile.Create("key", "Five switch", 1, 2, 5);

        Assert.Equal(5, device.SwitchCount);
        Assert.All(device.Banks, bank => Assert.Equal(5, bank.Bindings.Count));
    }

    [Fact]
    public void NextBankWraps()
    {
        var profile = new AppProfile { ActiveBankIndex = 2 };
        Assert.Equal(0, profile.NextBank());
    }

    [Fact]
    public void PedalsKeepIndependentActiveBanks()
    {
        var profile = new AppProfile();
        var left = PedalDeviceProfile.Create("left", "Left", 1, 1);
        var right = PedalDeviceProfile.Create("right", "Right", 1, 1);
        left.ActiveBankIndex = 0;
        right.ActiveBankIndex = 2;
        profile.Devices.Add(left);
        profile.Devices.Add(right);

        var serializer = new ProfileSerializer();
        var loaded = serializer.Deserialize(serializer.Serialize(profile));

        Assert.Equal(0, loaded.Devices[0].ActiveBankIndex);
        Assert.Equal(2, loaded.Devices[1].ActiveBankIndex);
    }

    [Fact]
    public void VersionTwoProfileMigratesGlobalBankToEveryPedal()
    {
        const string json = """
        {
          "SchemaVersion": 2,
          "ActiveBankIndex": 2,
          "Devices": [
            { "DeviceKey": "one", "DisplayName": "One", "SwitchCount": 3 },
            { "DeviceKey": "two", "DisplayName": "Two", "SwitchCount": 3 }
          ]
        }
        """;

        var loaded = new ProfileSerializer().Deserialize(json);

        Assert.Equal(4, loaded.SchemaVersion);
        Assert.All(loaded.Devices, device => Assert.Equal(2, device.ActiveBankIndex));
    }

    [Fact]
    public void ProfileRoundTripsEnumsAndMacros()
    {
        var profile = new AppProfile { Theme = AppTheme.Light };
        var device = PedalDeviceProfile.Create("dev", "Pedal", 1, 2);
        device.Banks[0].Bindings[0].Macro = new MacroDefinition
        {
            Name = "Copy",
            Steps = [new MacroStep { Type = MacroStepType.KeyChord, Keys = ["Ctrl", "C"] }]
        };
        profile.Devices.Add(device);

        var serializer = new ProfileSerializer();
        var loaded = serializer.Deserialize(serializer.Serialize(profile));

        Assert.Equal(AppTheme.Light, loaded.Theme);
        Assert.Equal("Copy", loaded.Devices[0].Banks[0].Bindings[0].Macro.Name);
        Assert.Equal(["Ctrl", "C"], loaded.Devices[0].Banks[0].Bindings[0].Macro.Steps[0].Keys);
    }

    [Fact]
    public void ReleaseOnceTriggerRoundTrips()
    {
        var profile = new AppProfile();
        var device = PedalDeviceProfile.Create("dev", "Pedal", 1, 2);
        device.Banks[0].Bindings[0].Macro.TriggerMode = MacroTriggerMode.ReleaseOnce;
        profile.Devices.Add(device);

        var serializer = new ProfileSerializer();
        var loaded = serializer.Deserialize(serializer.Serialize(profile));

        Assert.Equal(MacroTriggerMode.ReleaseOnce,
            loaded.Devices[0].Banks[0].Bindings[0].Macro.TriggerMode);
    }

    [Fact]
    public void ProfileRoundTripsTileAndTabbedLayoutPreferences()
    {
        var profile = new AppProfile
        {
            PedalLayout = PedalLayoutMode.Tiled,
            TileColumns = 4,
            SelectedTabbedDeviceKey = "right-pedal"
        };

        var serializer = new ProfileSerializer();
        var loaded = serializer.Deserialize(serializer.Serialize(profile));

        Assert.Equal(PedalLayoutMode.Tiled, loaded.PedalLayout);
        Assert.Equal(4, loaded.TileColumns);
        Assert.Equal("right-pedal", loaded.SelectedTabbedDeviceKey);
    }

    [Fact]
    public void SerializedProfileOmitsComputedDisplayProperties()
    {
        var profile = new AppProfile();
        profile.Devices.Add(PedalDeviceProfile.Create("dev", "Pedal", 1, 2));
        var json = new ProfileSerializer().Serialize(profile);

        Assert.DoesNotContain("\"Summary\"", json);
        Assert.DoesNotContain("\"DisplayName\": \"Pedal 1\"", json);
    }

    [Fact]
    public void LearnedDeviceRoundTripsWithProfile()
    {
        var profile = new AppProfile();
        profile.LearnedPedals.Add(new Tippy.Core.Input.LearnedDefinitionBuilder().Build(
            "Custom pedal", "USB buttons", 0x1234, 0x5678,
            [[0, 1], [0, 2], [0, 4]],
            [[0, 0], [0, 0], [0, 0]]));

        var serializer = new ProfileSerializer();
        var loaded = serializer.Deserialize(serializer.Serialize(profile));

        Assert.Equal(4, loaded.SchemaVersion);
        var learned = Assert.Single(loaded.LearnedPedals);
        Assert.Equal("Custom pedal", learned.Name);
        Assert.Equal(3, learned.Switches.Count);
    }

    [Fact]
    public void LearnedHardwareIdentityUsesDescriptorAndLegacyFallback()
    {
        var first = new LearnedPedalDefinition
        {
            VendorId = 0x1234, ProductId = 0x5678, ProductName = "Old name",
            ReportLength = 8, ReportDescriptorHash = "AABB"
        };
        var renamed = new LearnedPedalDefinition
        {
            VendorId = 0x1234, ProductId = 0x5678, ProductName = "New name",
            ReportLength = 16, ReportDescriptorHash = "aabb"
        };
        var changedDescriptor = new LearnedPedalDefinition
        {
            VendorId = 0x1234, ProductId = 0x5678, ProductName = "Old name",
            ReportLength = 8, ReportDescriptorHash = "CCDD"
        };
        var legacy = new LearnedPedalDefinition
        {
            VendorId = 0x1234, ProductId = 0x5678, ProductName = "Old name", ReportLength = 8
        };

        Assert.True(first.MatchesHardwareIdentity(renamed));
        Assert.False(first.MatchesHardwareIdentity(changedDescriptor));
        Assert.True(first.MatchesHardwareIdentity(legacy));
    }
}
