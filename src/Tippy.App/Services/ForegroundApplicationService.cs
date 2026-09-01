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
            var windowTitle = GetWindowTitle(window);
            return new ForegroundApplicationInfo(processName, executablePath, windowTitle);
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

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(IntPtr window, System.Text.StringBuilder text, int maximumCount);

    private static string GetWindowTitle(IntPtr window)
    {
        var text = new System.Text.StringBuilder(512);
        return GetWindowTextW(window, text, text.Capacity) <= 0 ? string.Empty : text.ToString();
    }
}

public sealed record ForegroundApplicationInfo(string ProcessName, string? ExecutablePath, string WindowTitle);
