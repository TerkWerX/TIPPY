using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;

namespace Tippy.App.Services;

public sealed record RawInputKeyboardDevice(IntPtr Handle, string DevicePath, string DisplayName);
public sealed record RawInputKeyEvent(string DevicePath, int VirtualKey, bool IsPressed, long Timestamp);

public sealed class RawInputService : IDisposable
{
    private const int WmInput = 0x00FF;
    private const int WmInputDeviceChange = 0x00FE;
    private const uint RidInput = 0x10000003;
    private const uint RidiDeviceName = 0x20000007;
    private const uint RimTypeKeyboard = 1;
    private const ushort HidUsagePageGeneric = 0x01;
    private const ushort HidUsageKeyboard = 0x06;
    private const uint RidevInputSink = 0x00000100;
    private const uint RidevDevNotify = 0x00002000;
    private const ushort RiKeyBreak = 0x0001;
    private HwndSource? _source;
    private IntPtr _handle;

    public event EventHandler<RawInputKeyEvent>? KeyChanged;
    public event EventHandler? DevicesChanged;

    public IReadOnlyList<RawInputKeyboardDevice> Devices { get; private set; } = [];

    public void Initialize(Window owner)
    {
        DisposeHook();
        _handle = new WindowInteropHelper(owner).Handle;
        if (_handle == IntPtr.Zero) throw new InvalidOperationException("The Tippy window is not ready for Raw Input.");
        var device = new RawInputDevice
        {
            UsagePage = HidUsagePageGeneric,
            Usage = HidUsageKeyboard,
            Flags = RidevInputSink | RidevDevNotify,
            Target = _handle
        };
        if (!RegisterRawInputDevices([device], 1, (uint)Marshal.SizeOf<RawInputDevice>()))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Windows rejected Raw Input registration.");
        _source = HwndSource.FromHwnd(_handle);
        _source?.AddHook(WindowHook);
        RefreshDevices();
    }

    public void RefreshDevices()
    {
        uint count = 0;
        var size = (uint)Marshal.SizeOf<RawInputDeviceList>();
        if (GetRawInputDeviceList(null, ref count, size) == uint.MaxValue || count == 0)
        {
            Devices = [];
            DevicesChanged?.Invoke(this, EventArgs.Empty);
            return;
        }
        var items = new RawInputDeviceList[count];
        if (GetRawInputDeviceList(items, ref count, size) == uint.MaxValue) return;
        Devices = items.Take((int)count)
            .Where(item => item.Type == RimTypeKeyboard)
            .Select(item =>
            {
                var path = GetDeviceName(item.Device);
                return new RawInputKeyboardDevice(item.Device, path, FriendlyName(path));
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.DevicePath))
            .ToArray();
        DevicesChanged?.Invoke(this, EventArgs.Empty);
    }

    private IntPtr WindowHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmInput)
        {
            uint bytes = 0;
            var headerSize = (uint)Marshal.SizeOf<RawInputHeader>();
            if (GetRawInputData(lParam, RidInput, IntPtr.Zero, ref bytes, headerSize) != 0 || bytes == 0) return IntPtr.Zero;
            var buffer = Marshal.AllocHGlobal((int)bytes);
            try
            {
                if (GetRawInputData(lParam, RidInput, buffer, ref bytes, headerSize) != bytes) return IntPtr.Zero;
                var input = Marshal.PtrToStructure<RawInput>(buffer);
                if (input.Header.Type != RimTypeKeyboard) return IntPtr.Zero;
                var path = Devices.FirstOrDefault(device => device.Handle == input.Header.Device)?.DevicePath ?? GetDeviceName(input.Header.Device);
                var pressed = (input.Keyboard.Flags & RiKeyBreak) == 0;
                KeyChanged?.Invoke(this, new RawInputKeyEvent(path, input.Keyboard.VirtualKey, pressed,
                    System.Diagnostics.Stopwatch.GetTimestamp()));
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        else if (message == WmInputDeviceChange)
        {
            RefreshDevices();
        }
        return IntPtr.Zero;
    }

    private static string GetDeviceName(IntPtr device)
    {
        uint length = 0;
        GetRawInputDeviceInfo(device, RidiDeviceName, null, ref length);
        if (length == 0) return string.Empty;
        var name = new StringBuilder((int)length + 1);
        return GetRawInputDeviceInfo(device, RidiDeviceName, name, ref length) == uint.MaxValue ? string.Empty : name.ToString();
    }

    private static string FriendlyName(string path)
    {
        var vid = System.Text.RegularExpressions.Regex.Match(path, "VID_([0-9A-F]{4})", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Groups[1].Value;
        var pid = System.Text.RegularExpressions.Regex.Match(path, "PID_([0-9A-F]{4})", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Groups[1].Value;
        return string.IsNullOrWhiteSpace(vid)
            ? $"Keyboard-style HID · {Short(path)}"
            : $"Keyboard-style HID · VID_{vid.ToUpperInvariant()} PID_{pid.ToUpperInvariant()}";
    }

    private static string Short(string path) => path.Length <= 28 ? path : path[^28..];

    private void DisposeHook()
    {
        _source?.RemoveHook(WindowHook);
        _source = null;
        _handle = IntPtr.Zero;
    }

    public void Dispose() => DisposeHook();

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDevice { public ushort UsagePage; public ushort Usage; public uint Flags; public IntPtr Target; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDeviceList { public IntPtr Device; public uint Type; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputHeader { public uint Type; public uint Size; public IntPtr Device; public IntPtr WParam; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawKeyboard
    {
        public ushort MakeCode;
        public ushort Flags;
        public ushort Reserved;
        public ushort VirtualKey;
        public uint Message;
        public uint ExtraInformation;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInput { public RawInputHeader Header; public RawKeyboard Keyboard; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices(RawInputDevice[] devices, uint count, uint size);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputDeviceList([In, Out] RawInputDeviceList[]? devices, ref uint count, uint size);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputData(IntPtr rawInput, uint command, IntPtr data, ref uint size, uint headerSize);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetRawInputDeviceInfo(IntPtr device, uint command, StringBuilder? data, ref uint size);
}
