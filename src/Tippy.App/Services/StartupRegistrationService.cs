using Microsoft.Win32;

namespace Tippy.App.Services;

public sealed class StartupRegistrationService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Tippy";

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
        return key?.GetValue(ValueName) is string command && !string.IsNullOrWhiteSpace(command);
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, true)
                        ?? throw new InvalidOperationException("Windows startup settings could not be opened.");
        if (!enabled)
        {
            key.DeleteValue(ValueName, false);
            return;
        }
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
            throw new InvalidOperationException("Tippy's executable path could not be determined.");
        key.SetValue(ValueName, $"\"{executable}\" --minimized", RegistryValueKind.String);
    }
}
