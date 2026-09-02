using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Tippy.App.Models;

namespace Tippy.App.Services;

public sealed record ApplicationInstallCandidate(
    string DisplayName,
    string ExecutablePath,
    string ProcessName,
    string Source);

public sealed record CompatibleApplicationMatch(
    ApplicationShortcutProfile CatalogProfile,
    string DisplayName,
    string ProcessName,
    string ExecutablePath,
    string Evidence)
{
    public int ShortcutCount => CatalogProfile.Shortcuts.Count;
}

public sealed class InstalledApplicationScanner
{
    private sealed record ApplicationIdentity(string CatalogName, string[] ProcessNames, string[] DisplayAliases);

    private static readonly ApplicationIdentity[] Identities =
    [
        I("Microsoft Word", ["WINWORD"], ["Microsoft Word", "Word"]),
        I("Microsoft Excel", ["EXCEL"], ["Microsoft Excel", "Excel"]),
        I("Microsoft PowerPoint", ["POWERPNT"], ["Microsoft PowerPoint", "PowerPoint"]),
        I("Microsoft Outlook", ["OUTLOOK", "olk"], ["Microsoft Outlook", "Outlook"]),
        I("Microsoft OneNote", ["ONENOTE"], ["Microsoft OneNote", "OneNote"]),
        I("Microsoft Access", ["MSACCESS"], ["Microsoft Access", "Access"]),
        I("Microsoft Visio", ["VISIO"], ["Microsoft Visio", "Visio"]),
        I("Microsoft Project", ["WINPROJ"], ["Microsoft Project", "Project"]),
        I("Microsoft Teams", ["ms-teams", "msteams", "Teams"], ["Microsoft Teams", "Teams"]),
        I("Microsoft Publisher", ["MSPUB"], ["Microsoft Publisher", "Publisher"]),
        I("GIMP", ["gimp", "gimp-3.0", "gimp-2.10"], ["GIMP"]),
        I("Blender", ["blender"], ["Blender"]),
        I("Adobe Photoshop", ["Photoshop"], ["Adobe Photoshop", "Photoshop"]),
        I("Adobe Illustrator", ["Illustrator"], ["Adobe Illustrator", "Illustrator"]),
        I("Adobe Premiere Pro", ["Adobe Premiere Pro", "Premiere"], ["Adobe Premiere Pro", "Premiere Pro"]),
        I("Adobe After Effects", ["AfterFX"], ["Adobe After Effects", "After Effects"]),
        I("Autodesk Maya", ["maya"], ["Autodesk Maya", "Maya"]),
        I("Autodesk 3ds Max", ["3dsmax"], ["Autodesk 3ds Max", "3ds Max"]),
        I("Ableton Live", ["Ableton Live"], ["Ableton Live"]),
        I("Reason", ["Reason"], ["Reason Studios", "Reason"]),
        I("Akai MPC Software", ["MPC"], ["Akai MPC", "MPC Software"]),
        I("ACID Pro", ["acid", "acidpro"], ["ACID Pro", "MAGIX ACID"]),
        I("REAPER", ["reaper"], ["REAPER"]),
        I("Audacity", ["audacity"], ["Audacity"]),
        I("OBS Studio", ["obs64", "obs32"], ["OBS Studio"]),
        I("Streamlabs Desktop", ["Streamlabs Desktop", "Streamlabs"], ["Streamlabs Desktop", "Streamlabs"]),
        I("vMix", ["vMix64", "vMix"], ["vMix"]),
        I("XSplit Broadcaster", ["XSplit.Core", "XSplit.Broadcaster"], ["XSplit Broadcaster", "XSplit"]),
        I("Wirecast", ["Wirecast"], ["Wirecast"]),
        I("Visual Studio Code", ["Code"], ["Visual Studio Code", "VS Code"]),
        I("VLC media player", ["vlc"], ["VLC media player", "VLC"]),
        I("Notepad++", ["notepad++"], ["Notepad++"])
    ];

    public IReadOnlyList<CompatibleApplicationMatch> Scan()
    {
        var candidates = new List<ApplicationInstallCandidate>();
        CollectRegistryCandidates(candidates);
        CollectStartMenuCandidates(candidates);
        CollectRunningCandidates(candidates);
        return MatchCandidates(ApplicationShortcutCatalog.Create(), candidates);
    }

    public static IReadOnlyList<CompatibleApplicationMatch> MatchCandidates(
        IReadOnlyList<ApplicationShortcutProfile> catalog,
        IEnumerable<ApplicationInstallCandidate> candidates)
    {
        var available = candidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.DisplayName) ||
                                !string.IsNullOrWhiteSpace(candidate.ProcessName))
            .DistinctBy(candidate => $"{candidate.DisplayName}|{candidate.ProcessName}|{candidate.ExecutablePath}|{candidate.Source}",
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var catalogByName = catalog.ToDictionary(profile => profile.Name, StringComparer.OrdinalIgnoreCase);
        List<CompatibleApplicationMatch> results = [];
        foreach (var identity in Identities)
        {
            if (!catalogByName.TryGetValue(identity.CatalogName, out var profile)) continue;
            var matches = available.Where(candidate => Matches(identity, candidate)).ToArray();
            if (matches.Length == 0) continue;
            var best = matches
                .OrderByDescending(candidate => File.Exists(candidate.ExecutablePath))
                .ThenByDescending(candidate => candidate.Source.Equals("Running now", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(candidate => candidate.Source.Equals("Start menu", StringComparison.OrdinalIgnoreCase))
                .First();
            var process = NormalizeProcessName(best.ProcessName);
            if (string.IsNullOrWhiteSpace(process) ||
                identity.ProcessNames.All(alias => !NormalizeProcessName(alias).Equals(process, StringComparison.OrdinalIgnoreCase)))
                process = NormalizeProcessName(identity.ProcessNames[0]);
            var evidence = string.Join(", ", matches.Select(match => match.Source).Distinct(StringComparer.OrdinalIgnoreCase));
            results.Add(new CompatibleApplicationMatch(profile,
                string.IsNullOrWhiteSpace(best.DisplayName) ? profile.Name : best.DisplayName,
                process, NormalizeExecutablePath(best.ExecutablePath), evidence));
        }
        return results.OrderBy(result => result.CatalogProfile.Category)
            .ThenBy(result => result.CatalogProfile.Name).ToArray();
    }

    private static void CollectRegistryCandidates(List<ApplicationInstallCandidate> candidates)
    {
        var locations = new[]
        {
            (RegistryHive.LocalMachine, RegistryView.Registry64),
            (RegistryHive.LocalMachine, RegistryView.Registry32),
            (RegistryHive.CurrentUser, RegistryView.Default)
        };
        foreach (var (hive, view) in locations)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var uninstall = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstall is null) continue;
                foreach (var subKeyName in uninstall.GetSubKeyNames())
                {
                    try
                    {
                        using var subKey = uninstall.OpenSubKey(subKeyName);
                        var displayName = subKey?.GetValue("DisplayName") as string;
                        if (subKey is null || string.IsNullOrWhiteSpace(displayName)) continue;
                        var path = NormalizeExecutablePath(subKey.GetValue("DisplayIcon") as string ?? string.Empty);
                        candidates.Add(new ApplicationInstallCandidate(displayName.Trim(), path,
                            NormalizeProcessName(path), "Installed programs"));
                    }
                    catch { }
                }
            }
            catch { }
        }
    }

    private static void CollectStartMenuCandidates(List<ApplicationInstallCandidate> candidates)
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu)
        };
        foreach (var root in roots.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            IEnumerable<string> shortcuts;
            try
            {
                shortcuts = Directory.EnumerateFiles(root, "*.lnk", new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    ReturnSpecialDirectories = false
                }).Take(5000).ToArray();
            }
            catch { continue; }
            foreach (var shortcutPath in shortcuts)
            {
                var displayName = Path.GetFileNameWithoutExtension(shortcutPath);
                var target = ResolveShortcutTarget(shortcutPath);
                candidates.Add(new ApplicationInstallCandidate(displayName, target,
                    NormalizeProcessName(target), "Start menu"));
            }
        }
    }

    private static void CollectRunningCandidates(List<ApplicationInstallCandidate> candidates)
    {
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.Id == Environment.ProcessId || process.MainWindowHandle == IntPtr.Zero) continue;
                string path;
                try { path = process.MainModule?.FileName ?? string.Empty; } catch { path = string.Empty; }
                candidates.Add(new ApplicationInstallCandidate(
                    string.IsNullOrWhiteSpace(process.MainWindowTitle) ? process.ProcessName : process.MainWindowTitle,
                    path, process.ProcessName, "Running now"));
            }
            catch { }
            finally { process.Dispose(); }
        }
    }

    private static string ResolveShortcutTarget(string shortcutPath)
    {
        object? shell = null;
        object? shortcut = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null) return string.Empty;
            shell = Activator.CreateInstance(shellType);
            shortcut = shellType.InvokeMember("CreateShortcut",
                System.Reflection.BindingFlags.InvokeMethod, null, shell, [shortcutPath]);
            return NormalizeExecutablePath(shortcut?.GetType().InvokeMember("TargetPath",
                System.Reflection.BindingFlags.GetProperty, null, shortcut, null) as string ?? string.Empty);
        }
        catch { return string.Empty; }
        finally
        {
            try { if (shortcut is not null && Marshal.IsComObject(shortcut)) Marshal.FinalReleaseComObject(shortcut); } catch { }
            try { if (shell is not null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell); } catch { }
        }
    }

    private static bool Matches(ApplicationIdentity identity, ApplicationInstallCandidate candidate)
    {
        var process = NormalizeProcessName(string.IsNullOrWhiteSpace(candidate.ProcessName)
            ? candidate.ExecutablePath
            : candidate.ProcessName);
        if (identity.ProcessNames.Any(alias => NormalizeProcessName(alias).Equals(process, StringComparison.OrdinalIgnoreCase)))
            return true;
        return identity.DisplayAliases.Any(alias => ContainsDisplayAlias(candidate.DisplayName, alias));
    }

    private static string NormalizeExecutablePath(string value)
    {
        var text = Environment.ExpandEnvironmentVariables(value?.Trim() ?? string.Empty);
        if (text.StartsWith('"'))
        {
            var end = text.IndexOf('"', 1);
            if (end > 1) text = text[1..end];
        }
        else
        {
            var exe = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (exe >= 0) text = text[..(exe + 4)];
        }
        return text.Trim().Trim('"');
    }

    private static string NormalizeProcessName(string value) =>
        Path.GetFileNameWithoutExtension(NormalizeExecutablePath(value));

    private static bool ContainsDisplayAlias(string displayName, string alias)
    {
        if (string.IsNullOrWhiteSpace(displayName) || alias.Trim().Length < 4) return false;
        var pattern = $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(alias.Trim())}(?![\p{{L}}\p{{N}}])";
        return Regex.IsMatch(displayName, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(100));
    }

    private static ApplicationIdentity I(string name, string[] processes, string[] aliases) =>
        new(name, processes, aliases);
}
