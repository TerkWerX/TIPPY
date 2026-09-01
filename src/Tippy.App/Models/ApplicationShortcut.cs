namespace Tippy.App.Models;

public sealed record ApplicationShortcut(string Category, string Name, string Shortcut, string Description)
{
    public string SearchText => $"{Category} {Name} {Shortcut} {Description}";
    public IReadOnlyList<string> Keys => Shortcut.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}

public sealed record ApplicationShortcutProfile(
    string Name, string Publisher, string Category, string VersionNote, string SourceUrl,
    IReadOnlyList<ApplicationShortcut> Shortcuts)
{
    public string SearchText => $"{Name} {Publisher} {Category}";
}
