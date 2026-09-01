namespace Tippy.App.Models;

public sealed record KnownWindowsAction(
    string Category, string Name, string Shortcut, string Description, bool IsDirectKey = false)
{
    public string SearchText => $"{Category} {Name} {Shortcut} {Description}";
    public IReadOnlyList<string> Keys => Shortcut.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}
