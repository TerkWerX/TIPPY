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

        Assert.Equal(11, loaded.SchemaVersion);
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
    public void LegacyReleaseOnceTriggerMigratesToDedicatedReleaseAction()
    {
        var profile = new AppProfile();
        var device = PedalDeviceProfile.Create("dev", "Pedal", 1, 2);
        device.Banks[0].Bindings[0].Macro.Name = "Stop recording";
        device.Banks[0].Bindings[0].Macro.TriggerMode = MacroTriggerMode.ReleaseOnce;
        device.Banks[0].Bindings[0].Macro.Steps.Add(new MacroStep
        {
            Type = MacroStepType.KeyChord,
            Keys = ["Ctrl", "F10"]
        });
        profile.Devices.Add(device);

        var serializer = new ProfileSerializer();
        var loaded = serializer.Deserialize(serializer.Serialize(profile));

        var binding = loaded.Devices[0].Banks[0].Bindings[0];
        Assert.Empty(binding.Macro.Steps);
        Assert.Equal(MacroTriggerMode.ReleaseOnce, binding.ReleaseMacro.TriggerMode);
        Assert.Equal("Stop recording", binding.ReleaseMacro.Name);
        Assert.Equal(["Ctrl", "F10"], binding.ReleaseMacro.Steps[0].Keys);
    }

    [Fact]
    public void ProfileRoundTripsTileAndTabbedLayoutPreferences()
    {
        var profile = new AppProfile
        {
            PedalLayout = PedalLayoutMode.Tiled,
            TileColumns = 4,
            IsCompactMode = true,
            IsSubCompactMode = true,
            SelectedTabbedDeviceKey = "right-pedal"
        };

        var serializer = new ProfileSerializer();
        var loaded = serializer.Deserialize(serializer.Serialize(profile));

        Assert.Equal(PedalLayoutMode.Tiled, loaded.PedalLayout);
        Assert.Equal(4, loaded.TileColumns);
        Assert.False(loaded.IsCompactMode);
        Assert.True(loaded.IsSubCompactMode);
        Assert.Equal("right-pedal", loaded.SelectedTabbedDeviceKey);
    }

    [Fact]
    public void ProfileRoundTripsWindowPlacementAndMaximizedState()
    {
        var profile = new AppProfile
        {
            PedalLayout = PedalLayoutMode.Tabbed,
            WindowPlacement = new WindowPlacementSettings
            {
                HasPlacement = true,
                Left = -1420,
                Top = 84,
                Width = 1180,
                Height = 820,
                IsMaximized = true
            }
        };

        var loaded = new ProfileSerializer().Deserialize(new ProfileSerializer().Serialize(profile));

        Assert.True(loaded.WindowPlacement.HasPlacement);
        Assert.Equal(-1420, loaded.WindowPlacement.Left);
        Assert.Equal(84, loaded.WindowPlacement.Top);
        Assert.Equal(1180, loaded.WindowPlacement.Width);
        Assert.Equal(820, loaded.WindowPlacement.Height);
        Assert.True(loaded.WindowPlacement.IsMaximized);
        Assert.Equal(PedalLayoutMode.Tabbed, loaded.PedalLayout);
    }

    [Fact]
    public void ProfileRoundTripsIndependentLayoutWindowSizes()
    {
        var profile = new AppProfile
        {
            LayoutWindowSizes = new Dictionary<string, LayoutWindowSizeSettings>
            {
                ["Stacked"] = new() { HasSize = true, Width = 1120, Height = 875 },
                ["Tiled:2"] = new() { HasSize = true, Width = 1480, Height = 940 }
            }
        };

        var serializer = new ProfileSerializer();
        var loaded = serializer.Deserialize(serializer.Serialize(profile));

        Assert.Equal(1120, loaded.LayoutWindowSizes["sTaCkEd"].Width);
        Assert.Equal(875, loaded.LayoutWindowSizes["Stacked"].Height);
        Assert.Equal(1480, loaded.LayoutWindowSizes["Tiled:2"].Width);
        Assert.Equal(940, loaded.LayoutWindowSizes["Tiled:2"].Height);
    }

    [Fact]
    public void SubCompactProfileRetainsQuarterSizeWindowPlacement()
    {
        var profile = new AppProfile
        {
            IsSubCompactMode = true,
            WindowPlacement = new WindowPlacementSettings
            {
                HasPlacement = true,
                Left = 40,
                Top = 50,
                Width = 210,
                Height = 180
            }
        };

        var loaded = new ProfileSerializer().Deserialize(new ProfileSerializer().Serialize(profile));

        Assert.True(loaded.IsSubCompactMode);
        Assert.False(loaded.IsCompactMode);
        Assert.Equal(210, loaded.WindowPlacement.Width);
        Assert.Equal(180, loaded.WindowPlacement.Height);
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

        Assert.Equal(11, loaded.SchemaVersion);
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

    [Fact]
    public void ProfileRoundTripsDualEdgeProgramAndShiftAssignments()
    {
        var profile = new AppProfile();
        var device = PedalDeviceProfile.Create("dev", "Pedal", 1, 2);
        var dual = device.Banks[0].Bindings[0];
        dual.Macro.Name = "Open tool";
        dual.Macro.Steps.Add(new MacroStep
        {
            Type = MacroStepType.LaunchProgram,
            Value = @"C:\Tools\Tool.exe",
            Arguments = "--quiet",
            WorkingDirectory = @"C:\Tools"
        });
        dual.ReleaseMacro.Name = "Confirm";
        dual.ReleaseMacro.Steps.Add(new MacroStep { Type = MacroStepType.KeyChord, Keys = ["Enter"] });
        var shift = device.Banks[0].Bindings[1];
        shift.Type = PedalBindingType.ShiftLayer;
        shift.ShiftBankIndex = 2;
        profile.Devices.Add(device);

        var loaded = new ProfileSerializer().Deserialize(new ProfileSerializer().Serialize(profile));
        var loadedDual = loaded.Devices[0].Banks[0].Bindings[0];
        var loadedProgram = Assert.Single(loadedDual.Macro.Steps);

        Assert.Equal(MacroStepType.LaunchProgram, loadedProgram.Type);
        Assert.Equal("--quiet", loadedProgram.Arguments);
        Assert.Equal(@"C:\Tools", loadedProgram.WorkingDirectory);
        Assert.Equal(["Enter"], Assert.Single(loadedDual.ReleaseMacro.Steps).Keys);
        Assert.Equal(MacroTriggerMode.ReleaseOnce, loadedDual.ReleaseMacro.TriggerMode);
        Assert.Equal(PedalBindingType.ShiftLayer, loaded.Devices[0].Banks[0].Bindings[1].Type);
        Assert.Equal(2, loaded.Devices[0].Banks[0].Bindings[1].ShiftBankIndex);
    }

    [Fact]
    public void ApplicationProfilesRoundTripAndMatchByPathOrProcess()
    {
        var profile = new AppProfile();
        profile.ApplicationProfiles.Add(new ApplicationProfileRule
        {
            Name = "OBS production",
            ProcessName = "obs64.exe",
            ExecutablePath = @"C:\OBS\bin\64bit\obs64.exe",
            DeviceBanks = [new ApplicationDeviceBank { DeviceKey = "left", BankIndex = 2 }]
        });

        var loaded = new ProfileSerializer().Deserialize(new ProfileSerializer().Serialize(profile));
        var rule = Assert.Single(loaded.ApplicationProfiles);

        Assert.Equal("obs64", rule.ProcessName);
        Assert.True(rule.Matches("obs64", @"C:\Other\obs64.exe"));
        Assert.True(rule.Matches("anything", @"C:\OBS\bin\64bit\obs64.exe"));
        Assert.False(rule.Matches("blender", @"C:\Blender\blender.exe"));
        Assert.Equal(2, rule.GetBankIndex("left", 0));
        Assert.Equal(1, rule.GetBankIndex("right", 1));
    }

    [Fact]
    public void AdvancedInteractionAndSafetySettingsRoundTrip()
    {
        var profile = new AppProfile
        {
            Variables = [new TippyVariable { Name = "project", Value = "Tippy" }],
            Safety = new MacroSafetySettings
            {
                MaximumMacroSeconds = 45, MaximumRepeatSeconds = 12, MaximumSteps = 800,
                EmergencyStopHotkey = "Ctrl+Shift+Escape"
            },
            Overlay = new OverlaySettings { Enabled = true, VisibleSeconds = 5, Left = 100, Top = 200 },
            Midi = new MidiOutputSettings { PreferredOutputName = "APC MINI" },
            RawInputPedals =
            [
                new RawInputPedalDefinition
                {
                    DevicePath = @"\\?\HID#VID_1234&PID_5678",
                    DisplayName = "Keyboard pedal",
                    Switches = [new RawInputSwitchMapping { VirtualKey = 0x70, SwitchIndex = 0 }]
                }
            ],
            PedalPatterns =
            [
                new PedalPatternDefinition
                {
                    Name = "Chord",
                    Type = PedalPatternType.Combination,
                    Triggers =
                    [
                        new PedalTriggerReference { DeviceKey = "left", SwitchIndex = 0 },
                        new PedalTriggerReference { DeviceKey = "right", SwitchIndex = 2 }
                    ],
                    Macro = new MacroDefinition
                    {
                        Name = "OSC scene",
                        Steps = [new MacroStep { Type = MacroStepType.Osc, Value = "/scene", Arguments = "2", Amount = 8000 }]
                    }
                }
            ]
        };
        var device = PedalDeviceProfile.Create("left", "Left", 1, 2);
        device.Banks[0].Bindings[0].Gestures.DoubleTapMacro = new MacroDefinition
        {
            Name = "Double",
            Steps = [new MacroStep { Type = MacroStepType.Midi, Value = "note:1:60:127" }]
        };
        device.Banks[0].Bindings[0].Gestures.Toggle = true;
        profile.Devices.Add(device);

        var serializer = new ProfileSerializer();
        var loaded = serializer.Deserialize(serializer.Serialize(profile));

        Assert.Equal(11, loaded.SchemaVersion);
        Assert.Equal("Tippy", Assert.Single(loaded.Variables).Value);
        Assert.Equal(45, loaded.Safety.MaximumMacroSeconds);
        Assert.True(loaded.Overlay.Enabled);
        Assert.Equal("APC MINI", loaded.Midi.PreferredOutputName);
        Assert.Equal(0x70, Assert.Single(Assert.Single(loaded.RawInputPedals).Switches).VirtualKey);
        Assert.Equal(PedalPatternType.Combination, Assert.Single(loaded.PedalPatterns).Type);
        Assert.True(loaded.Devices[0].Banks[0].Bindings[0].Gestures.Toggle);
        Assert.Equal(MacroStepType.Midi,
            Assert.Single(loaded.Devices[0].Banks[0].Bindings[0].Gestures.DoubleTapMacro.Steps).Type);
    }
}
