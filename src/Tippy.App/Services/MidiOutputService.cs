using System.Runtime.InteropServices;
using System.Text;
using Tippy.Core.Models;
using Tippy.Core.Output;

namespace Tippy.App.Services;

public sealed class MidiOutputService : IDisposable
{
    private const int MidiMapper = -1;
    private static readonly UIntPtr MidiMapperId = UIntPtr.Size == 8
        ? new UIntPtr(ulong.MaxValue)
        : new UIntPtr(uint.MaxValue);
    private readonly object _gate = new();
    private IntPtr _handle;
    private string _preferredOutputName = string.Empty;
    private string _activeOutputName = string.Empty;

    public sealed record OutputDevice(int DeviceId, string Name, bool IsSystemDefault = false, bool IsAvailable = true)
    {
        public string DisplayName => IsSystemDefault ? "Windows default MIDI output" :
            IsAvailable ? Name : $"{Name} (not connected)";
    }

    public static IReadOnlyList<OutputDevice> GetOutputDevices(bool includeSystemDefault = true)
    {
        var devices = new List<OutputDevice>();
        var count = midiOutGetNumDevs();
        if (includeSystemDefault)
            devices.Add(new OutputDevice(MidiMapper, string.Empty, IsSystemDefault: true, IsAvailable: count > 0));
        var size = (uint)Marshal.SizeOf<MidiOutCaps>();
        for (uint index = 0; index < count; index++)
        {
            var result = midiOutGetDevCapsW((UIntPtr)index, out var caps, size);
            if (result == 0 && !string.IsNullOrWhiteSpace(caps.Name))
                devices.Add(new OutputDevice((int)index, caps.Name.Trim()));
        }
        return devices;
    }

    public void Configure(MidiOutputSettings settings) => Configure(settings.PreferredOutputName);

    public void Configure(string? preferredOutputName)
    {
        var normalized = preferredOutputName?.Trim() ?? string.Empty;
        lock (_gate)
        {
            if (string.Equals(_preferredOutputName, normalized, StringComparison.OrdinalIgnoreCase)) return;
            CloseHandle();
            _preferredOutputName = normalized;
        }
    }

    public void Send(string description)
    {
        Send(MidiMessageParser.Parse(description));
    }

    public void Send(MidiShortMessage message)
    {
        lock (_gate)
        {
            EnsureOpen();
            var result = midiOutShortMsg(_handle, message.PackedValue);
            if (result == 0) return;
            var activeOutputName = _activeOutputName;
            CloseHandle();
            throw MidiError(result, $"Windows could not send the MIDI message to {activeOutputName}");
        }
    }

    public string ActiveOutputName
    {
        get { lock (_gate) return _activeOutputName; }
    }

    private void EnsureOpen()
    {
        if (_handle != IntPtr.Zero) return;
        var outputs = GetOutputDevices(false);
        if (outputs.Count == 0)
            throw new InvalidOperationException("No Windows MIDI output is available. Connect a MIDI device or install a virtual MIDI port, then use Tools → MIDI output setup.");

        var deviceId = MidiMapperId;
        var displayName = "Windows default MIDI output";
        if (!string.IsNullOrWhiteSpace(_preferredOutputName))
        {
            var selected = outputs.FirstOrDefault(device =>
                device.Name.Equals(_preferredOutputName, StringComparison.OrdinalIgnoreCase));
            if (selected is null)
                throw new InvalidOperationException($"The selected MIDI output '{_preferredOutputName}' is not connected. Open Tools → MIDI output setup to select another output.");
            deviceId = (UIntPtr)(uint)selected.DeviceId;
            displayName = selected.Name;
        }

        var result = midiOutOpen(out _handle, deviceId, IntPtr.Zero, IntPtr.Zero, 0);
        if (result != 0)
        {
            _handle = IntPtr.Zero;
            throw MidiError(result, $"Could not open {displayName}");
        }
        _activeOutputName = displayName;
    }

    public void Dispose()
    {
        lock (_gate) CloseHandle();
    }

    private void CloseHandle()
    {
        if (_handle == IntPtr.Zero) return;
        midiOutReset(_handle);
        midiOutClose(_handle);
        _handle = IntPtr.Zero;
        _activeOutputName = string.Empty;
    }

    private static Exception MidiError(int result, string context)
    {
        var text = new StringBuilder(256);
        return midiOutGetErrorTextW(result, text, text.Capacity) == 0
            ? new InvalidOperationException($"{context}: {text} (MIDI error {result}).")
            : new InvalidOperationException($"{context} (MIDI error {result}).");
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MidiOutCaps
    {
        public ushort ManufacturerId;
        public ushort ProductId;
        public uint DriverVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
        public ushort Technology;
        public ushort Voices;
        public ushort Notes;
        public ushort ChannelMask;
        public uint Support;
    }

    [DllImport("winmm.dll", EntryPoint = "midiOutGetNumDevs")]
    private static extern uint midiOutGetNumDevs();

    [DllImport("winmm.dll", EntryPoint = "midiOutGetDevCapsW", CharSet = CharSet.Unicode)]
    private static extern int midiOutGetDevCapsW(UIntPtr deviceId, out MidiOutCaps caps, uint capsSize);

    [DllImport("winmm.dll", EntryPoint = "midiOutGetErrorTextW", CharSet = CharSet.Unicode)]
    private static extern int midiOutGetErrorTextW(int error, StringBuilder text, int textLength);

    [DllImport("winmm.dll", EntryPoint = "midiOutOpen")]
    private static extern int midiOutOpen(out IntPtr handle, UIntPtr deviceId, IntPtr callback, IntPtr instance, int flags);

    [DllImport("winmm.dll", EntryPoint = "midiOutShortMsg")]
    private static extern int midiOutShortMsg(IntPtr handle, uint message);

    [DllImport("winmm.dll", EntryPoint = "midiOutReset")]
    private static extern int midiOutReset(IntPtr handle);

    [DllImport("winmm.dll", EntryPoint = "midiOutClose")]
    private static extern int midiOutClose(IntPtr handle);
}
