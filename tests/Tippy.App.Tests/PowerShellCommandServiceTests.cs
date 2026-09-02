using Tippy.App.Services;
using Tippy.Core.Models;

namespace Tippy.App.Tests;

public sealed class PowerShellCommandServiceTests
{
    [Fact]
    public void BuildsLockedDownWindowsPowerShellInvocationWithoutRequotingCommand()
    {
        const string command = "Get-Process | Where-Object { $_.Name -eq 'Tippy App' }";
        var info = PowerShellCommandService.BuildStartInfo(new MacroStep
        {
            Type = MacroStepType.PowerShellCommand,
            Value = command,
            Arguments = PowerShellCommandService.WindowsPowerShell
        });

        Assert.Equal("powershell.exe", info.FileName);
        Assert.False(info.UseShellExecute);
        Assert.True(info.CreateNoWindow);
        Assert.Equal(["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", command], info.ArgumentList);
        Assert.DoesNotContain("Bypass", info.ArgumentList);
        Assert.Equal(string.Empty, info.Verb);
    }

    [Fact]
    public void SupportsPowerShellSevenAndExistingWorkingDirectory()
    {
        var info = PowerShellCommandService.BuildStartInfo(new MacroStep
        {
            Type = MacroStepType.PowerShellCommand,
            Value = "Write-Output 'ready'",
            Arguments = PowerShellCommandService.PowerShell7,
            WorkingDirectory = Environment.CurrentDirectory
        });

        Assert.Equal("pwsh.exe", info.FileName);
        Assert.Equal(Environment.CurrentDirectory, info.WorkingDirectory);
    }

    [Fact]
    public void BuiltInPowerShellExecutesAHarmlessCommand()
    {
        var info = PowerShellCommandService.BuildStartInfo(new MacroStep
        {
            Type = MacroStepType.PowerShellCommand,
            Value = "$PSVersionTable.PSVersion.Major | Out-Null"
        });

        using var process = System.Diagnostics.Process.Start(info);
        Assert.NotNull(process);
        Assert.True(process.WaitForExit(10_000));
        Assert.Equal(0, process.ExitCode);
    }

    [Fact]
    public void RejectsBlankCommandsAndUnrecognizedHosts()
    {
        Assert.Throws<InvalidOperationException>(() => PowerShellCommandService.BuildStartInfo(new MacroStep
            { Type = MacroStepType.PowerShellCommand, Value = " " }));
        Assert.Throws<InvalidOperationException>(() => PowerShellCommandService.BuildStartInfo(new MacroStep
            { Type = MacroStepType.PowerShellCommand, Value = "Get-Date", Arguments = "custom.exe" }));
    }
}
