using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Tippy.App.Services;

public sealed class WindowsInputService
{
    private const uint InputKeyboard = 1;
    private const uint InputMouse = 0;
    private const uint KeyeventfKeyup = 0x0002;
    private const uint KeyeventfUnicode = 0x0004;
    private const uint MouseeventfLeftdown = 0x0002;
    private const uint MouseeventfLeftup = 0x0004;
    private const uint MouseeventfRightdown = 0x0008;
    private const uint MouseeventfRightup = 0x0010;
    private const uint MouseeventfMiddledown = 0x0020;
    private const uint MouseeventfMiddleup = 0x0040;
    private const uint MouseeventfXdown = 0x0080;
    private const uint MouseeventfXup = 0x0100;
    private const uint MouseeventfWheel = 0x0800;

    private static readonly IReadOnlyDictionary<string, ushort> NamedKeys =
        new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase)
        {
            ["Ctrl"] = 0x11, ["Control"] = 0x11, ["Alt"] = 0x12,
            ["Shift"] = 0x10, ["Win"] = 0x5B, ["Windows"] = 0x5B,
            ["Enter"] = 0x0D, ["Return"] = 0x0D, ["Escape"] = 0x1B, ["Esc"] = 0x1B,
            ["Tab"] = 0x09, ["Space"] = 0x20, ["Backspace"] = 0x08,
            ["Delete"] = 0x2E, ["Insert"] = 0x2D, ["Home"] = 0x24, ["End"] = 0x23,
            ["PageUp"] = 0x21, ["PageDown"] = 0x22,
            ["Left"] = 0x25, ["Up"] = 0x26, ["Right"] = 0x27, ["Down"] = 0x28,
            ["CapsLock"] = 0x14, ["NumLock"] = 0x90, ["ScrollLock"] = 0x91,
            ["PrintScreen"] = 0x2C, ["Pause"] = 0x13,
            ["Apps"] = 0x5D, ["Menu"] = 0x5D, ["Sleep"] = 0x5F,
            ["Numpad0"] = 0x60, ["Numpad1"] = 0x61, ["Numpad2"] = 0x62,
            ["Numpad3"] = 0x63, ["Numpad4"] = 0x64, ["Numpad5"] = 0x65,
            ["Numpad6"] = 0x66, ["Numpad7"] = 0x67, ["Numpad8"] = 0x68,
            ["Numpad9"] = 0x69, ["Multiply"] = 0x6A, ["Add"] = 0x6B,
            ["Subtract"] = 0x6D, ["Decimal"] = 0x6E, ["Divide"] = 0x6F,
            ["BrowserBack"] = 0xA6, ["BrowserForward"] = 0xA7,
            ["BrowserRefresh"] = 0xA8, ["BrowserStop"] = 0xA9,
            ["BrowserSearch"] = 0xAA, ["BrowserFavorites"] = 0xAB, ["BrowserHome"] = 0xAC,
            ["VolumeMute"] = 0xAD, ["VolumeDown"] = 0xAE, ["VolumeUp"] = 0xAF,
            ["MediaNext"] = 0xB0, ["MediaPrevious"] = 0xB1,
            ["MediaStop"] = 0xB2, ["MediaPlayPause"] = 0xB3,
            ["LaunchMail"] = 0xB4, ["MediaSelect"] = 0xB5,
            ["LaunchApp1"] = 0xB6, ["LaunchApp2"] = 0xB7,
            [";"] = 0xBA, ["="] = 0xBB, [","] = 0xBC, ["-"] = 0xBD,
            ["."] = 0xBE, ["/"] = 0xBF, ["`"] = 0xC0,
            ["["] = 0xDB, ["\\"] = 0xDC, ["]"] = 0xDD, ["'"] = 0xDE
        };

    public void KeyDown(IEnumerable<string> keys) => SendKeys(keys, false);
    public void KeyUp(IEnumerable<string> keys) => SendKeys(keys.Reverse(), true);

    public async Task KeyChordAsync(IEnumerable<string> keys, int durationMs, CancellationToken token)
    {
        var keyList = keys.Where(key => !string.IsNullOrWhiteSpace(key)).ToArray();
        KeyDown(keyList);
        try
        {
            await Task.Delay(Math.Clamp(durationMs, 1, 5_000), token).ConfigureAwait(false);
        }
        finally
        {
            KeyUp(keyList);
        }
    }

    public void TypeText(string text)
    {
        var inputs = new List<Input>(text.Length * 2);
        foreach (var character in text)
        {
            inputs.Add(KeyboardUnicode(character, false));
            inputs.Add(KeyboardUnicode(character, true));
        }
        Send(inputs);
    }

    public void MouseClick(string button)
    {
        var normalized = button.Trim();
        var (down, up, data) = normalized.ToUpperInvariant() switch
        {
            "RIGHT" => (MouseeventfRightdown, MouseeventfRightup, 0u),
            "MIDDLE" => (MouseeventfMiddledown, MouseeventfMiddleup, 0u),
            "X1" => (MouseeventfXdown, MouseeventfXup, 1u),
            "X2" => (MouseeventfXdown, MouseeventfXup, 2u),
            _ => (MouseeventfLeftdown, MouseeventfLeftup, 0u)
        };
        Send([Mouse(down, data), Mouse(up, data)]);
    }

    public void MouseWheel(int amount) => Send([Mouse(MouseeventfWheel, unchecked((uint)amount))]);

    private static void SendKeys(IEnumerable<string> keys, bool up)
    {
        var inputs = keys.Select(key => KeyboardVirtual(ResolveKey(key), up)).ToArray();
        Send(inputs);
    }

    public static ushort ResolveKey(string key)
    {
        if (NamedKeys.TryGetValue(key.Trim(), out var named))
        {
            return named;
        }
        var value = key.Trim();
        if (value.Length == 1)
        {
            var character = char.ToUpperInvariant(value[0]);
            if (character is >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                return character;
            }
        }
        if (value.StartsWith('F') && int.TryParse(value.AsSpan(1), out var function) &&
            function is >= 1 and <= 24)
        {
            return (ushort)(0x70 + function - 1);
        }
        throw new ArgumentException($"Unsupported key: {key}", nameof(key));
    }

    private static Input KeyboardVirtual(ushort virtualKey, bool up) => new()
    {
        Type = InputKeyboard,
        Data = new InputUnion
        {
            Keyboard = new KeyboardInput
            {
                VirtualKey = virtualKey,
                Flags = up ? KeyeventfKeyup : 0
            }
        }
    };

    private static Input KeyboardUnicode(char character, bool up) => new()
    {
        Type = InputKeyboard,
        Data = new InputUnion
        {
            Keyboard = new KeyboardInput
            {
                ScanCode = character,
                Flags = KeyeventfUnicode | (up ? KeyeventfKeyup : 0)
            }
        }
    };

    private static Input Mouse(uint flags, uint data) => new()
    {
        Type = InputMouse,
        Data = new InputUnion { Mouse = new MouseInput { MouseData = data, Flags = flags } }
    };

    private static void Send(IReadOnlyCollection<Input> inputs)
    {
        if (inputs.Count == 0)
        {
            return;
        }
        var array = inputs.ToArray();
        var sent = SendInput((uint)array.Length, array, Marshal.SizeOf<Input>());
        if (sent != array.Length)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows rejected injected input.");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, Input[] inputs, int size);
}
