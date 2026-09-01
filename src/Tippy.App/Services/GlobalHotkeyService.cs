using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace Tippy.App.Services;

public sealed class GlobalHotkeyService : IDisposable
{
    private static int _nextHotkeyId = 0x54A0;
    private readonly int _hotkeyId = Interlocked.Increment(ref _nextHotkeyId);
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const uint ModNoRepeat = 0x4000;
    private HwndSource? _source;
    private IntPtr _handle;
    private Action? _callback;

    public bool Register(Window owner, string shortcut, Action callback, out string? error)
    {
        Unregister();
        error = null;
        try
        {
            var parts = shortcut.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                throw new ArgumentException("Choose a bank-switch shortcut.");
            }
            uint modifiers = ModNoRepeat;
            foreach (var modifier in parts[..^1])
            {
                modifiers |= modifier.ToUpperInvariant() switch
                {
                    "CTRL" or "CONTROL" => ModControl,
                    "ALT" => ModAlt,
                    "SHIFT" => ModShift,
                    "WIN" or "WINDOWS" => ModWin,
                    _ => throw new ArgumentException($"Unknown hotkey modifier: {modifier}")
                };
            }
            var virtualKey = WindowsInputService.ResolveKey(parts[^1]);
            _handle = new WindowInteropHelper(owner).Handle;
            _source = HwndSource.FromHwnd(_handle);
            _source?.AddHook(WindowHook);
            if (!RegisterHotKey(_handle, _hotkeyId, modifiers, virtualKey))
            {
                throw new InvalidOperationException($"{shortcut} is already reserved by another application.");
            }
            _callback = callback;
            return true;
        }
        catch (Exception exception)
        {
            Unregister();
            error = exception.Message;
            return false;
        }
    }

    private IntPtr WindowHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmHotkey && wParam.ToInt32() == _hotkeyId)
        {
            handled = true;
            _callback?.Invoke();
        }
        return IntPtr.Zero;
    }

    private void Unregister()
    {
        if (_handle != IntPtr.Zero)
        {
            UnregisterHotKey(_handle, _hotkeyId);
        }
        _source?.RemoveHook(WindowHook);
        _source = null;
        _handle = IntPtr.Zero;
        _callback = null;
    }

    public void Dispose() => Unregister();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr window, int id);
}
