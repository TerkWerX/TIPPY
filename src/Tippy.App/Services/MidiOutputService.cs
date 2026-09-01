using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Tippy.App.Services;

public sealed class MidiOutputService : IDisposable
{
    private readonly object _gate = new();
    private IntPtr _handle;

    public void Send(string description)
    {
        var parts = (description ?? string.Empty).Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length < 3) throw new ArgumentException("MIDI format must be note:channel:number[:velocity], cc:channel:number:value, or pc:channel:program.");
        var kind = parts[0].ToLowerInvariant();
        var channel = Math.Clamp(Parse(parts[1], "channel") - 1, 0, 15);
        int status;
        int data1;
        int data2;
        switch (kind)
        {
            case "note":
                status = 0x90 | channel;
                data1 = MidiByte(parts[2], "note");
                data2 = parts.Length > 3 ? MidiByte(parts[3], "velocity") : 127;
                break;
            case "cc":
                if (parts.Length < 4) throw new ArgumentException("MIDI CC requires cc:channel:controller:value.");
                status = 0xB0 | channel;
                data1 = MidiByte(parts[2], "controller");
                data2 = MidiByte(parts[3], "value");
                break;
            case "pc":
                status = 0xC0 | channel;
                data1 = MidiByte(parts[2], "program");
                data2 = 0;
                break;
            default:
                throw new ArgumentException("MIDI message must start with note, cc, or pc.");
        }
        lock (_gate)
        {
            EnsureOpen();
            var message = (uint)(status | data1 << 8 | data2 << 16);
            var result = MidiOutShortMsg(_handle, message);
            if (result != 0) throw new Win32Exception(result, "Windows could not send the MIDI message.");
        }
    }

    private void EnsureOpen()
    {
        if (_handle != IntPtr.Zero) return;
        var result = MidiOutOpen(out _handle, -1, IntPtr.Zero, IntPtr.Zero, 0);
        if (result != 0) throw new Win32Exception(result, "No Windows MIDI output is available.");
    }

    private static int Parse(string value, string label) =>
        int.TryParse(value, out var parsed) ? parsed : throw new ArgumentException($"Invalid MIDI {label}: {value}");

    private static int MidiByte(string value, string label) => Math.Clamp(Parse(value, label), 0, 127);

    public void Dispose()
    {
        lock (_gate)
        {
            if (_handle == IntPtr.Zero) return;
            MidiOutReset(_handle);
            MidiOutClose(_handle);
            _handle = IntPtr.Zero;
        }
    }

    [DllImport("winmm.dll")]
    private static extern int midiOutOpen(out IntPtr handle, int deviceId, IntPtr callback, IntPtr instance, int flags);
    private static int MidiOutOpen(out IntPtr handle, int deviceId, IntPtr callback, IntPtr instance, int flags) =>
        midiOutOpen(out handle, deviceId, callback, instance, flags);

    [DllImport("winmm.dll")]
    private static extern int midiOutShortMsg(IntPtr handle, uint message);
    private static int MidiOutShortMsg(IntPtr handle, uint message) => midiOutShortMsg(handle, message);

    [DllImport("winmm.dll")]
    private static extern int midiOutReset(IntPtr handle);
    private static int MidiOutReset(IntPtr handle) => midiOutReset(handle);

    [DllImport("winmm.dll")]
    private static extern int midiOutClose(IntPtr handle);
    private static int MidiOutClose(IntPtr handle) => midiOutClose(handle);
}
