using System;
using System.Diagnostics;
using System.IO;
using Tippy.Core.Models;

namespace Tippy.App.Services;

public static class PowerShellCommandService
{
    public const string WindowsPowerShell = "Windows PowerShell 5.1 (built in)";
    public const string PowerShell7 = "PowerShell 7 (pwsh, if installed)";

    public static ProcessStartInfo BuildStartInfo(MacroStep step)
    {
        var command = step.Value?.Trim();
        if (string.IsNullOrWhiteSpace(command))
        {
            throw new InvalidOperationException("The PowerShell step has no command.");
        }

        var executable = step.Arguments switch
        {
            PowerShell7 or "pwsh" or "pwsh.exe" => "pwsh.exe",
            WindowsPowerShell or null or "" or "powershell" or "powershell.exe" => "powershell.exe",
            _ => throw new InvalidOperationException("The PowerShell step specifies an unsupported PowerShell host.")
        };

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(command);

        var workingDirectory = Environment.ExpandEnvironmentVariables(step.WorkingDirectory?.Trim() ?? string.Empty);
        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            if (!Directory.Exists(workingDirectory))
            {
                throw new DirectoryNotFoundException($"The PowerShell working directory does not exist: {workingDirectory}");
            }
            startInfo.WorkingDirectory = workingDirectory;
        }

        return startInfo;
    }

    public static void Launch(MacroStep step)
    {
        if (Process.Start(BuildStartInfo(step)) is null)
        {
            throw new InvalidOperationException("Windows could not start PowerShell.");
        }
    }
}
