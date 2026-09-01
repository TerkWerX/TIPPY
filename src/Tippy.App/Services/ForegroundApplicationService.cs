using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Tippy.App.Services;

public sealed class ForegroundApplicationService
{
    public ForegroundApplicationInfo? GetCurrent()
    {
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero) return null;
        _ = GetWindowThreadProcessId(window, out var processId);
        if (processId == 0) return null;
        try
        {
            using var process = Process.GetProcessById((int)processId);
            var processName = process.ProcessName;
            string? executablePath = null;
            try { executablePath = process.MainModule?.FileName; }
            catch { }
            return new ForegroundApplicationInfo(processName, executablePath);
        }
        catch
        {
            return null;
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
}

public sealed record ForegroundApplicationInfo(string ProcessName, string? ExecutablePath);
