using System.Reflection;
using System.Security.Principal;
using Tippy.App.Models;
using Tippy.Core.Models;

namespace Tippy.App.Services;

public enum TippyDoctorStatus
{
    Pass,
    Warning,
    Fail,
    Info
}

public sealed record TippyDoctorCheck(
    string Category,
    string Name,
    TippyDoctorStatus Status,
    string Summary,
    string Details = "")
{
    public string Symbol => Status switch
    {
        TippyDoctorStatus.Pass => "✓",
        TippyDoctorStatus.Warning => "!",
        TippyDoctorStatus.Fail => "×",
        _ => "i"
    };
}

public sealed record TippyDoctorReport(
    DateTimeOffset Generated,
    string AppVersion,
    string OperatingSystem,
    bool PortableMode,
    int ConnectedPedals,
    IReadOnlyList<TippyDoctorCheck> Checks)
{
    public int Passed => Checks.Count(check => check.Status == TippyDoctorStatus.Pass);
    public int Warnings => Checks.Count(check => check.Status == TippyDoctorStatus.Warning);
    public int Failed => Checks.Count(check => check.Status == TippyDoctorStatus.Fail);
    public int Informational => Checks.Count(check => check.Status == TippyDoctorStatus.Info);
    public string Overall => Failed > 0 ? "Action required" : Warnings > 0 ? "Ready with notes" : "Ready";
}

public sealed class TippyDoctorContext
{
    public required ProfileStore ProfileStore { get; init; }
    public required AppProfile Profile { get; init; }
    public required PedalRegistryService PedalRegistry { get; init; }
    public IReadOnlyCollection<PedalDeviceInfo> ConnectedDevices { get; init; } = [];
    public bool HidListening { get; init; }
    public bool BankHotkeyRegistered { get; init; }
    public bool EmergencyHotkeyRegistered { get; init; }
    public string? StartupProfileError { get; init; }
    public Func<bool>? StartupRegistrationProbe { get; init; }
    public Func<(bool Available, string Status)>? GamepadProbe { get; init; }
}

public sealed class TippyDoctorService
{
    public TippyDoctorReport Run(TippyDoctorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        List<TippyDoctorCheck> checks = [];

        checks.Add(OperatingSystem.IsWindows()
            ? Pass("SYSTEM", "Windows input host", "Windows input services are available.",
                $"{Environment.OSVersion.VersionString} · {(Environment.Is64BitProcess ? "64-bit" : "32-bit")} process")
            : Fail("SYSTEM", "Windows input host", "Tippy requires Windows for HID, SendInput, MIDI, and tray integration."));
        checks.Add(Environment.Is64BitProcess
            ? Pass("SYSTEM", "64-bit runtime", "Tippy is running as a 64-bit process.")
            : Warning("SYSTEM", "64-bit runtime", "This process is not 64-bit; use the Windows x64 Tippy build."));

        AddGuarded(checks, "STORAGE", "Profile storage", () =>
        {
            ProbeWritableDirectory(context.ProfileStore.AppDataDirectory);
            return Pass("STORAGE", "Profile storage", "Tippy can safely write its profile and recovery data.",
                context.ProfileStore.IsPortable ? "Portable TippyData storage" : "Standard per-user storage");
        });

        if (!string.IsNullOrWhiteSpace(context.StartupProfileError))
        {
            checks.Add(Fail("STORAGE", "Live profile", "The saved profile could not be loaded.", context.StartupProfileError));
        }
        else
        {
            AddGuarded(checks, "STORAGE", "Live profile", () =>
            {
                if (!File.Exists(context.ProfileStore.DefaultProfilePath))
                    return Warning("STORAGE", "Live profile", "The live profile has not been written yet; Tippy will create it during autosave.");
                var loaded = context.ProfileStore.LoadDefaultAsync().GetAwaiter().GetResult();
                return Pass("STORAGE", "Live profile", "The saved profile is readable and normalized.",
                    $"Schema {loaded.SchemaVersion} · {loaded.Devices.Count} configured pedal{Plural(loaded.Devices.Count)}");
            });
        }

        AddGuarded(checks, "STORAGE", "Automatic backups", () =>
        {
            var count = context.ProfileStore.GetBackups().Count;
            return count > 0
                ? Pass("STORAGE", "Automatic backups", $"{count} recoverable profile backup{Plural(count)} found.")
                : Warning("STORAGE", "Automatic backups", "No profile backup exists yet. Tippy creates them as the profile changes.");
        });

        AddGuarded(checks, "DEVICES", "Pedal artwork registry", () =>
        {
            var loaded = context.PedalRegistry.Reload();
            var options = loaded ? context.PedalRegistry.GetArtworkOptions().Count : 0;
            return loaded
                ? Pass("DEVICES", "Pedal artwork registry", "The data-driven pedal registry loaded successfully.",
                    $"{options} selectable artwork option{Plural(options)}")
                : Warning("DEVICES", "Pedal artwork registry", context.PedalRegistry.LoadError ?? "The registry could not be loaded.");
        });

        checks.Add(context.HidListening
            ? Pass("DEVICES", "USB HID listener", "The event-driven pedal listener is active.")
            : Fail("DEVICES", "USB HID listener", "The pedal listener is not active."));
        if (context.ConnectedDevices.Count == 0)
        {
            checks.Add(Warning("DEVICES", "Connected pedals", "No supported or learned pedal is connected right now.",
                "Connect a pedal and rerun Tippy Doctor for a live device check."));
        }
        else
        {
            var details = string.Join(" · ", context.ConnectedDevices.Take(8).Select(device =>
                $"{device.DisplayName} [{device.VendorId:X4}:{device.ProductId:X4}, {device.SwitchCount} switch{Plural(device.SwitchCount)}, {device.DecoderName}]"));
            checks.Add(Pass("DEVICES", "Connected pedals",
                $"{context.ConnectedDevices.Count} pedal{Plural(context.ConnectedDevices.Count)} connected and readable.", details));
        }

        checks.Add(context.EmergencyHotkeyRegistered
            ? Pass("SAFETY", "Emergency stop", $"{context.Profile.Safety.EmergencyStopHotkey} is registered and ready.")
            : Fail("SAFETY", "Emergency stop", $"{context.Profile.Safety.EmergencyStopHotkey} could not be registered.",
                "Choose a different emergency shortcut in Settings, then rerun Tippy Doctor."));
        checks.Add(context.BankHotkeyRegistered
            ? Pass("SAFETY", "Bank hotkey", $"{context.Profile.BankHotkey} is registered and ready.")
            : Warning("SAFETY", "Bank hotkey", $"{context.Profile.BankHotkey} could not be registered.",
                "Another program may already own this shortcut."));
        checks.Add(Pass("SAFETY", "Macro safety limits", "Runaway-output limits are enabled.",
            $"{context.Profile.Safety.MaximumMacroSeconds}s macro · {context.Profile.Safety.MaximumRepeatSeconds}s repeat · {context.Profile.Safety.MaximumSteps} steps"));

        var elevated = IsElevated();
        checks.Add(OperatingSystem.IsWindows()
            ? Pass("OUTPUTS", "Keyboard and mouse output", "The Windows SendInput backend is available.",
                elevated ? "Tippy is elevated; output can reach normal and elevated applications." :
                    "Normal integrity level; Windows may block output into applications running as administrator.")
            : Fail("OUTPUTS", "Keyboard and mouse output", "Windows SendInput is unavailable on this operating system."));

        var usesGamepad = UsesAny(context.Profile, MacroStepType.GamepadButton, MacroStepType.GamepadAxis);
        AddGuarded(checks, "OUTPUTS", "Virtual Xbox controller", () =>
        {
            if (context.GamepadProbe is null)
                return Info("OUTPUTS", "Virtual Xbox controller", "Gamepad output was not probed.");
            var result = context.GamepadProbe();
            if (result.Available) return Pass("OUTPUTS", "Virtual Xbox controller", result.Status);
            return usesGamepad
                ? Warning("OUTPUTS", "Virtual Xbox controller", result.Status, "Install or repair ViGEmBus, then rerun this check.")
                : Info("OUTPUTS", "Virtual Xbox controller", result.Status, "Optional: required only by gamepad assignments.");
        });

        var usesMidi = UsesAny(context.Profile, MacroStepType.Midi);
        AddGuarded(checks, "OUTPUTS", "MIDI output", () =>
        {
            var outputs = MidiOutputService.GetOutputDevices(false);
            var preferred = context.Profile.Midi.PreferredOutputName;
            if (!string.IsNullOrWhiteSpace(preferred) && outputs.All(output =>
                    !output.Name.Equals(preferred, StringComparison.OrdinalIgnoreCase)))
                return Warning("OUTPUTS", "MIDI output", $"The selected MIDI output '{preferred}' is not connected.");
            if (outputs.Count > 0)
                return Pass("OUTPUTS", "MIDI output", $"{outputs.Count} Windows MIDI output{Plural(outputs.Count)} available.",
                    string.IsNullOrWhiteSpace(preferred) ? "Using the Windows default when a MIDI macro runs" : $"Selected: {preferred}");
            return usesMidi
                ? Warning("OUTPUTS", "MIDI output", "MIDI assignments exist, but Windows reports no MIDI output.")
                : Info("OUTPUTS", "MIDI output", "No MIDI output is connected; MIDI is optional.");
        });

        var usesOsc = UsesAny(context.Profile, MacroStepType.Osc);
        var endpointCount = context.Profile.Osc.Endpoints.Count;
        checks.Add(usesOsc
            ? Pass("OUTPUTS", "OSC endpoints", $"{endpointCount} OSC endpoint preset{Plural(endpointCount)} configured.")
            : Info("OUTPUTS", "OSC endpoints", $"{endpointCount} endpoint preset{Plural(endpointCount)} ready; no current assignment uses OSC."));

        AddGuarded(checks, "STARTUP", "Start with Windows", () =>
        {
            var registered = context.StartupRegistrationProbe?.Invoke() == true;
            if (registered == context.Profile.StartWithWindows)
                return context.Profile.StartWithWindows
                    ? Pass("STARTUP", "Start with Windows", "The Windows startup entry matches Tippy's profile setting.")
                    : Info("STARTUP", "Start with Windows", "Automatic startup is not enabled.");
            return Warning("STARTUP", "Start with Windows", "The profile setting and Windows startup entry do not match.",
                "Open Settings and save the Start with Windows option again.");
        });

        var scenes = context.Profile.ApplicationProfiles.Count(profile => profile.Enabled);
        checks.Add(scenes > 0
            ? Pass("AUTOMATION", "Application scenes", $"{scenes} enabled application scene{Plural(scenes)} configured.")
            : Info("AUTOMATION", "Application scenes", "No application scenes are configured yet.",
                "Use Find compatible apps to discover software with Tippy shortcut support."));

        checks = checks.Select(check => check with
        {
            Summary = Redact(check.Summary, context),
            Details = Redact(check.Details, context)
        }).ToList();
        var version = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0);
        return new TippyDoctorReport(DateTimeOffset.Now,
            $"{version.Major}.{version.Minor}.{Math.Max(0, version.Build)}",
            Environment.OSVersion.VersionString, context.ProfileStore.IsPortable,
            context.ConnectedDevices.Count, checks);
    }

    private static void AddGuarded(List<TippyDoctorCheck> checks, string category, string name,
        Func<TippyDoctorCheck> check)
    {
        try { checks.Add(check()); }
        catch (Exception exception)
        {
            checks.Add(Fail(category, name, "The check could not be completed.",
                $"{exception.GetType().Name}: {exception.Message}"));
        }
    }

    private static void ProbeWritableDirectory(string directory)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $".tippy-doctor-{Guid.NewGuid():N}.tmp");
        try
        {
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1,
                FileOptions.DeleteOnClose);
            stream.WriteByte(0x54);
        }
        finally
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }

    private static bool IsElevated()
    {
        if (!OperatingSystem.IsWindows()) return false;
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static string Redact(string value, TippyDoctorContext context)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        var result = value;
        var sensitiveValues = new[]
        {
            context.ProfileStore.AppDataDirectory,
            AppContext.BaseDirectory,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.UserName
        };
        foreach (var sensitive in sensitiveValues.Where(item => !string.IsNullOrWhiteSpace(item))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
            result = result.Replace(sensitive, "[redacted]", StringComparison.OrdinalIgnoreCase);
        return result;
    }

    private static bool UsesAny(AppProfile profile, params MacroStepType[] types)
    {
        var wanted = types.ToHashSet();
        return EnumerateMacros(profile).SelectMany(macro => macro.Steps).Any(step => wanted.Contains(step.Type));
    }

    private static IEnumerable<MacroDefinition> EnumerateMacros(AppProfile profile)
    {
        foreach (var bank in profile.Devices.SelectMany(device => device.Banks))
        foreach (var macro in EnumerateBank(bank)) yield return macro;
        foreach (var bank in profile.ApplicationProfiles.SelectMany(scene => scene.DeviceScenes).SelectMany(scene => scene.Banks))
        foreach (var macro in EnumerateBank(bank)) yield return macro;
        foreach (var pattern in profile.PedalPatterns) yield return pattern.Macro;
    }

    private static IEnumerable<MacroDefinition> EnumerateBank(PedalBank bank)
    {
        foreach (var binding in bank.Bindings)
        {
            yield return binding.Macro;
            yield return binding.ReleaseMacro;
            yield return binding.Gestures.DoubleTapMacro;
            yield return binding.Gestures.LongPressMacro;
        }
    }

    private static string Plural(int count) => count == 1 ? string.Empty : "s";
    private static TippyDoctorCheck Pass(string category, string name, string summary, string details = "") =>
        new(category, name, TippyDoctorStatus.Pass, summary, details);
    private static TippyDoctorCheck Warning(string category, string name, string summary, string details = "") =>
        new(category, name, TippyDoctorStatus.Warning, summary, details);
    private static TippyDoctorCheck Fail(string category, string name, string summary, string details = "") =>
        new(category, name, TippyDoctorStatus.Fail, summary, details);
    private static TippyDoctorCheck Info(string category, string name, string summary, string details = "") =>
        new(category, name, TippyDoctorStatus.Info, summary, details);
}
