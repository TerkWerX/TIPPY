using System.Text.Json;

namespace Tippy.App.Services;

public sealed class CrashRecoveryService
{
    private readonly string _markerPath;
    private readonly object _logGate = new();
    private bool _fatalCrashDetected;

    public CrashRecoveryService(string appDataDirectory)
    {
        Directory.CreateDirectory(appDataDirectory);
        LogsDirectory = Path.Combine(appDataDirectory, "Logs");
        Directory.CreateDirectory(LogsDirectory);
        _markerPath = Path.Combine(appDataDirectory, "running-session.json");
        CrashLogPath = Path.Combine(LogsDirectory, "tippy-crashes.log");
    }

    public string LogsDirectory { get; }
    public string CrashLogPath { get; }

    public PreviousCrashSession? BeginSession()
    {
        PreviousCrashSession? previous = null;
        if (File.Exists(_markerPath))
        {
            try
            {
                previous = JsonSerializer.Deserialize<PreviousCrashSession>(File.ReadAllText(_markerPath));
            }
            catch
            {
                previous = new PreviousCrashSession(0, DateTimeOffset.MinValue, "Previous session did not close cleanly.");
            }
        }
        var current = new PreviousCrashSession(Environment.ProcessId, DateTimeOffset.Now,
            typeof(CrashRecoveryService).Assembly.GetName().Version?.ToString() ?? "unknown");
        File.WriteAllText(_markerPath, JsonSerializer.Serialize(current));
        return previous;
    }

    public void Log(Exception exception, string source, bool fatal = false)
    {
        lock (_logGate)
        {
            if (fatal) _fatalCrashDetected = true;
            try
            {
                File.AppendAllText(CrashLogPath,
                    $"[{DateTimeOffset.Now:O}] {source}{Environment.NewLine}{exception}{Environment.NewLine}{new string('-', 72)}{Environment.NewLine}");
            }
            catch { }
        }
    }

    public void CompleteSession()
    {
        if (_fatalCrashDetected) return;
        try { File.Delete(_markerPath); } catch { }
    }
}

public sealed record PreviousCrashSession(int ProcessId, DateTimeOffset StartedAt, string Version);
